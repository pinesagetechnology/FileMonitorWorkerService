using FileMonitorWorkerService.Data.Repository;
using FileMonitorWorkerService.Models;
using FileMonitorWorkerService.Utility;
using System.Collections.Concurrent;

namespace FileMonitorWorkerService.Services
{
    public interface IFolderWatcherService
    {
        Task StartAsync(FileDataSourceConfig config, Func<int, string, Task> onError);
        Task StopAsync();
        bool IsRunning { get; }
    }

    public class FolderWatcherService : IFolderWatcherService, IDisposable
    {
        private readonly IRepository<UploadQueue> _repository;
        private readonly IRepository<WatcherError> _errorRepository;
        private readonly IConfigurationService _configurationService;

        private readonly ILogger<FolderWatcherService> _logger;

        private FileDataSourceConfig _config = new();
        private Func<int, string, Task> _onError = null!;
        private FileSystemWatcher? _watcher;

        private readonly ConcurrentDictionary<string, DateTime> _processingFiles = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _isRunning = false;

        public bool IsRunning => _isRunning;

        public FolderWatcherService(
            IConfigurationService configurationService,
            IRepository<UploadQueue> repository,
            IRepository<WatcherError> errorRepository,
            ILogger<FolderWatcherService> logger)
        {
            _configurationService = configurationService;
            _repository = repository;
            _errorRepository = errorRepository;
            _logger = logger;
        }

        public async Task StartAsync(FileDataSourceConfig config, Func<int, string, Task> onError)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _onError = onError ?? throw new ArgumentNullException(nameof(onError));

            if (_isRunning) return;

