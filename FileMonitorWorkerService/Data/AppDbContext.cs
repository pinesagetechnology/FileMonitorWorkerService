using FileMonitorWorkerService.Models;
using Microsoft.EntityFrameworkCore;

namespace FileMonitorWorkerService.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Configuration> Configurations { get; set; }

        public DbSet<FileDataSourceConfig> FileDataSourceConfigs { get; set; }

        public DbSet<UploadQueue> UploadQueues { get; set; }
        
        public DbSet<WatcherError> WatcherErrors { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UploadQueue>()
               .HasIndex(u => u.Status)
               .HasDatabaseName("IX_UploadQueue_Status");

            modelBuilder.Entity<UploadQueue>()
                .HasIndex(u => u.CreatedAt)
                .HasDatabaseName("IX_UploadQueue_CreatedAt");

            modelBuilder.Entity<UploadQueue>()
                .HasIndex(u => u.Hash)
                .HasDatabaseName("IX_UploadQueue_Hash");
        }
    }
}
