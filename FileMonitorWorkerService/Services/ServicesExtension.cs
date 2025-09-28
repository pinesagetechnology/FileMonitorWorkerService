namespace FileMonitorWorkerService.Services
{
    public static class ServiceLayerExtension
    {
        public static IServiceCollection RegisterServiceLayer(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IConfigurationService, ConfigurationService>();
            services.AddScoped<IDataSourceService, DataSourceService>();
            services.AddTransient<IFolderWatcherService, FolderWatcherService>();
            services.AddScoped<IAzureStorageService, AzureStorageService>();
            services.AddScoped<IUploadProcessService, UploadProcessService>();
            services.AddScoped<IHeartbeatService, HeartBeatService>();

            return services;
        }
    }
}
