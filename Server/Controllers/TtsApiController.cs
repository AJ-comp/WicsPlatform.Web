// Server/Controllers/TtsController.cs
// Windows 전용 - 가장 간단한 버전

using Microsoft.AspNetCore.Mvc;
using System.Speech.Synthesis;  // NuGet: System.Speech 설치 필요

namespace WicsPlatform.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TtsApiController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<TtsApiController> _logger;

        public TtsApiController(IWebHostEnvironment env, ILogger<TtsApiController> logger)
        {
            _env = env;
            _logger = logger;
        }

        [HttpPost("generate")]
        public IActionResult GenerateTts([FromBody] TtsRequest request)
        {
            try
            {
                // 1. 파일명 생성
                var fileName = $"tts_{DateTime.Now.Ticks}.wav";
                var filePath = Path.Combine(_env.WebRootPath, "Uploads", fileName);

                // Uploads 폴더 생성
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                // 2. TTS 생성
                using (var synth = new SpeechSynthesizer())
                {
                    // 한국어 음성 찾기 (Windows 10/11에는 기본 포함)
                    var voices = synth.GetInstalledVoices();
                    var koreanVoice = voices.FirstOrDefault(v =>
                        v.VoiceInfo.Culture.Name.StartsWith("ko-KR"));

                    if (koreanVoice != null)
                    {
                        synth.SelectVoice(koreanVoice.VoiceInfo.Name);
                        _logger.LogInformation($"한국어 음성 사용: {koreanVoice.VoiceInfo.Name}");
                    }
                    else
                    {
                        _logger.LogWarning("한국어 음성이 없어 기본 음성 사용");
                    }

                    // 속도/음높이 조절 (선택사항)
                    synth.Rate = 0;  // -10 ~ 10 (0이 기본)
                    synth.Volume = 100;  // 0 ~ 100

                    // WAV 파일로 저장
                    synth.SetOutputToWaveFile(filePath);
                    synth.Speak(request.Text);
                }

                _logger.LogInformation($"TTS 생성 완료: {fileName}");

                // 3. URL 반환
                return Ok(new TtsResponse
                {
                    Success = true,
                    AudioUrl = $"/Uploads/{fileName}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TTS 생성 실패");
                return Ok(new TtsResponse
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        // 설치된 음성 목록 조회 (디버깅용)
        [HttpGet("voices")]
        public IActionResult GetVoices()
        {
            try
            {
                using (var synth = new SpeechSynthesizer())
                {
                    var voices = synth.GetInstalledVoices()
                        .Select(v => new
                        {
                            Name = v.VoiceInfo.Name,
                            Culture = v.VoiceInfo.Culture.Name,
                            Gender = v.VoiceInfo.Gender.ToString(),
                            Age = v.VoiceInfo.Age.ToString(),
                            Enabled = v.Enabled
                        })
                        .ToList();

                    return Ok(voices);
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }

    public class TtsRequest
    {
        public string Text { get; set; }
        public string Name { get; set; }  // 선택사항
    }

    public class TtsResponse
    {
        public bool Success { get; set; }
        public string AudioUrl { get; set; }
        public string Error { get; set; }
    }
}