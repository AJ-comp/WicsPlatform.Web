using System.Threading;
using System.Threading.Tasks;

namespace WicsPlatform.Server.Services
{
    public interface IScheduleExecutionService
    {
        Task EnqueueAsync(ulong scheduleId, CancellationToken cancellationToken = default);
    }
}
