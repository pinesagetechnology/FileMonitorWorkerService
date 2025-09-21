using FileMonitorWorkerService.Models;
using FileMonitorWorkerService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace FileMonitorWorkerService.Data
{
    public class DatabaseInitializer
    {
        public static async Task InitializeAsync(AppDbContext context, ILogger logger)
        {
            logger.LogInformation("=== Starting Database Initialization ===");

            try
            {
                logger.LogInformation("Testing database connection...");
                var canConnect = await context.Database.CanConnectAsync();
                if (!canConnect)
                {
                    logger.LogWarning("Cannot connect to database, attempting to create...");
                }
                else
                {
                    logger.LogInformation("Database connection test successful");
                }

                logger.LogInformation("Ensuring database exists and is up to date...");
                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                if (pendingMigrations.Any())
                {
                    logger.LogInformation("Found {Count} pending migrations: {Migrations}",
                        pendingMigrations.Count(), string.Join(", ", pendingMigrations));
                }
                else
                {
                    logger.LogInformation("No pending migrations found");
                }

                await context.Database.EnsureCreatedAsync();
                logger.LogInformation("Database schema ensured successfully");

                var tableNames = await GetTableNamesAsync(context);
                logger.LogInformation("Database contains {Count} tables: {Tables}",
                    tableNames.Count, string.Join(", ", tableNames));

                logger.LogInformation("Checking for existing data source configurations...");
                var existingConfigs = await context.FileDataSourceConfigs.CountAsync();
                logger.LogInformation("Found {Count} existing data source configurations", existingConfigs);

                if (!await context.FileDataSourceConfigs.AnyAsync())
                {
                    logger.LogInformation("No data source configurations found, seeding defaults...");
                    await SeedDataSourcesIfEmptyAsync(context, logger);
                }
                else
                {
                    logger.LogInformation("Data source configurations already exist, skipping seeding");
                }

                logger.LogInformation("Seeding essential configuration values...");
                await SeedEssentialConfigurationsAsync(context, logger);

                await LogDatabaseStatisticsAsync(context, logger);

                logger.LogInformation("=== Database Initialization Complete ===");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while initializing the database");
                throw;
            }
        }

        private static async Task<List<string>> GetTableNamesAsync(AppDbContext context)
        {
            try
            {
                var tableNames = new List<string>();
                using var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";

                using var result = await command.ExecuteReaderAsync();
                while (await result.ReadAsync())
                {
                    tableNames.Add(result.GetString(0));
                }

                return tableNames;
            }
            catch
            {
                return new List<string>();
            }
        }

        private static async Task SeedDataSourcesIfEmptyAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                var hasAny = await context.FileDataSourceConfigs.AnyAsync();

                if (hasAny)
                {
                    logger.LogInformation("Data source configs already exist. Skipping seeding.");
                    return;
                }

                logger.LogInformation("Seeding default data source configurations...");

                var defaultSources = new[]
                {
                    new FileDataSourceConfig
                    {
                        Name = "FolderMonitor1",
                        IsEnabled = false,
                        IsRefreshing = false,
                        FolderPath = "",
                        FilePattern = "*.*",
                        CreatedAt = DateTime.UtcNow
                    }
                };

                await context.FileDataSourceConfigs.AddRangeAsync(defaultSources);
                await context.SaveChangesAsync();
                logger.LogInformation("Successfully seeded {Count} default data source configurations", defaultSources.Length);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error seeding data source configurations");
            }
        }

        private static async Task SeedEssentialConfigurationsAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                // Ensure a minimal set of configuration keys exist if missing
                var defaults = new List<Configuration>
                {
                    new Configuration { Key = Constants.ProcessingIntervalSeconds, Value = "10", Category = "App", Description = "Default processing interval (seconds)" },
                    new Configuration { Key = Constants.UploadMaxFileSizeMB, Value = "100", Category = "Upload", Description = "Max upload file size (MB)" },
                    new Configuration { Key = Constants.UploadMaxConcurrentUploads, Value = "3", Category = "Upload", Description = "Max concurrent uploads" },
                    new Configuration { Key = Constants.UploadMaxRetries, Value = "5", Category = "Upload", Description = "Max upload retries" },
                    new Configuration { Key = Constants.UploadRetryDelaySeconds, Value = "30", Category = "Upload", Description = "Initial upload retry delay (seconds)" },
                    new Configuration { Key = Constants.UploadArchiveOnSuccess, Value = "true", Category = "Upload", Description = "Archive file on successful upload" },
                    new Configuration { Key = Constants.UploadDeleteOnSuccess, Value = "false", Category = "Upload", Description = "Delete file on successful upload" },
                    new Configuration { Key = Constants.UploadNotifyOnCompletion, Value = "false", Category = "Upload", Description = "Notify on successful upload" },
                    new Configuration { Key = Constants.UploadNotifyOnFailure, Value = "true", Category = "Upload", Description = "Notify on upload failure" },
                    new Configuration { Key = Constants.FileMonitorDefaultFilePattern, Value = "*.*", Category = "FileMonitor", Description = "Default file pattern to monitor" },
                    new Configuration { Key = Constants.AzureStorageConnectionString, Value = "", Category = "Azure", Description = "Azure Storage connection string" },
                    new Configuration { Key = Constants.AzureDefaultContainer, Value = "uploads", Category = "Azure", Description = "Default Azure Storage container name" }
                };

                foreach (var item in defaults)
                {
                    var exists = await context.Configurations.AnyAsync(c => c.Key == item.Key);
                    if (!exists)
                    {
                        await context.Configurations.AddAsync(item);
                    }
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error seeding essential configuration values");
            }
        }

        private static async Task LogDatabaseStatisticsAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                var configCount = await context.Configurations.CountAsync();
                var sourceCount = await context.FileDataSourceConfigs.CountAsync();
                var queueCount = await context.UploadQueues.CountAsync();

                logger.LogInformation("DB Stats -> Configurations: {Configs}, DataSources: {Sources}, UploadQueue: {Queue}",
                    configCount, sourceCount, queueCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error logging database statistics");
            }
        }

    }
}
