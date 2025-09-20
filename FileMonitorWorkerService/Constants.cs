namespace FileMonitorWorkerService
{
    public class Constants
    {
        public const string ProcessingIntervalSeconds = "App.ProcessingIntervalSeconds";
        public const string UploadMaxFileSizeMB = "Upload.MaxFileSizeMB";
        public const string UploadMaxConcurrentUploads = "Upload.MaxConcurrentUploads";
        public const string UploadMaxRetries = "Upload.MaxRetries";
        public const string UploadRetryDelaySeconds = "Upload.RetryDelaySeconds";
        public const string UploadMaxRetryDelayMinutes = "Upload.MaxRetryDelayMinutes";
        public const string UploadArchiveOnSuccess = "Upload.ArchiveOnSuccess";
        public const string UploadDeleteOnSuccess = "Upload.DeleteOnSuccess";
        public const string UploadNotifyOnCompletion = "Upload.NotifyOnCompletion";
        public const string UploadNotifyOnFailure = "Upload.NotifyOnFailure";

        public const string FileMonitorDefaultFilePattern = "FileMonitor.DefaultFilePattern";
        public const string FileMonitorFolderPath = "FileMonitor.FolderPath";
        public const string FileMonitorArchivePath = "FileMonitor.ArchivePath";

        public const string AzureStorageConnectionString = "Azure.StorageConnectionString";
        public const string AzureDefaultContainer = "Azure.DefaultContainer";

    }
}
