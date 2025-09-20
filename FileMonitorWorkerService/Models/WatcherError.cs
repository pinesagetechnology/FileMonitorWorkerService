using System.ComponentModel.DataAnnotations;

namespace FileMonitorWorkerService.Models
{
    public class WatcherError
    {
        [Key]
        public int Id { get; set; }

        public int DataSourceId { get; set; }

        [StringLength(200)]
        public string? DataSourceName { get; set; }

        [StringLength(500)]
        public string? Path { get; set; }

        [StringLength(1000)]
        public string Message { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Exception { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

