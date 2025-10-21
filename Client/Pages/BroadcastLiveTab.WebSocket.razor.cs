using WicsPlatform.Client.Services;

namespace WicsPlatform.Client.Pages;

public partial class BroadcastLiveTab
{
    #region WebSocket Event Handlers
    private void SubscribeToWebSocketEvents()
    {
        WebSocketService.OnBroadcastStatusReceived += OnBroadcastStatusReceived;
        WebSocketService.OnConnectionStatusChanged += OnWebSocketConnectionStatusChanged;
        WebSocketService.OnPlaybackCompleted += OnServerPlaybackCompleted; // 재생 완료 수신 등록
    }

    private void UnsubscribeFromWebSocketEvents()
    {
        if (WebSocketService != null)
        {
            WebSocketService.OnBroadcastStatusReceived -= OnBroadcastStatusReceived;
            WebSocketService.OnConnectionStatusChanged -= OnWebSocketConnectionStatusChanged;
            WebSocketService.OnPlaybackCompleted -= OnServerPlaybackCompleted;
        }
    }

    private void OnBroadcastStatusReceived(ulong broadcastId, BroadcastStatus status)
    {
        if (broadcastId == currentBroadcastId)
        {
            _logger.LogDebug($"Broadcast status update - Packets: {status.PacketCount}, Bytes: {status.TotalBytes}");
        }
    }

    private void OnWebSocketConnectionStatusChanged(ulong broadcastId, string status)
    {
        if (broadcastId != currentBroadcastId) return;

        switch (status)
        {
            case "Connected":
                NotifySuccess("연결 성공", $"채널 {selectedChannel?.Name}의 실시간 방송이 시작되었습니다.");
                break;
            case "Disconnected":
                HandleWebSocketDisconnection(broadcastId);
                break;
        }
    }

    private void HandleWebSocketDisconnection(ulong broadcastId)
    {
        NotifyError("연결 끊김", new Exception($"채널 {selectedChannel?.Name}의 방송 연결이 끊어졌습니다."));

        if (isBroadcasting && broadcastId == currentBroadcastId)
        {
            isBroadcasting = false;
            currentBroadcastId = null;
            InvokeAsync(StateHasChanged);
        }
    }

    // 서버에서 모든 재생 완료(playbackCompleted) 수신 시: 방송은 유지하고, 재생 전 상태로만 복귀
    private void OnServerPlaybackCompleted(ulong broadcastId)
    {
        if (currentBroadcastId.HasValue && broadcastId == currentBroadcastId.Value)
        {
            _logger.LogInformation($"Playback completed for broadcast {broadcastId} - resetting playlist/TTS state only");
            // 방송 상태(isBroadcasting)나 상위 UI는 변경하지 않음
            playlistSection?.ResetMediaPlaybackState();
            ttsSection?.ResetTtsPlaybackState();
            InvokeAsync(StateHasChanged);
        }
    }
    #endregion

    #region WebSocket Broadcast Methods
    private async Task<bool> InitializeWebSocketBroadcast(List<ulong> onlineGroups)
    {
        // 빈 리스트면 null로 전달 (서버가 채널 매핑 기반으로 조회하도록)
        var groupIds = onlineGroups != null && onlineGroups.Count > 0 ? onlineGroups : null;
        var response = await WebSocketService.StartBroadcastAsync(selectedChannel.Id, groupIds);

        if (!response.Success)
        {
            NotifyError("방송 시작 실패", new Exception(response.Error));
            return false;
        }

        currentBroadcastId = response.BroadcastId;
        _logger.LogInformation($"WebSocket broadcast started with ID: {currentBroadcastId}");

        // 복구 시나리오 처리
        _logger.LogInformation($"[InitializeWebSocketBroadcast] ConnectedResponse={response.ConnectedResponse != null}, IsRecovery={response.ConnectedResponse?.IsRecovery}, PlaybackState={response.ConnectedResponse?.PlaybackState != null}");
        
        if (response.ConnectedResponse != null && response.ConnectedResponse.IsRecovery && response.ConnectedResponse.PlaybackState != null)
        {
            _logger.LogInformation($"[InitializeWebSocketBroadcast] 복구 시나리오 감지! Source={response.ConnectedResponse.PlaybackState.Source}");
            
            if (response.ConnectedResponse.PlaybackState.Source == "media")
            {
                _logger.LogInformation("[InitializeWebSocketBroadcast] playlistSection.RestorePlaybackState 호출");
                // 플레이리스트 섹션에 재생 상태 복원
                playlistSection?.RestorePlaybackState(response.ConnectedResponse.PlaybackState);
                _logger.LogInformation("[InitializeWebSocketBroadcast] RestorePlaybackState 호출 완료");
            }
            else if (response.ConnectedResponse.PlaybackState.Source == "tts")
            {
                _logger.LogInformation("[InitializeWebSocketBroadcast] ttsSection.RestorePlaybackState 호출");
                // TTS 섹션에 재생 상태 복원
                ttsSection?.RestorePlaybackState();
            }
        }
        else
        {
            _logger.LogInformation("[InitializeWebSocketBroadcast] 복구 시나리오 아님 - 일반 방송 시작");
        }

        return true;
    }

    private async Task StopWebSocketBroadcast()
    {
        if (currentBroadcastId.HasValue)
        {
            var broadcastIdToStop = currentBroadcastId.Value;
            currentBroadcastId = null;

            try
            {
                await WebSocketService.StopBroadcastAsync(broadcastIdToStop);
                _logger.LogInformation($"WebSocket 종료 완료: {broadcastIdToStop}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WebSocket 종료 실패: {broadcastIdToStop}");
            }
        }
    }
    #endregion
}
