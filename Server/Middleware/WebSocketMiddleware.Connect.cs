using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Services;
using WicsPlatform.Shared;
using static WicsPlatform.Server.Services.IBroadcastPreparationService;

namespace WicsPlatform.Server.Middleware;

public partial class WebSocketMiddleware
{
    private async Task HandleConnectAsync(WebSocket webSocket, string connectionId, ulong channelId, JsonElement root)
    {
        Debug.WriteLine($"[HandleConnectAsync] ========== 시작 ==========");
        
        // 1. 요청 파싱 및 검증
        var request = ParseConnectRequest(root);
        if (request == null) return;

        var broadcastId = request.BroadcastId;
        var selectedGroupIds = request.SelectedGroupIds;
        Debug.WriteLine($"[HandleConnectAsync] BroadcastId: {broadcastId}, ChannelId: {channelId}");

        // 2. 기존 세션 확인
        LogExistingSession(broadcastId);

        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<wicsContext>();

        // 3. 방송 리소스 준비 (스피커, 미디어, TTS)
        var prepared = await PrepareBroadcastResourcesAsync(channelId, selectedGroupIds, scope);

        // 4. 스피커 검증 및 로깅
        if (!await ValidateAndLogSpeakersAsync(webSocket, broadcastId, channelId, prepared))
            return;

        // 5. 세션 생성 및 등록
        CreateAndRegisterSession(webSocket, connectionId, broadcastId, channelId, selectedGroupIds, prepared);

        // 6. 오디오 믹서 초기화 또는 복구
        bool isRecovery = await IsRecoveryScenarioAsync(broadcastId);
        if (isRecovery)
        {
            await RecoveryAudioMixerAsync(broadcastId, channelId, prepared.Speakers);
        }
        else
        {
            await InitializeAudioMixerAsync(broadcastId, channelId, prepared.Speakers);
        }

        // 7. 볼륨 설정 적용
        await ApplyVolumeSettingsAsync(context, broadcastId, channelId);

        // 8. 스피커 인계 로깅
        LogSpeakerTakeovers(prepared.Takeovers);

        // 9. Connect 성공 응답 전송 (복구 시 재생 상태 포함)
        await SendConnectResponseAsync(webSocket, broadcastId, isRecovery);

        logger.LogInformation($"Broadcast session created: {broadcastId}");
        Debug.WriteLine($"[HandleConnectAsync] ========== 완료 ==========");
    }

    /// <summary>
    /// Connect 요청 파싱
    /// </summary>
    private ConnectBroadcastRequest ParseConnectRequest(JsonElement root)
    {
        var req = JsonSerializer.Deserialize<ConnectBroadcastRequest>(root);
        if (req == null)
        {
            logger.LogWarning("Failed to deserialize ConnectBroadcastRequest");
        }
        return req;
    }

    /// <summary>
    /// 기존 세션 존재 여부 로깅
    /// </summary>
    private void LogExistingSession(ulong broadcastId)
    {
        bool hasSession = _broadcastSessions.TryGetValue(broadcastId, out var existingSession);
        Debug.WriteLine($"[HandleConnectAsync] 기존 세션 존재 여부: {hasSession}");
        
        if (hasSession)
        {
            logger.LogInformation($"Found existing session for broadcast {broadcastId}, will be replaced");
        }
    }

    /// <summary>
    /// 방송 리소스 준비 (스피커, 미디어, TTS)
    /// </summary>
    private async Task<PreparedBroadcast> PrepareBroadcastResourcesAsync(
        ulong channelId, 
        List<ulong> selectedGroupIds, 
        IServiceScope scope)
    {
        var prepService = scope.ServiceProvider.GetRequiredService<IBroadcastPreparationService>();
        var prepared = await prepService.PrepareAsync(channelId, selectedGroupIds);
        
        Debug.WriteLine($"[HandleConnectAsync] 리소스 준비 완료: 스피커 {prepared.Speakers.Count}개, 미디어 {prepared.Media.Count}개, TTS {prepared.Tts.Count}개");
        
        return prepared;
    }

    /// <summary>
    /// 스피커 검증 및 상세 로깅
    /// </summary>
    private async Task<bool> ValidateAndLogSpeakersAsync(
        WebSocket webSocket,
        ulong broadcastId, 
        ulong channelId,
        PreparedBroadcast prepared)
    {
        var onlineSpeakers = prepared.Speakers;
        var selectedMedia = prepared.Media;
        var selectedTts = prepared.Tts;

        // 스피커 검증
        if (!onlineSpeakers.Any())
        {
            // 스피커가 0대인 경우에도 세션/믹서/복구 루틴은 그대로 진행한다.
            // 단, 경고 로그만 남겨 운영자가 상태를 파악할 수 있도록 한다.
            logger.LogWarning($"No speakers available for broadcast {broadcastId} (channel {channelId})");
        }

        // 상세 로깅
        logger.LogInformation($"Broadcast {broadcastId} - Found {onlineSpeakers.Count} online speakers for channel {channelId}");
        logger.LogInformation($"  - 선택된 미디어: {selectedMedia.Count}개");
        logger.LogInformation($"  - 선택된 TTS: {selectedTts.Count}개");

        // VPN 사용 스피커 로그
        var vpnSpeakers = onlineSpeakers.Where(s => s.UseVpn).ToList();
        if (vpnSpeakers.Any())
        {
            logger.LogInformation($"VPN speakers: {string.Join(", ", vpnSpeakers.Select(s => $"{s.Name}({s.Ip})"))}");
        }

        return true;
    }

