using System.Threading;
using System.Threading.Tasks;

namespace WicsPlatform.Server.Services
{
    public interface IScheduleExecutionService
    {
        Task EnqueueAsync(ulong scheduleId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 진행 중인 방송을 강제로 종료합니다.
        /// </summary>
        Task FinalizeBroadcastAsync(ulong broadcastId, ulong channelId);
    }
}
