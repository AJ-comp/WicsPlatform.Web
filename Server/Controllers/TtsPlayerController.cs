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
    public class TtsPlayerController : Controller
    {
        private readonly ITtsBroadcastService _ttsBroadcastService;
        private readonly ILogger<TtsPlayerController> _logger;
        private readonly wicsContext context;

        public TtsPlayerController(
            ITtsBroadcastService ttsBroadcastService,
            ILogger<TtsPlayerController> logger,
            WicsPlatform.Server.Data.wicsContext context)
        {
            _ttsBroadcastService = ttsBroadcastService;
            _logger = logger;
            this.context = context;
        }

        /// <summary>
        /// TTS 재생 시작
        /// </summary>
        /// <param name="request">재생 요청 정보</param>
        /// <returns>재생 시작 결과</returns>
        [HttpPost("play")]
        public async Task<IActionResult> Play([FromBody] TtsPlayRequest request)
        {
            try
            {
                // 요청 유효성 검사
                if (request.BroadcastId == 0)
                {
                    return BadRequest(new TtsPlayResponse
                    {
                        Success = false,
                        Message = "BroadcastId is required"
                    });
                }

                _logger.LogInformation(
                    $"TTS play request - BroadcastId: {request.BroadcastId}, " +
                    $"TtsIds: [{string.Join(", ", request.TtsIds ?? new List<ulong>())}]"
                );

                // DB에서 실제 TTS 정보 조회
                var ttsInfoList = new List<Middleware.WebSocketMiddleware.TtsInfo>();

                if (request.TtsIds != null && request.TtsIds.Any())
                {
                    var ttsItems = await context.Tts
                        .Where(t => request.TtsIds.Contains(t.Id) && (t.DeleteYn != "Y" || t.DeleteYn == null))
                        .Select(t => new Middleware.WebSocketMiddleware.TtsInfo
                        {
                            Id = t.Id,
                            Name = t.Name,
                            Content = t.Content
                        })
                        .ToListAsync();

                    ttsInfoList.AddRange(ttsItems);

                    _logger.LogInformation($"Found {ttsInfoList.Count} TTS items from DB");
                }

                // JsonElement 형태로 변환하여 TtsBroadcastService에 전달
                var jsonData = JsonSerializer.SerializeToElement(new
                {
                    broadcastId = request.BroadcastId,
                    ttsIds = request.TtsIds ?? new List<ulong>()
                });

                // 실제 TTS 정보를 전달
                var result = await _ttsBroadcastService.HandlePlayRequestAsync(
                    request.BroadcastId,
                    jsonData,
                    ttsInfoList,
                    new List<Services.SpeakerInfo>(), // 스피커 정보는 필요시 서비스 내부에서 처리
                    0 // ChannelId는 서비스 내부에서 필요시 조회
                );

                // 성공 응답
                if (result.Success)
                {
                    return Ok(new TtsPlayResponse
                    {
                        Success = true,
                        SessionId = result.SessionId,
                        Message = result.Message,
                        TtsFiles = result.TtsFiles?.Select(f => new TtsFileInfo
                        {
                            Id = f.Id,
                            Name = f.Name,
                            Status = f.Status
                        }).ToList()
                    });
                }

                // 실패 응답
                return BadRequest(new TtsPlayResponse
                {
                    Success = false,
                    Message = result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting TTS playback");
                return StatusCode(500, new TtsPlayResponse
                {
                    Success = false,
                    Message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// TTS 재생 중지
        /// </summary>
        /// <param name="request">중지 요청 정보</param>
        /// <returns>중지 결과</returns>
        [HttpPost("stop")]
        public async Task<IActionResult> Stop([FromBody] TtsStopRequest request)
        {
            try
            {
                // 요청 유효성 검사
                if (request.BroadcastId == 0)
                {
                    return BadRequest(new TtsStopResponse
                    {
                        Success = false,
                        Message = "BroadcastId is required"
                    });
                }

                _logger.LogInformation($"TTS stop request - BroadcastId: {request.BroadcastId}");

                // TTS 재생 중지
                var success = await _ttsBroadcastService.StopTtsByBroadcastIdAsync(request.BroadcastId);

                if (success)
                {
                    return Ok(new TtsStopResponse
                    {
                        Success = true,
                        Message = "TTS playback stopped successfully"
                    });
                }

                return BadRequest(new TtsStopResponse
                {
                    Success = false,
                    Message = "Failed to stop TTS playback or no active playback found"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping TTS playback");
                return StatusCode(500, new TtsStopResponse
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
        public async Task<IActionResult> GetStatus(ulong broadcastId)
        {
            try
            {
                if (broadcastId == 0)
                {
                    return BadRequest(new { success = false, message = "BroadcastId is required" });
                }

                var status = await _ttsBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);

                return Ok(new
                {
                    success = true,
                    broadcastId = broadcastId,
                    isPlaying = status.IsPlaying,
                    currentIndex = status.CurrentIndex,
                    totalCount = status.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting TTS status");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}