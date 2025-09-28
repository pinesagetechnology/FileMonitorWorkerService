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

                // Ensure all required tables exist
                await EnsureAllTablesExistAsync(context, logger);

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
                    new Configuration { Key = Constants.FileMonitorArchivePath, Value = "./", Category = "Upload", Description = "Path to archive uploaded files" },
                    new Configuration { Key = Constants.UploadDeleteOnSuccess, Value = "false", Category = "Upload", Description = "Delete file on successful upload" },
                    new Configuration { Key = Constants.UploadNotifyOnCompletion, Value = "false", Category = "Upload", Description = "Notify on successful upload" },
                    new Configuration { Key = Constants.UploadNotifyOnFailure, Value = "true", Category = "Upload", Description = "Notify on upload failure" },
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

        /// <summary>
        /// Ensures all required database tables exist. Creates any missing tables.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="logger">Logger instance</param>
        /// <returns>Task representing the async operation</returns>
        public static async Task EnsureAllTablesExistAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                logger.LogInformation("Checking for required database tables...");

                var requiredTables = new[]
                {
                    "Configurations",
                    "FileDataSourceConfigs", 
                    "UploadQueues",
                    "WatcherErrors",
                    "FileMonitorServiceHeartBeats"
                };

                var existingTables = await GetTableNamesAsync(context);
                var missingTables = requiredTables.Where(table => 
                    !existingTables.Any(existing => 
                        string.Equals(existing, table, StringComparison.OrdinalIgnoreCase))).ToList();

                if (missingTables.Any())
                {
                    logger.LogWarning("Missing tables detected: {MissingTables}", string.Join(", ", missingTables));
                    logger.LogInformation("Creating missing tables using SQL...");
                    
                    using var connection = context.Database.GetDbConnection();
                    await connection.OpenAsync();
                    
                    foreach (var tableName in missingTables)
                    {
                        try
                        {
                            var createTableSql = GetCreateTableSql(tableName);
                            logger.LogInformation("Creating table: {TableName}", tableName);
                            
                            using var command = connection.CreateCommand();
                            command.CommandText = createTableSql;
                            await command.ExecuteNonQueryAsync();
                            
                            logger.LogInformation("Successfully created table: {TableName}", tableName);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to create table: {TableName}", tableName);
                            throw;
                        }
                    }
                    
                    // Verify tables were created
                    var updatedTables = await GetTableNamesAsync(context);
                    var stillMissing = requiredTables.Where(table => 
                        !updatedTables.Any(existing => 
                            string.Equals(existing, table, StringComparison.OrdinalIgnoreCase))).ToList();

                    if (stillMissing.Any())
                    {
                        logger.LogError("Failed to create tables: {StillMissing}", string.Join(", ", stillMissing));
                        throw new InvalidOperationException($"Could not create required tables: {string.Join(", ", stillMissing)}");
                    }
                    
                    logger.LogInformation("Successfully created all missing tables");
                }
                else
                {
                    logger.LogInformation("All required tables already exist");
                }

                // Log final table status
                var finalTables = await GetTableNamesAsync(context);
                logger.LogInformation("Database now contains {Count} tables: {Tables}", 
                    finalTables.Count, string.Join(", ", finalTables));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error ensuring all tables exist");
                throw;
            }
        }

        /// <summary>
        /// Gets the SQL CREATE TABLE statement for the specified table name
        /// </summary>
        /// <param name="tableName">Name of the table</param>
        /// <returns>SQL CREATE TABLE statement</returns>
        private static string GetCreateTableSql(string tableName)
        {
            return tableName switch
            {
                "Configurations" => @"
                    CREATE TABLE IF NOT EXISTS Configurations (
                        Key TEXT PRIMARY KEY,
                        Value TEXT NOT NULL,
                        Description TEXT,
                        UpdatedAt TEXT NOT NULL,
                        Category TEXT,
                        IsEncrypted INTEGER NOT NULL DEFAULT 0
                    )",
                "FileDataSourceConfigs" => @"
                    CREATE TABLE IF NOT EXISTS FileDataSourceConfigs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        IsEnabled INTEGER NOT NULL DEFAULT 1,
                        IsRefreshing INTEGER NOT NULL DEFAULT 1,
                        FolderPath TEXT,
                        FilePattern TEXT,
                        CreatedAt TEXT NOT NULL,
                        LastProcessedAt TEXT
                    )",
                "UploadQueues" => @"
                    CREATE TABLE IF NOT EXISTS UploadQueues (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FilePath TEXT NOT NULL,
                        FileName TEXT NOT NULL,
                        FileType INTEGER NOT NULL,
                        Status INTEGER NOT NULL DEFAULT 0,
                        FileSizeBytes INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        LastAttemptAt TEXT,
                        AttemptCount INTEGER NOT NULL DEFAULT 0,
                        MaxRetries INTEGER NOT NULL DEFAULT 5,
                        ErrorMessage TEXT,
                        AzureBlobUrl TEXT,
                        AzureContainer TEXT,
                        AzureBlobName TEXT,
                        CompletedAt TEXT,
                        UploadDurationMs INTEGER,
                        Hash TEXT
                    );
                    CREATE INDEX IF NOT EXISTS IX_UploadQueue_Status ON UploadQueues(Status);
                    CREATE INDEX IF NOT EXISTS IX_UploadQueue_CreatedAt ON UploadQueues(CreatedAt);
                    CREATE INDEX IF NOT EXISTS IX_UploadQueue_Hash ON UploadQueues(Hash);",
                "WatcherErrors" => @"
                    CREATE TABLE IF NOT EXISTS WatcherErrors (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DataSourceId INTEGER NOT NULL,
                        DataSourceName TEXT,
                        Path TEXT,
                        Message TEXT NOT NULL,
                        Exception TEXT,
                        CreatedAt TEXT NOT NULL
                    )",
                "FileMonitorServiceHeartBeats" => @"
                    CREATE TABLE IF NOT EXISTS FileMonitorServiceHeartBeats (
                        Id INTEGER PRIMARY KEY,
                        LastRun TEXT
                    )",
                _ => throw new ArgumentException($"Unknown table name: {tableName}")
            };
        }

        private static async Task LogDatabaseStatisticsAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                var configCount = await context.Configurations.CountAsync();
                var sourceCount = await context.FileDataSourceConfigs.CountAsync();
                var queueCount = await context.UploadQueues.CountAsync();
                var errorCount = await context.WatcherErrors.CountAsync();
                var heartbeatCount = await context.FileMonitorServiceHeartBeats.CountAsync();

                logger.LogInformation("DB Stats -> Configurations: {Configs}, DataSources: {Sources}, UploadQueue: {Queue}, WatcherErrors: {Errors}, HeartBeats: {HeartBeats}",
                    configCount, sourceCount, queueCount, errorCount, heartbeatCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error logging database statistics");
            }
        }

    }
}
