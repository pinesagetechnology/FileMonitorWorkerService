using System.ComponentModel.DataAnnotations;

namespace FileMonitorWorkerService.Models
{
    public class FileDataSourceConfig
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        public bool IsRefreshing { get; set; } = true;

        [StringLength(500)]
        public string? FolderPath { get; set; }

        [StringLength(500)]
        public string? ArchiveFolderPath { get; set; }

        [StringLength(100)]
        public string? FilePattern { get; set; } = "*.*";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastProcessedAt { get; set; }
    }

}
