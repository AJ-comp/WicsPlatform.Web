using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Models.wics;
using WicsPlatform.Shared;

namespace WicsPlatform.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VolumeController : Controller
    {
        private readonly IAudioMixingService _audioMixingService;
        private readonly ILogger<VolumeController> _logger;
        private readonly wicsContext _context;

        public VolumeController(
            IAudioMixingService audioMixingService,
            ILogger<VolumeController> logger,
            wicsContext context)  // DB Context 추가
        {
            _audioMixingService = audioMixingService;
            _logger = logger;
            _context = context;
        }

        // POST: api/volume/set
        [HttpPost("set")]
        public async Task<IActionResult> SetVolume([FromBody] VolumeRequest request)
        {
            try
            {
                // 1. DB에 볼륨 정보 저장 (방송 여부와 관계없이)
                await SaveVolumeToDatabase(request);

                // 2. 방송 중이면 실시간 볼륨 조절
                // BroadcastId가 없거나 세션이 없으면 SetVolume 내부에서 무시됨
                if (!string.IsNullOrEmpty(request.BroadcastId))
                {
                    await _audioMixingService.SetVolume(
                        request.BroadcastId,
                        request.Source,
                        request.Volume
                    );

                    _logger.LogInformation(
                        $"Volume changed in real-time - Broadcast: {request.BroadcastId}, " +
                        $"Source: {request.Source}, Volume: {request.Volume:P0}"
                    );
                }
                else
                {
                    _logger.LogInformation(
                        $"Volume saved to DB only (no active broadcast) - " +
                        $"Channel: {request.ChannelId}, Source: {request.Source}, Volume: {request.Volume:P0}"
                    );
                }

                return Ok(new
                {
                    success = true,
                    message = request.BroadcastId != null
                        ? "Volume updated successfully"
                        : "Volume settings saved",
                    broadcastId = request.BroadcastId,
                    channelId = request.ChannelId,
                    source = request.Source.ToString(),
                    volume = request.Volume,
                    savedToDb = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting volume");
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        private async Task SaveVolumeToDatabase(VolumeRequest request)
        {
            // Channel 테이블 업데이트
            var channel = await _context.Channels
                .FirstOrDefaultAsync(c => c.Id == request.ChannelId);

            if (channel == null)
            {
                _logger.LogWarning($"Channel not found: {request.ChannelId}");
                return;
            }

            // Source에 따라 다른 컬럼 업데이트
            switch (request.Source)
            {
                case AudioSource.Microphone:
                    channel.MicVolume = request.Volume;
                    break;

                case AudioSource.Media:
                    channel.MediaVolume = request.Volume;
                    break;

                case AudioSource.TTS:
                    channel.TtsVolume = request.Volume;
                    break;

                case AudioSource.Master:
                    channel.Volume = request.Volume;
                    break;
            }

            channel.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogDebug(
                $"Volume saved to DB - Channel: {request.ChannelId}, " +
                $"Source: {request.Source}, Volume: {request.Volume:F2}"
            );
        }

        // GET: api/volume/get/{channelId}
        [HttpGet("get/{channelId}")]
        public async Task<IActionResult> GetVolumeSettings(ulong channelId)
        {
            var channel = await _context.Channels
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == channelId);

            if (channel == null)
            {
                return NotFound(new { success = false, message = "Channel not found" });
            }

            return Ok(new
            {
                success = true,
                channelId = channelId,
                volumes = new
                {
                    microphone = channel.MicVolume,
                    media = channel.MediaVolume,
                    tts = channel.TtsVolume,
                    master = channel.Volume
                }
            });
        }
    }
}