            await _semaphore.WaitAsync();
            try
            {
                if (_isRunning) return;

                if (_config.IsEnabled && !string.IsNullOrEmpty(_config.FolderPath))
                {
                    var folderPath = _config.FolderPath;
                    try
                    {
                        // Use the utility method to normalize and create the folder path
                        folderPath = FileHelper.NormalizeFolderPath(folderPath, createIfNotExists: true);
                        _logger.LogInformation("Folder path validated and ready: {Path}", folderPath);
                    }
                    catch (Exception ex)
                    {
                        var error = $"Failed to validate folder path: {folderPath}. Error: {ex.Message}";
                        _logger.LogError(ex, "Failed to validate folder path: {Path}", folderPath);
                        await _onError(_config.Id, error);
                        await PersistErrorAsync(error, folderPath, ex);
                        throw;
                    }

                    _watcher = new FileSystemWatcher(folderPath)
                    {
                        Filter = _config.FilePattern ?? "*.*",
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = false
                    };

                    _watcher.Created += OnItemCreated;
                    _watcher.Changed += OnItemChanged;
                    _watcher.Error += OnError;

                    _watcher.EnableRaisingEvents = true;
                    _isRunning = true;

                    _logger.LogInformation("Started FolderWatcherService for data source: {Name} at {Path}", _config.Name, folderPath);

                    await ProcessExistingFilesAsync(folderPath);
                }
                else
                {
                    _logger.LogWarning($"DataSource {_config.Name} is disabled or FolderPath is empty.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting FolderWatcherService for data source: {_config.Name}");
                await _onError(_config.Id, ex.Message);
            }
            finally
            {
                _semaphore.Release();
            }
            
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            await _semaphore.WaitAsync();
            try
            {
                if (!_isRunning) return;

                _watcher?.Dispose();
                _watcher = null;
                _isRunning = false;
                _processingFiles.Clear();

                _logger.LogInformation("Stopped folder watcher for {Name}", _config.Name);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        #region PrivateMethods

        private async void OnItemCreated(object sender, FileSystemEventArgs e)
        {
            await ProcessItemAsync(e.FullPath, "Created");
        }

        private async void OnItemChanged(object sender, FileSystemEventArgs e)
        {
            await ProcessItemAsync(e.FullPath, "Changed");
        }

        private async void OnError(object sender, ErrorEventArgs e)
        {
            var error = $"FileSystemWatcher error: {e.GetException().Message}";
            await _onError(_config.Id, error);
            await PersistErrorAsync(error, _config.FolderPath ?? string.Empty, e.GetException());
            _logger.LogError(e.GetException(), "FileSystemWatcher error in {Name}", _config.Name);
        }

        private async Task ProcessItemAsync(string itemPath, string eventType)
        {
            try
            {
                // Check if it's a directory
                if (Directory.Exists(itemPath))
                {
                    await ProcessDirectoryAsync(itemPath, eventType);
                }
                // Check if it's a file
                else if (File.Exists(itemPath))
                {
                    await ProcessFileAsync(itemPath, eventType);
                }
                else
                {
                    // Item doesn't exist yet (might be in the process of being created)
                    _logger.LogDebug("Item not found yet, skipping: {Path}", itemPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing item: {Path} ({EventType})", itemPath, eventType);
                await _onError(_config.Id, $"Error processing item {itemPath}: {ex.Message}");
            }
        }

        private async Task ProcessDirectoryAsync(string directoryPath, string eventType)
        {
            _logger.LogInformation("Directory {EventType}: {Path}", eventType, directoryPath);

            // When a new directory is created, process all files in it
            if (eventType == "Created")
            {
                await ProcessExistingFilesAsync(directoryPath);
            }
        }

        private async Task ProcessFileAsync(string filePath, string eventType)
        {
            try
            {
                // Prevent duplicate processing
                var fileName = Path.GetFileName(filePath);
                var now = DateTime.UtcNow;

                if (_processingFiles.TryGetValue(filePath, out var lastProcessed))
                {
                    if (now - lastProcessed < TimeSpan.FromSeconds(2))
                    {
                        return; // Skip if processed recently
                    }
                }

                _processingFiles[filePath] = now;

                await WaitForFileToBeReady(filePath);

                if (!await IsValidFileAsync(filePath))
                {
                    _logger.LogDebug("Skipping invalid file: {FilePath}", filePath);
                    return;
                }

                // Calculate hash for duplicate detection
                var hash = await FileHelper.CalculateFileHashAsync(filePath);
                var fileInfo = new FileInfo(filePath);
                var fileType = FileHelper.GetFileType(fileName);

                if(await IsDuplicateAsync(hash))
                {
                    _logger.LogInformation("Duplicate file detected (skipping): {FilePath}", filePath);
                    return;
                }

                var uploadEntry = new UploadQueue
                {
                    FilePath = filePath,
                    FileName = fileName,
                    FileType = fileType,
                    FileSizeBytes = fileInfo.Length,
                    Status = FileStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    Hash = hash,
                    MaxRetries = 5
                };

                await _repository.AddAsync(uploadEntry);
            }
            catch (Exception ex)
            {
                var error = $"Error processing file {filePath}: {ex.Message}";
                await _onError(_config.Id, error);
                await PersistErrorAsync(error, filePath, ex);
                _logger.LogError(ex, "Error processing file {FilePath}", filePath);
            }
            finally
            {
                // Clean up old entries to prevent memory growth
                var cutoff = DateTime.UtcNow.AddMinutes(-5);
                var oldEntries = _processingFiles.Where(kvp => kvp.Value < cutoff).ToList();
                foreach (var entry in oldEntries)
                {
                    _processingFiles.TryRemove(entry.Key, out _);
                }
            }
        }

        private async Task<bool> IsDuplicateAsync(string fileHash)
        {
            try
            {
                var existing = await _repository.CountAsync(q => q.Hash == fileHash && 
                    (q.Status == FileStatus.Pending || q.Status == FileStatus.Processing || q.Status == FileStatus.Uploading));

                return existing > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for duplicate file hash: {Hash}", fileHash);
                return false; // Assume not a duplicate on error
            }
        }

        private async Task ProcessExistingFilesAsync(string folderPath)
        {
            try
            {
                var pattern = _config.FilePattern ?? "*.*";

                // Get files from the current directory and all subdirectories
                var files = Directory.GetFiles(folderPath, pattern, SearchOption.AllDirectories);

                _logger.LogInformation("Processing {Count} existing files in {Path} (including subdirectories)", files.Length, folderPath);

                foreach (var file in files)
                {
                    await ProcessFileAsync(file, "Existing");

                    // Small delay to prevent overwhelming the system
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                var error = $"Error processing existing files: {ex.Message}";
                await _onError(_config.Id, error);
                await PersistErrorAsync(error, folderPath, ex);
                _logger.LogError(ex, "Error processing existing files in {FolderPath}", folderPath);
            }
        }

        private async Task PersistErrorAsync(string message, string path, Exception? ex)
        {
            try
            {
                var entity = new WatcherError
                {
                    DataSourceId = _config.Id,
                    DataSourceName = _config.Name,
                    Path = path,
                    Message = message,
                    Exception = ex?.ToString(),
                    CreatedAt = DateTime.UtcNow
                };
                await _errorRepository.AddAsync(entity);
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx, "Failed to persist watcher error");
            }
        }

        private async Task WaitForFileToBeReady(string filePath, int maxWaitSeconds = 10)
        {
            var attempts = 0;
            var maxAttempts = maxWaitSeconds * 2; // Check every 500ms

            while (attempts < maxAttempts)
            {
                try
                {
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    return; // File is ready
                }
                catch (IOException)
                {
                    attempts++;
                    await Task.Delay(500);
                }
                catch (UnauthorizedAccessException)
                {
                    // File might still be being written
                    attempts++;
                    await Task.Delay(500);
                }
            }

            _logger.LogWarning("File may still be in use after waiting {Seconds}s: {FilePath}", maxWaitSeconds, filePath);
        }

        private async Task<bool> IsValidFileAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);

                if (!fileInfo.Exists || fileInfo.Length == 0)
                    return false;

                var maxSizeMB = await _configurationService.GetValueAsync<int?>(Constants.UploadMaxFileSizeMB) ?? 100;

                if (fileInfo.Length > maxSizeMB * 1024 * 1024)
                {
                    _logger.LogWarning("File too large ({SizeMB}MB > {MaxMB}MB): {FilePath}",
                        fileInfo.Length / 1024 / 1024, maxSizeMB, filePath);
                    return false;
                }

                // Validate based on file type
                var fileType = FileHelper.GetFileType(filePath);

                if (fileType == FileType.Json)
                {
                    return await FileHelper.IsValidJsonFileAsync(filePath);
                }
                else if (fileType == FileType.Image)
                {
                    return FileHelper.IsValidImageFile(filePath);
                }

                return true; // Other file types are accepted
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating file {FilePath}", filePath);
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch { }
            _watcher?.Dispose();
            _semaphore?.Dispose();
        }

        #endregion
    }
}