    /// <summary>
    /// 방송 세션 생성 및 등록
    /// </summary>
    private void CreateAndRegisterSession(
        WebSocket webSocket,
        string connectionId,
        ulong broadcastId,
        ulong channelId,
        List<ulong> selectedGroupIds,
        PreparedBroadcast prepared)
    {
        var session = new BroadcastSession
        {
            BroadcastId = broadcastId,
            ChannelId = channelId,
            ConnectionId = connectionId,
            StartTime = DateTime.UtcNow,
            SelectedGroupIds = selectedGroupIds,
            WebSocket = webSocket,
            OnlineSpeakers = prepared.Speakers,
            SelectedMedia = prepared.Media.ToList(),
            SelectedTts = prepared.Tts.ToList(),
        };

        _broadcastSessions[broadcastId] = session;
        Debug.WriteLine($"[HandleConnectAsync] 세션 등록 완료: BroadcastId={broadcastId}");
    }

    /// <summary>
    /// 복구 시나리오인지 판별 (미디어 또는 TTS 재생 중)
    /// </summary>
    private async Task<bool> IsRecoveryScenarioAsync(ulong broadcastId)
    {
        // 미디어 재생 상태 확인
        var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
        if (mediaStatus?.IsPlaying == true)
        {
            logger.LogInformation($"Media is playing for broadcast {broadcastId}, entering recovery mode");
            return true;
        }

        // TTS 재생 상태 확인
        var ttsStatus = await ttsBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
        if (ttsStatus?.IsPlaying == true)
        {
            logger.LogInformation($"TTS is playing for broadcast {broadcastId}, entering recovery mode");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 오디오 믹서 초기화 (일반 방송 시작)
    /// </summary>
    private async Task InitializeAudioMixerAsync(
        ulong broadcastId, 
        ulong channelId, 
        List<SpeakerInfo> speakers)
    {
        await audioMixingService.InitializeMixer(broadcastId, channelId, speakers);
        logger.LogInformation($"Mixer initialized for broadcast {broadcastId}");
        Debug.WriteLine($"[HandleConnectAsync] 오디오 믹서 초기화 완료");
    }

    /// <summary>
    /// 오디오 믹서 복구 (기존 믹서 유지, 마이크만 재연결)
    /// </summary>
    private async Task RecoveryAudioMixerAsync(
        ulong broadcastId, 
        ulong channelId, 
        List<SpeakerInfo> speakers)
    {
        logger.LogInformation($"Recovering mixer for broadcast {broadcastId}, keeping existing media/TTS streams");
        Debug.WriteLine($"[HandleConnectAsync] 복구 모드: 기존 믹서 유지, 마이크만 재연결 대기");
        
        // 기존 믹서 세션 완전 유지
        // - 마이크: 클라이언트가 오디오 데이터를 보내면 AddMicrophoneData에서 자동으로 재연결됨
        // - 미디어: 현재 재생 중인 스트림 그대로 유지
        // - TTS: 현재 재생 중인 스트림 그대로 유지
        // - 스피커: 기존 스피커 리스트로 계속 UDP 전송 (BroadcastSession에는 새 리스트 반영됨)
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 채널 볼륨 설정 적용
    /// </summary>
    private async Task ApplyVolumeSettingsAsync(
        wicsContext context,
        ulong broadcastId, 
        ulong channelId)
    {
        var channel = await context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelId);

        if (channel != null)
        {
            await audioMixingService.SetVolume(broadcastId, AudioSource.Microphone, channel.MicVolume);
            await audioMixingService.SetVolume(broadcastId, AudioSource.Media, channel.MediaVolume);
            await audioMixingService.SetVolume(broadcastId, AudioSource.TTS, channel.TtsVolume);

            logger.LogInformation($"Volume settings applied: Mic={channel.MicVolume:F2}, Media={channel.MediaVolume:F2}, TTS={channel.TtsVolume:F2}");
        }
        else
        {
            logger.LogWarning($"Channel {channelId} not found, using default volume settings");
        }
    }

    /// <summary>
    /// 스피커 인계 정보 로깅
    /// </summary>
    private void LogSpeakerTakeovers(List<TakeoverInfo> takeovers)
    {
        if (takeovers.Any())
        {
            foreach (var t in takeovers)
            {
                logger.LogInformation($"Speaker {t.SpeakerId} taken over from channel {t.PreviousChannelId}");
            }
        }
    }

    /// <summary>
    /// Connect 성공 응답 전송 (복구 시 재생 상태 포함)
    /// </summary>
    private async Task SendConnectResponseAsync(WebSocket webSocket, ulong broadcastId, bool isRecovery)
    {
        Debug.WriteLine($"[SendConnectResponseAsync] isRecovery={isRecovery}, broadcastId={broadcastId}");
        object response;

        if (isRecovery && _broadcastSessions.TryGetValue(broadcastId, out var session))
        {
            Debug.WriteLine($"[SendConnectResponseAsync] 복구 시나리오 - 세션 발견");
            
            // 복구 시나리오: 재생 상태 확인
            var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);
            var ttsStatus = await ttsBroadcastService.GetStatusByBroadcastIdAsync(broadcastId);

            Debug.WriteLine($"[SendConnectResponseAsync] mediaStatus.IsPlaying={mediaStatus?.IsPlaying}, ttsStatus.IsPlaying={ttsStatus?.IsPlaying}");

            object playbackState = null;

            if (mediaStatus?.IsPlaying == true)
            {
                Debug.WriteLine($"[SendConnectResponseAsync] 미디어 재생 중! SessionId={mediaStatus.SessionId}");
                var currentMedia = mediaBroadcastService.GetCurrentMedia(broadcastId);
                Debug.WriteLine($"[SendConnectResponseAsync] currentMedia={currentMedia?.fileName}");
                
                playbackState = new
                {
                    source = "media",
                    isPlaying = true,
                    sessionId = mediaStatus.SessionId,
                    selectedGroupIds = session.SelectedGroupIds,
                    mediaList = session.SelectedMedia?.Select(m => new
                    {
                        id = m.Id,
                        fileName = m.FileName,
                        fullPath = m.FullPath
                    }).ToList(),
                    currentMedia = currentMedia != null ? new
                    {
                        mediaId = currentMedia.Value.mediaId,
                        fileName = currentMedia.Value.fileName,
                        currentTrackIndex = mediaStatus.CurrentTrackIndex
                    } : null
                };
                
                Debug.WriteLine($"[SendConnectResponseAsync] playbackState 생성 완료: mediaList={session.SelectedMedia?.Count}개");
            }
            else if (ttsStatus?.IsPlaying == true)
            {
                playbackState = new
                {
                    source = "tts",
                    isPlaying = true,
                    sessionId = ttsStatus.SessionId,
                    selectedGroupIds = session.SelectedGroupIds,
                    ttsList = session.SelectedTts?.Select(t => new
                    {
                        id = t.Id,
                        content = t.Content
                    }).ToList()
                };
            }

            response = new
            {
                type = "connected",
                broadcastId = broadcastId,
                isRecovery = true,
                playbackState = playbackState
            };

            logger.LogInformation($"Sending connect response with playback state for broadcast {broadcastId}");
        }
        else
        {
            // 일반 시나리오: 기본 응답만
            response = new
            {
                type = "connected",
                broadcastId = broadcastId,
                isRecovery = false
            };

            logger.LogInformation($"Sending connect response for broadcast {broadcastId}");
        }

        var jsonResponse = JsonSerializer.Serialize(response);
        Debug.WriteLine($"[SendConnectResponseAsync] 응답 전송: {jsonResponse.Substring(0, Math.Min(200, jsonResponse.Length))}...");
        await SendMessageAsync(webSocket, jsonResponse);
        Debug.WriteLine($"[SendConnectResponseAsync] 응답 전송 완료");
    }


    private async Task HandleDisconnectAsync(JsonElement root)
    {
        Debug.WriteLine($"[HandleDisconnectAsync] ========== 시작 ==========");
        var req = JsonSerializer.Deserialize<DisconnectBroadcastRequest>(root);
        Debug.WriteLine($"[HandleDisconnectAsync] BroadcastId: {req.BroadcastId}");
        Debug.WriteLine($"[HandleDisconnectAsync] forceCleanup=true로 CleanupBroadcastSessionAsync 호출");

        await CleanupBroadcastSessionAsync(req.BroadcastId, true);
        Debug.WriteLine($"[HandleDisconnectAsync] ========== 완료 ==========");
    }


    private async Task StartBroadCastAsync(ulong channelId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<wicsContext>();

        // ✅ 채널 상태를 1(방송 중)로 업데이트
        var channel = await context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel != null)
        {
            channel.State = 1;  // 1 = 방송 중 상태
            channel.UpdatedAt = DateTime.Now;
            await context.SaveChangesAsync();

            logger.LogInformation($"Channel {channelId} state updated to 1 (Broadcasting)");
        }
    }
}
