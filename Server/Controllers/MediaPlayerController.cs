using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Shared;

namespace WicsPlatform.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MediaPlayerController : Controller
    {
        private readonly IMediaBroadcastService _mediaBroadcastService;
        private readonly ILogger<MediaPlayerController> _logger;
        private readonly wicsContext context;

        public MediaPlayerController(
            IMediaBroadcastService mediaBroadcastService,
            ILogger<MediaPlayerController> logger,
            WicsPlatform.Server.Data.wicsContext context)
        {
            _mediaBroadcastService = mediaBroadcastService;
            _logger = logger;
            this.context = context;
        }

        /// <summary>
        /// 미디어 재생 시작
        /// </summary>
        /// <param name="request">재생 요청 정보</param>
        /// <returns>재생 시작 결과</returns>
        [HttpPost("play")]
        public async Task<IActionResult> Play([FromBody] MediaPlayRequest request)
        {
            try
            {
                // 요청 유효성 검사
                if (string.IsNullOrWhiteSpace(request.BroadcastId))
                {
                    return BadRequest(new MediaPlayResponse
                    {
                        Success = false,
                        Message = "BroadcastId is required"
                    });
                }

                _logger.LogInformation(
                    $"Media play request - BroadcastId: {request.BroadcastId}, " +
                    $"MediaIds: [{string.Join(", ", request.MediaIds ?? new List<ulong>())}]"
                );

                // DB에서 실제 미디어 정보 조회
                var mediaInfoList = new List<Middleware.WebSocketMiddleware.MediaInfo>();

                if (request.MediaIds != null && request.MediaIds.Any())
                {
                    var mediaItems = await context.Media
                        .Where(m => request.MediaIds.Contains(m.Id) && (m.DeleteYn != "Y" || m.DeleteYn == null))
                        .Select(m => new Middleware.WebSocketMiddleware.MediaInfo
                        {
                            Id = m.Id,
                            FileName = m.FileName,
                            FullPath = m.FullPath
                        })
                        .ToListAsync();

                    mediaInfoList.AddRange(mediaItems);

                    _logger.LogInformation($"Found {mediaInfoList.Count} media files from DB");
                }

                // JsonElement 형태로 변환하여 MediaBroadcastService에 전달
                var jsonData = JsonSerializer.SerializeToElement(new
                {
                    broadcastId = request.BroadcastId,
                    mediaIds = request.MediaIds ?? new List<ulong>()
                });

                // 실제 미디어 정보를 전달
                var result = await _mediaBroadcastService.HandlePlayRequestAsync(
                    request.BroadcastId,
                    jsonData,
                    mediaInfoList, // 실제 미디어 정보 전달
                    new List<Services.SpeakerInfo>(), // 스피커 정보는 필요시 서비스 내부에서 처리
                    0 // ChannelId는 서비스 내부에서 필요시 조회
                );

                // 성공 응답
                if (result.Success)
                {
                    return Ok(new MediaPlayResponse
                    {
                        Success = true,
                        SessionId = result.SessionId,
                        Message = result.Message,
                        MediaFiles = result.MediaFiles?.Select(f => new MediaFileInfo
                        {
                            Id = f.Id,
                            FileName = f.FileName,
                            Status = f.Status
                        }).ToList()
                    });
                }

                // 실패 응답
                return BadRequest(new MediaPlayResponse
                {
                    Success = false,
                    Message = result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting media playback");
                return StatusCode(500, new MediaPlayResponse
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 미디어 재생 중지
        /// </summary>
        /// <param name="request">중지 요청 정보</param>
        /// <returns>중지 결과</returns>
        [HttpPost("stop")]
        public async Task<IActionResult> Stop([FromBody] MediaStopRequest request)
        {
            try
            {
                // 요청 유효성 검사
                if (string.IsNullOrWhiteSpace(request.BroadcastId))
                {
                    return BadRequest(new MediaStopResponse
                    {
                        Success = false,
                        Message = "BroadcastId is required"
                    });
                }

                _logger.LogInformation($"Media stop request - BroadcastId: {request.BroadcastId}");

                // 미디어 재생 중지
                var success = await _mediaBroadcastService.StopMediaByBroadcastIdAsync(request.BroadcastId);

                if (success)
                {
                    return Ok(new MediaStopResponse
                    {
                        Success = true,
                        Message = "Media playback stopped successfully"
                    });
                }

                return BadRequest(new MediaStopResponse
                {
                    Success = false,
                    Message = "Failed to stop media playback or no active playback found"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping media playback");
                return StatusCode(500, new MediaStopResponse
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 현재 재생 상태 조회 (선택적 기능)
        /// </summary>
        /// <param name="broadcastId">방송 ID</param>
        /// <returns>재생 상태 정보</returns>
        [HttpGet("status/{broadcastId}")]
        public async Task<IActionResult> GetStatus(string broadcastId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(broadcastId))
                {
                    return BadRequest(new { success = false, message = "BroadcastId is required" });
                }

                var status = await _mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);

                return Ok(new
                {
                    success = true,
                    broadcastId = broadcastId,
                    isPlaying = status.IsPlaying,
                    currentTrackIndex = status.CurrentTrackIndex,
                    currentPosition = status.CurrentPosition.ToString(@"mm\:ss"),
                    totalDuration = status.TotalDuration.ToString(@"mm\:ss")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting media status");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}