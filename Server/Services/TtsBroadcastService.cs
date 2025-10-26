using System.Collections.Concurrent;
using System.Speech.Synthesis;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using static WicsPlatform.Server.Middleware.WebSocketMiddleware;

namespace WicsPlatform.Server.Services;

public class TtsBroadcastService : ITtsBroadcastService, IDisposable
{
    private readonly ILogger<TtsBroadcastService> logger;
    private readonly IAudioMixingService audioMixingService;
    private readonly ConcurrentDictionary<ulong, TtsSession> _sessions = new();
    private readonly string _ttsFilePath;

    public event Action<ulong> OnPlaybackCompleted;

    private class TtsSession
    {
        public ulong BroadcastId { get; set; }
        public List<TtsInfo> TtsItems { get; set; }
        public List<string> GeneratedFiles { get; set; } = new();
        public Dictionary<int, string> StreamToFileMap { get; set; } = new(); // 스트림 ID와 파일 경로 매핑
        public int CurrentIndex { get; set; }
        public int CurrentStream { get; set; }
        public bool IsPlaying { get; set; }
        public System.Threading.Timer CheckTimer { get; set; } // 재생 완료 체크 타이머
    }

    public TtsBroadcastService(
        ILogger<TtsBroadcastService> logger,
        IUdpBroadcastService udpService,
        IAudioMixingService audioMixingService,
        IWebHostEnvironment environment)
    {
        this.logger = logger;
        this.audioMixingService = audioMixingService;

        // TTS 임시 파일 저장 경로 설정
        _ttsFilePath = Path.Combine(environment.WebRootPath, "TTS", "Temp");
        if (!Directory.Exists(_ttsFilePath))
        {
            Directory.CreateDirectory(_ttsFilePath);
        }

        // 애플리케이션 종료 시 임시 파일 정리
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public async Task<TtsPlaybackResult> HandlePlayRequestAsync(
        ulong broadcastId,
        JsonElement requestData,
        List<TtsInfo> availableTts,
        List<SpeakerInfo> onlineSpeakers,
        ulong channelId)
    {
        try
        {
            logger.LogInformation($"Starting TTS playback for broadcast: {broadcastId}");

            // 1. 기존 재생 중지 (이전 임시 파일들도 정리됨)
            await StopTtsByBroadcastIdAsync(broadcastId);

            // 2. 재생할 TTS 선택
            var ttsToPlay = SelectTtsToPlay(availableTts, requestData);
            if (!ttsToPlay.Any())
            {
                return new TtsPlaybackResult
                {
                    Success = false,
                    Message = "No TTS items available"
                };
            }

            // 3. 새 세션 생성
            var session = new TtsSession
            {
                BroadcastId = broadcastId,
                TtsItems = ttsToPlay,
                CurrentIndex = 0,
                IsPlaying = true
            };

            // 4. TTS를 음성 파일로 변환 (임시 파일 생성)
            foreach (var tts in ttsToPlay)
            {
                var audioFile = await ConvertTextToSpeech(tts.Id, tts.Content, broadcastId);
                if (!string.IsNullOrEmpty(audioFile))
                {
                    session.GeneratedFiles.Add(audioFile);
                }
            }

            if (!session.GeneratedFiles.Any())
            {
                return new TtsPlaybackResult
                {
                    Success = false,
                    Message = "Failed to generate TTS audio files"
                };
            }

            _sessions[broadcastId] = session;

            // 5. 첫 번째 파일 재생 시작
            await PlayNextFile(broadcastId);

            // 6. 재생 완료 체크 타이머 시작 (500ms 간격)
            session.CheckTimer = new System.Threading.Timer(
                async _ => await CheckPlaybackStatus(broadcastId),
                null,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(500));

            // 7. 결과 반환
            var result = new TtsPlaybackResult
            {
                SessionId = Guid.NewGuid().ToString(),
                Success = true,
                Message = $"Started TTS playback of {ttsToPlay.Count} items"
            };

            foreach (var tts in ttsToPlay)
            {
                result.TtsFiles.Add(new TtsFileStatus
                {
                    Id = tts.Id,
                    Name = tts.Name,
                    Status = "ready"
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error starting TTS playback for broadcast {broadcastId}");
            return new TtsPlaybackResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private async Task CheckPlaybackStatus(ulong broadcastId)
    {
        try
        {
            if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsPlaying)
                return;

            // AudioMixingService에서 TTS 재생 상태 확인
            bool hasTts = audioMixingService.HasActiveTtsStream(broadcastId);

            if (!hasTts)
            {
                // TTS 재생이 모두 완료됨
                logger.LogInformation($"All TTS playback completed for broadcast {broadcastId}");
                
                // 세션 정리
                await StopTtsByBroadcastIdAsync(broadcastId);
                
                // 재생 완료 이벤트 발생
                OnPlaybackCompleted?.Invoke(broadcastId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error checking TTS playback status for broadcast {broadcastId}");
        }
    }

    private async Task<string> ConvertTextToSpeech(ulong ttsId, string text, ulong broadcastId)
    {
        try
        {
            // 임시 파일명 생성 (브로드캐스트 ID와 타임스탬프 포함)
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fileName = $"tts_{broadcastId}_{ttsId}_{timestamp}.wav";
            var filePath = Path.Combine(_ttsFilePath, fileName);

            // Windows Speech API를 사용한 TTS 생성
            using (var synthesizer = new SpeechSynthesizer())
            {
                // 한국어 음성 설정
                var voices = synthesizer.GetInstalledVoices();
                var koreanVoice = voices.FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith("ko"));
                if (koreanVoice != null)
                {
                    synthesizer.SelectVoice(koreanVoice.VoiceInfo.Name);
                }

                // 속도 및 볼륨 설정
                synthesizer.Rate = 0;  // -10 ~ 10 (0이 기본값)
                synthesizer.Volume = 100;  // 0 ~ 100

                // WAV 파일로 저장
                synthesizer.SetOutputToWaveFile(filePath);
                synthesizer.Speak(text);
                synthesizer.SetOutputToNull();
            }

            logger.LogInformation($"TTS temp file generated: {fileName}");
            return $"/TTS/Temp/{fileName}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to convert TTS ID {ttsId} to speech");
            return null;
        }
    }

    private async Task PlayNextFile(ulong broadcastId)
    {
        if (!_sessions.TryGetValue(broadcastId, out var session) || !session.IsPlaying)
            return;

        if (session.CurrentIndex >= session.GeneratedFiles.Count)
        {
            // 플레이리스트 종료 - 타이머가 자동으로 감지하여 정리함
            logger.LogInformation($"TTS playlist completed for broadcast {broadcastId}");
            return;
        }

        var audioPath = session.GeneratedFiles[session.CurrentIndex];
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", audioPath.TrimStart('/'));

        if (!File.Exists(fullPath))
        {
            logger.LogError($"TTS audio file not found: {fullPath}");
            session.CurrentIndex++;
            await PlayNextFile(broadcastId);
            return;
        }

        // AddTtsStream 사용
        var ttsStreamId = await audioMixingService.AddTtsStream(broadcastId, audioPath);

        if (ttsStreamId == 0)
        {
            logger.LogError($"Failed to add TTS to mixer");
            session.CurrentIndex++;
            await PlayNextFile(broadcastId);
            return;
        }

        // 스트림 ID와 파일 경로 매핑 저장
        session.StreamToFileMap[ttsStreamId] = fullPath;

        var currentTtsInfo = session.TtsItems[session.CurrentIndex];
        logger.LogInformation($"Playing TTS: {currentTtsInfo.Name} ({session.CurrentIndex + 1}/{session.GeneratedFiles.Count})");
    }

    public async Task<bool> StopTtsByBroadcastIdAsync(ulong broadcastId)
    {
        try
        {
            logger.LogInformation($"Stopping TTS for broadcast: {broadcastId}");

            // 1. 믹서에서 모든 TTS 스트림 제거
            await audioMixingService.RemoveAllTtsStreams(broadcastId);

            // 2. 세션 정리
            if (_sessions.TryRemove(broadcastId, out var session))
            {
                session.IsPlaying = false;

                // 타이머 정지
                session.CheckTimer?.Dispose();

                // 모든 임시 파일 삭제
                DeleteAllTempFiles(session);

                logger.LogInformation($"TTS stopped and temp files cleaned for broadcast {broadcastId}");
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error stopping TTS for broadcast {broadcastId}");
            return false;
        }
    }

    private void DeleteTempFile(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                logger.LogDebug($"Deleted TTS temp file: {Path.GetFileName(fullPath)}");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, $"Failed to delete TTS temp file: {fullPath}");
        }
    }

    private void DeleteAllTempFiles(TtsSession session)
    {
        // 생성된 모든 임시 파일 삭제
        foreach (var file in session.GeneratedFiles)
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", file.TrimStart('/'));
            DeleteTempFile(fullPath);
        }

        // 매핑에 남아있는 파일들도 삭제
        foreach (var filePath in session.StreamToFileMap.Values)
        {
            DeleteTempFile(filePath);
        }

        session.GeneratedFiles.Clear();
        session.StreamToFileMap.Clear();
    }

    public async Task<TtsPlaybackStatus> GetStatusByBroadcastIdAsync(ulong broadcastId)
    {
        if (_sessions.TryGetValue(broadcastId, out var session) && session.IsPlaying)
        {
            return new TtsPlaybackStatus
            {
                SessionId = broadcastId.ToString(),
                IsPlaying = true,
                CurrentIndex = session.CurrentIndex,
                TotalCount = session.TtsItems.Count
            };
        }

        return new TtsPlaybackStatus
        {
            SessionId = broadcastId.ToString(),
            IsPlaying = false
        };
    }

    private List<TtsInfo> SelectTtsToPlay(List<TtsInfo> availableTts, JsonElement requestData)
    {
        if (availableTts == null || !availableTts.Any())
            return new List<TtsInfo>();

        // 요청에 특정 TTS ID가 있으면 해당 TTS만 선택
        if (requestData.TryGetProperty("ttsIds", out var ttsIdsElement))
        {
            var requestedIds = new List<ulong>();
            foreach (var idElement in ttsIdsElement.EnumerateArray())
            {
                if (idElement.TryGetUInt64(out var ttsId))
                {
                    requestedIds.Add(ttsId);
                }
            }

            if (requestedIds.Any())
            {
                return availableTts.Where(t => requestedIds.Contains(t.Id)).ToList();
            }
        }

        // 요청된 ID가 없으면 모든 TTS 재생
        return availableTts.ToList();
    }

    private void CleanupAllTempFiles()
    {
        try
        {
            // Temp 폴더의 모든 파일 삭제
            if (Directory.Exists(_ttsFilePath))
            {
                var tempFiles = Directory.GetFiles(_ttsFilePath, "*.wav");
                foreach (var file in tempFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // 파일 삭제 실패는 무시
                    }
                }
                logger.LogInformation($"Cleaned up {tempFiles.Length} TTS temp files on shutdown");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cleanup TTS temp files on shutdown");
        }
    }

    private void OnProcessExit(object sender, EventArgs e)
    {
        // 애플리케이션 종료 시 모든 임시 파일 정리
        CleanupAllTempFiles();
    }

    public void Dispose()
    {
        // 모든 세션 정리
        foreach (var kvp in _sessions)
        {
            _ = StopTtsByBroadcastIdAsync(kvp.Key);
        }

        _sessions.Clear();

        // 모든 임시 파일 정리
        CleanupAllTempFiles();

        // 이벤트 핸들러 제거
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }
}
