using System.ComponentModel.DataAnnotations;

namespace FileMonitorWorkerService.Models
{
    public class FileMonitorServiceHeartBeat
    {
        [Key]
        public int Id { get; set; }

        public DateTime? LastRun { get; set; }
    }
}
