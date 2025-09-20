using FileMonitorWorkerService.Models;
using FileMonitorWorkerService.Services;
using Microsoft.EntityFrameworkCore;

namespace FileMonitorWorkerService.Data
{
    public class DatabaseInitializer
    {
        private readonly ILogger<IServiceProvider> _logger;
        private readonly IConfigurationService _configService;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _dbContext;
        public DatabaseInitializer(ILogger<IServiceProvider> logger,
            IConfigurationService configService,
            IConfiguration configuration,
            AppDbContext dbContext) 
        {
            _configService = configService;
            _configuration = configuration;
            _logger = logger;
            _dbContext = dbContext;
        }

        public async Task SeedConfigurationFromAppSettingsAsync()
        {
            _logger.LogInformation("=== Starting Configuration Seeding ===");

            var configDefaults = _configuration.GetSection("ConfigurationDefaults");
            if (!configDefaults.Exists())
            {
                _logger.LogInformation("ConfigurationDefaults section not found in appsettings.json");
                return;
            }

            _logger.LogInformation("Found ConfigurationDefaults section with {CategoryCount} categories",
                configDefaults.GetChildren().Count());

            var seededCount = 0;
            var skippedCount = 0;
            var errorCount = 0;

            foreach (var category in configDefaults.GetChildren())
            {
                _logger.LogInformation("Processing configuration category: {Category}", category.Key);
                var categorySettings = category.GetChildren().ToList();
                _logger.LogInformation("Category {Category} contains {SettingCount} settings",
                    category.Key, categorySettings.Count);

                foreach (var setting in categorySettings)
                {
                    var key = $"{category.Key}.{setting.Key}";
                    var value = setting.Value ?? "";

                    try
                    {
                        var exists = await _configService.KeyExistsAsync(key);
                        if (!exists)
                        {
                            await _configService.SetValueAsync(
                                key,
                                value,
                                $"Default value from appsettings.json for {key}",
                                category.Key);

                            seededCount++;
                            _logger.LogDebug("Seeded configuration: {Key} = {Value}", key, value);
                        }
                        else
                        {
                            skippedCount++;
                            _logger.LogDebug("Configuration key {Key} already exists, skipping", key);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logger.LogError(ex, "Failed to seed configuration key: {Key}", key);
                    }
                }
            }

            _logger.LogInformation("=== Configuration Seeding Complete ===");
            _logger.LogInformation("Seeded: {SeededCount}, Skipped: {SkippedCount}, Errors: {ErrorCount}",
                seededCount, skippedCount, errorCount);
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

        public async Task SeedDataSourcesIfEmptyAsync(bool force = false)
        {
            try
            {
                var hasAny = await _dbContext.FileDataSourceConfigs.AnyAsync();
                if (hasAny && !force)
                {
                    _logger.LogInformation("Data source configs already exist. Skipping seeding.");
                    return;
                }

                _logger.LogInformation("Seeding default data source configurations...");

                var defaultSources = new[]
                {
                    new FileDataSourceConfig
                    {
                        Name = "FolderMonitor1",
                        IsEnabled = false,
                        IsRefreshing = false,
                        FolderPath = "",
                        ArchiveFolderPath = "",
                        FilePattern = "*.*",
                        CreatedAt = DateTime.UtcNow
                    }
                };

                if (force)
                {
                    _dbContext.FileDataSourceConfigs.RemoveRange(_dbContext.FileDataSourceConfigs);
                }

                await _dbContext.FileDataSourceConfigs.AddRangeAsync(defaultSources);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Successfully seeded {Count} default data source configurations", defaultSources.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding data source configurations");
            }
        }

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
                    await SeedDataSourceConfigsAsync(context, logger);
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

        private static async Task SeedEssentialConfigurationsAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                // Ensure a minimal set of configuration keys exist if missing
                var defaults = new List<Configuration>
                {
                    new Configuration { Key = Constants.ProcessingIntervalSeconds, Value = "10", Category = "App", Description = "Default processing interval (seconds)" },
                    new Configuration { Key = Constants.UploadMaxFileSizeMB, Value = "100", Category = "Upload", Description = "Max upload file size (MB)" }
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

        private static async Task SeedDataSourceConfigsAsync(AppDbContext context, ILogger logger)
        {
            try
            {
                var defaults = new[]
                {
                    new FileDataSourceConfig
                    {
                        Name = "Local Folder Monitor",
                        IsEnabled = false,
                        FolderPath = "",
                        ArchiveFolderPath = "",
                        FilePattern = "*.*",
                        CreatedAt = DateTime.UtcNow
                    }
                };

                await context.FileDataSourceConfigs.AddRangeAsync(defaults);
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded {Count} data source configurations", defaults.Length);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error seeding data source configurations");
            }
        }
    }
}
