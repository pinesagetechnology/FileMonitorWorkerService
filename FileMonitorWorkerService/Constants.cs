namespace FileMonitorWorkerService
{
    public class Constants
    {
        public const string ProcessingIntervalSeconds = "App.ProcessingIntervalSeconds";
        public const string UploadMaxFileSizeMB = "Upload.MaxFileSizeMB";
        public const string UploadMaxConcurrentUploads = "Upload.MaxConcurrentUploads";
        public const string UploadMaxRetries = "Upload.MaxRetries";
        public const string UploadRetryDelaySeconds = "Upload.RetryDelaySeconds";
        public const string FileMonitorArchivePath = "Upload.ArchivePath"; 
        public const string UploadArchiveOnSuccess = "Upload.ArchiveOnSuccess";
        public const string UploadDeleteOnSuccess = "Upload.DeleteOnSuccess";
        public const string UploadNotifyOnCompletion = "Upload.NotifyOnCompletion";
        public const string UploadNotifyOnFailure = "Upload.NotifyOnFailure";
        public const string AzureStorageConnectionString = "Azure.StorageConnectionString";
        public const string AzureDefaultContainer = "Azure.DefaultContainer";

    }
}
