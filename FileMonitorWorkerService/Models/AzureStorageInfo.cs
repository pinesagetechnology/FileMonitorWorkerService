namespace FileMonitorWorkerService.Models
{
    public class AzureStorageInfo
    {
        public bool IsConnected { get; set; }
        public string? AccountName { get; set; }
        public IEnumerable<string> Containers { get; set; } = Enumerable.Empty<string>();
        public string? ErrorMessage { get; set; }
    }
}
