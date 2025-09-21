using FileMonitorWorkerService.Data.Repository;
using FileMonitorWorkerService.Models;
using FileMonitorWorkerService.Utility;

namespace FileMonitorWorkerService.Services
{
    public interface IUploadProcessService
    {
        Task<int> ProcessPendingBatchAsync(int maxItems = 10, CancellationToken cancellationToken = default);
    }

    public class UploadProcessService : IUploadProcessService
    {
        private readonly ILogger<UploadProcessService> _logger;
        private readonly IRepository<UploadQueue> _uploadQueueRepository;
        private readonly IConfigurationService _configurationService;
        private readonly IAzureStorageService _azureStorageService;

        public UploadProcessService(ILogger<UploadProcessService> logger,
            IRepository<UploadQueue> uploadQueueRepository,
            IConfigurationService configurationService,
            IAzureStorageService azureStorageService)
        {
            _logger = logger;
            _uploadQueueRepository = uploadQueueRepository;
            _configurationService = configurationService;
            _azureStorageService = azureStorageService;
        }

        public async Task<int> ProcessPendingBatchAsync(int maxItems = 10, CancellationToken cancellationToken = default)
        {
            var pending = (await _uploadQueueRepository.FindAsync(q => q.Status == FileStatus.Pending))
                .OrderBy(q => q.CreatedAt)
                .Take(maxItems)
                .ToList();

            var tasks = pending.Select(item => TryUploadOneAsync(item, cancellationToken));
            var results = await Task.WhenAll(tasks);
            return results.Count(r => r);
        }

        private async Task<bool> TryUploadOneAsync(UploadQueue item, CancellationToken cancellationToken)
        {
            try
            {
                if (await _azureStorageService.IsConnectedAsync() == false)
                {
                    _logger.LogWarning("Azure Storage service is not connected. Skipping upload for file: {File}", item.FilePath);
                    return false;
                }

                // Mark as Uploading
                item.Status = FileStatus.Uploading;
                item.LastAttemptAt = DateTime.UtcNow;
                item.AttemptCount += 1;
                await _uploadQueueRepository.UpdateAsync(item);

                // Load settings
                var storageConn = await _configurationService.GetValueAsync(Constants.AzureStorageConnectionString);
                var container = await _configurationService.GetValueAsync(Constants.AzureDefaultContainer) ?? "gateway-data";

                if (string.IsNullOrWhiteSpace(storageConn))
                {
                    _logger.LogError("Azure StorageConnectionString is not configured. Skipping file: {File}", item.FilePath);
                    return await MarkAsFailedAsync(item, "Missing Azure Storage connection string");
                }

                var blobName = item.AzureBlobName ?? FileHelper.GetSafeFileName(item.FileName);

                var progress = new Progress<AzureUploadProgress>(p =>
                {
                    _logger.LogDebug("Uploading {File}: {Uploaded}/{Total} bytes - {Status}", item.FileName, p.BytesUploaded, p.TotalBytes, p.StatusMessage);
                });

                var uploadResult = await _azureStorageService.UploadFileAsync(item.FilePath, container, blobName, progress);
                var uploaded = uploadResult.IsSuccess;
                if (!uploaded)
                {
                    return await MarkAsFailedAsync(item, uploadResult.ErrorMessage ?? "Upload failed");
                }

                // On success
                item.Status = FileStatus.Completed;
                item.CompletedAt = DateTime.UtcNow;
                item.UploadDurationMs = (long?)((item.CompletedAt - (item.LastAttemptAt ?? item.CreatedAt)).Value.TotalMilliseconds);
                await _uploadQueueRepository.UpdateAsync(item);

                // Archive/Delete as per config
                var archiveOnSuccess = await _configurationService.GetValueAsync<bool?>(Constants.UploadArchiveOnSuccess) ?? true;
                var deleteOnSuccess = await _configurationService.GetValueAsync<bool?>(Constants.UploadDeleteOnSuccess) ?? false;

                if (deleteOnSuccess && File.Exists(item.FilePath))
                {
                    File.Delete(item.FilePath);
                }
                else if (archiveOnSuccess)
                {
                    var archiveDir = await _configurationService.GetValueAsync(Constants.FileMonitorArchivePath) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(archiveDir) && File.Exists(item.FilePath))
                    {
                        await FileHelper.MoveFileToArchiveAsync(item.FilePath, archiveDir);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file {File}", item.FilePath);
                return await MarkAsFailedAsync(item, ex.Message);
            }
        }

        private async Task<bool> MarkAsFailedAsync(UploadQueue item, string error)
        {
            var maxRetries = await _configurationService.GetValueAsync<int?>(Constants.UploadMaxRetries) ?? 3;
            var retryDelaySeconds = await _configurationService.GetValueAsync<int?>(Constants.UploadRetryDelaySeconds) ?? 30;

            item.ErrorMessage = error;
            if (item.AttemptCount >= maxRetries)
            {
                item.Status = FileStatus.Failed;
            }
            else
            {
                item.Status = FileStatus.Pending;
                // backoff not persisted; worker loop interval will pick it up later
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
            }
            await _uploadQueueRepository.UpdateAsync(item);
            return false;
        }

        
    }
}
