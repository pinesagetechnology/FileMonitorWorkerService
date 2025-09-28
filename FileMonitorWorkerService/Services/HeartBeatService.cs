using FileMonitorWorkerService.Data.Repository;
using FileMonitorWorkerService.Models;

namespace FileMonitorWorkerService.Services
{
    public interface IHeartbeatService
    {
        Task Upsert();
    }

    public class HeartBeatService : IHeartbeatService
    {
        private readonly IRepository<FileMonitorServiceHeartBeat> _repository;
        public HeartBeatService(IRepository<FileMonitorServiceHeartBeat> repository)
        {
            _repository = repository;
        }

        public async Task Upsert()
        {
            var heartBeat = await _repository.GetByIdAsync(1);

            if (heartBeat == null)
            {
                heartBeat = new FileMonitorServiceHeartBeat
                {
                    Id = 1,
                    LastRun = DateTime.UtcNow
                };
                await _repository.AddAsync(heartBeat);
            }
            else
            {
                heartBeat.LastRun = DateTime.UtcNow;
                await _repository.UpdateAsync(heartBeat);
            }
        }
    }
}
