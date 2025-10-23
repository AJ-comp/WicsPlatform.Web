using System.Threading;
using System.Threading.Tasks;

namespace WicsPlatform.Server.Services
{
    public interface IScheduleExecutionService
    {
        Task EnqueueAsync(ulong scheduleId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 진행 중인 방송을 강제로 종료합니다.
        /// 채널 ID만으로 현재 진행 중인 방송을 조회하여 종료합니다.
        /// </summary>
        Task FinalizeBroadcastAsync(ulong channelId);
    }
}
