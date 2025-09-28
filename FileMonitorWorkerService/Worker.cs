using FileMonitorWorkerService.Models;
using FileMonitorWorkerService.Services;
using System.Collections.Concurrent;
using static System.Formats.Asn1.AsnWriter;

namespace FileMonitorWorkerService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, (IServiceScope Scope, IFolderWatcherService Watcher)> _activeWatchers = new();

        public Worker(ILogger<Worker> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker started at: {time}", DateTimeOffset.Now);

            int intervalSeconds = 5;
            using (var scope = _serviceProvider.CreateScope())
            {
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                intervalSeconds = await configService.GetValueAsync<int>(Constants.ProcessingIntervalSeconds);
            }

            _logger.LogInformation($"Fetch processing interval seconds: {intervalSeconds}");

            // Start a watcher per data source
            IEnumerable<FileDataSourceConfig> datasourceList;
            using (var scope = _serviceProvider.CreateScope())
            {
                var dataSourceService = scope.ServiceProvider.GetRequiredService<IDataSourceService>();
                datasourceList = await dataSourceService.GetAllDataSourcesAsync();
            }

            foreach (var datasource in datasourceList)
            {
                IServiceScope? scope = null;
                try
                {
                    _logger.LogInformation($"Starting to monitor folder: {datasource.FolderPath} for datasource: {datasource.Name}");
                    scope = _serviceProvider.CreateScope();
                    var watcher = scope.ServiceProvider.GetRequiredService<IFolderWatcherService>();

                    if (datasource.IsEnabled == false)
                    {
                        _logger.LogInformation($"Datasource {datasource.Name} is disabled. Skipping watcher start.");
                        scope.Dispose();
                        continue;
                    }

                    await watcher.StartAsync(datasource, async (id, error) =>
                    {
                        _logger.LogError("Watcher error for datasource {Id}: {Error}", id, error);
                        await Task.CompletedTask;
                    });

                    _activeWatchers.TryAdd(datasource.Name, (scope, watcher));
                    scope = null; // Ownership transferred to dictionary
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error starting watcher for datasource: {datasource.Name}");
                    scope?.Dispose();
                    continue;
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }

                await RefreshWatchersAsync();

                // Process upload queue each cycle with configured concurrency
                using (var scope = _serviceProvider.CreateScope())
                {
                    var uploader = scope.ServiceProvider.GetRequiredService<IUploadProcessService>();
                    var config = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                    var maxConcurrent = await config.GetValueAsync<int>(Constants.UploadMaxConcurrentUploads);
                    await uploader.ProcessPendingBatchAsync(maxConcurrent, stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
        }

        private async Task RefreshWatchersAsync()
        {
            IEnumerable<FileDataSourceConfig> datasourceList;

            using (var scope = _serviceProvider.CreateScope())
            {
                var dataSourceService = scope.ServiceProvider.GetRequiredService<IDataSourceService>();
                datasourceList = await dataSourceService.GetAllDataSourcesAsync();
            }

            foreach (var datasource in datasourceList)
            {
                var itemToRefresh = _activeWatchers.FirstOrDefault(x => x.Key == datasource.Name);

                if (!string.IsNullOrEmpty(itemToRefresh.Key))
                {
                    if (datasource.IsRefreshing || datasource.IsEnabled == false)
                    {
                        _logger.LogInformation($"Stopping existing watcher for datasource: {datasource.Name}");
                        try
                        {
                            await itemToRefresh.Value.Watcher.StopAsync();
                        }
                        finally
                        {
                            itemToRefresh.Value.Scope.Dispose();
                        }
                        _activeWatchers.TryRemove(itemToRefresh.Key, out var _);
                    }

                    _logger.LogInformation($"Refreshing folder watcher: {datasource.FolderPath} for datasource: {datasource.Name}");

                    if (datasource.IsRefreshing && datasource.IsEnabled)
                    {
                        IServiceScope? scope = null;
                        try
                        {
                            _logger.LogInformation($"Restarting to monitor folder: {datasource.FolderPath} for datasource: {datasource.Name}");
                            scope = _serviceProvider.CreateScope();
                            var watcher = scope.ServiceProvider.GetRequiredService<IFolderWatcherService>();

                            await watcher.StartAsync(datasource, async (id, error) =>
                            {
                                _logger.LogError("Watcher error for datasource {Id}: {Error}", id, error);
                                await Task.CompletedTask;
                            });

                            // Reset the refreshing flag for this specific data source
                            datasource.IsRefreshing = false;
                            using (var updateScope = _serviceProvider.CreateScope())
                            {
                                var dataSourceService = updateScope.ServiceProvider.GetRequiredService<IDataSourceService>();
                                await dataSourceService.UpdateDataSourcesIsrefreshingFlagAsync(datasource);
                            }

                            _activeWatchers.TryAdd(datasource.Name, (scope, watcher));
                            scope = null; // Ownership transferred to dictionary
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error starting watcher for datasource: {datasource.Name}");
                            scope?.Dispose();
                            continue;
                        }

                    }

                }
                else
                {
                    // Start new watcher for datasources that are enabled but not currently running
                    if (datasource.IsEnabled && !_activeWatchers.ContainsKey(datasource.Name))
                    {
                        IServiceScope? scope = null;
                        try
                        {
                            _logger.LogInformation($"Starting new watcher for datasource: {datasource.Name}");
                            scope = _serviceProvider.CreateScope();
                            var watcher = scope.ServiceProvider.GetRequiredService<IFolderWatcherService>();

                            await watcher.StartAsync(datasource, async (id, error) =>
                            {
                                _logger.LogError("Watcher error for datasource {Id}: {Error}", id, error);
                                await Task.CompletedTask;
                            });

                            _activeWatchers.TryAdd(datasource.Name, (scope, watcher));
                            scope = null; // Ownership transferred to dictionary
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error starting new watcher for datasource: {datasource.Name}");
                            scope?.Dispose();
                        }
                    }
                }

            }

        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Worker and active watchers...");
            foreach (var watcher in _activeWatchers)
            {
                try
                {
                    _logger.LogInformation($"Stopping watcher for datasource: {watcher.Key}");
                    await watcher.Value.Watcher.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping watcher");
                }
                finally
                {
                    watcher.Value.Scope.Dispose();
                }
            }
            _activeWatchers.Clear();
            await base.StopAsync(cancellationToken);
        }
    }
}
