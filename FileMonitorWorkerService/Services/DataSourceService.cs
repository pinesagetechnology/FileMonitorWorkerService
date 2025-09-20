using FileMonitorWorkerService.Data.Repository;
using FileMonitorWorkerService.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMonitorWorkerService.Services
{
    public interface IDataSourceService
    {
        Task<IEnumerable<FileDataSourceConfig>> GetAllDataSourcesAsync();
        Task<FileDataSourceConfig> GetDataSourcesByNameAsync(string name);
        Task UpdateDataSourcesIsrefreshingFlagAsync(FileDataSourceConfig fileDataSourceConfig);
    }

    public class DataSourceService : IDataSourceService
    {
        private readonly IRepository<FileDataSourceConfig> _repository;
        private readonly ILogger<DataSourceService> _logger;

        public DataSourceService(IRepository<FileDataSourceConfig> repository, ILogger<DataSourceService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<IEnumerable<FileDataSourceConfig>> GetAllDataSourcesAsync()
        {
            try
            {
                return await _repository.GetAllAsync();
            }
            catch(Exception ex)
            {
                _logger.LogError($"Error occurred while fetching data sources {ex.Message}");

                return Enumerable.Empty<FileDataSourceConfig>();
            }
        }

        public async Task<FileDataSourceConfig> GetDataSourcesByNameAsync(string name)
        {
            try
            {
                _logger.LogInformation("Fetching all data sources");

                var datasourceConfig = await _repository.FindAsync(x => x.Name == name);

                return datasourceConfig.FirstOrDefault() ?? new FileDataSourceConfig();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred while fetching data sources {ex.Message}");

                return new FileDataSourceConfig();
            }
        }

        public async Task UpdateDataSourcesIsrefreshingFlagAsync(FileDataSourceConfig fileDataSourceConfig)
        {
            _logger.LogInformation("Fetching all data sources");

            await _repository.UpdateAsync(fileDataSourceConfig);

            _logger.LogInformation($"Updated data source {fileDataSourceConfig.Name} with isRefreshing flag set to {fileDataSourceConfig.IsRefreshing}");
        }
    }
}
