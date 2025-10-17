using WicsPlatform.Client.Services;

namespace WicsPlatform.Client.Pages;

public partial class BroadcastLiveTab
{
    #region WebSocket Event Handlers
    private void SubscribeToWebSocketEvents()
    {
        WebSocketService.OnBroadcastStatusReceived += OnBroadcastStatusReceived;
        WebSocketService.OnConnectionStatusChanged += OnWebSocketConnectionStatusChanged;
        WebSocketService.OnPlaybackCompleted += OnServerPlaybackCompleted; // ��� �Ϸ� ���� ���
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
                NotifySuccess("���� ����", $"ä�� {selectedChannel?.Name}�� �ǽð� ����� ���۵Ǿ����ϴ�.");
                break;
            case "Disconnected":
                HandleWebSocketDisconnection(broadcastId);
                break;
        }
    }

    private void HandleWebSocketDisconnection(ulong broadcastId)
    {
        NotifyError("���� ����", new Exception($"ä�� {selectedChannel?.Name}�� ��� ������ ���������ϴ�."));

        if (isBroadcasting && broadcastId == currentBroadcastId)
        {
            isBroadcasting = false;
            currentBroadcastId = null;
            InvokeAsync(StateHasChanged);
        }
    }

    // �������� ��� ��� �Ϸ�(playbackCompleted) ���� ��: ����� �����ϰ�, ��� �� ���·θ� ����
    private void OnServerPlaybackCompleted(ulong broadcastId)
    {
        if (currentBroadcastId.HasValue && broadcastId == currentBroadcastId.Value)
        {
            _logger.LogInformation($"Playback completed for broadcast {broadcastId} - resetting playlist/TTS state only");
            // ��� ����(isBroadcasting)�� ���� UI�� �������� ����
            playlistSection?.ResetMediaPlaybackState();
            ttsSection?.ResetTtsPlaybackState();
            InvokeAsync(StateHasChanged);
        }
    }
    #endregion

    #region WebSocket Broadcast Methods
    private async Task<bool> InitializeWebSocketBroadcast(List<ulong> onlineGroups)
    {
        var response = await WebSocketService.StartBroadcastAsync(selectedChannel.Id, onlineGroups);

        if (!response.Success)
        {
            NotifyError("��� ���� ����", new Exception(response.Error));
            return false;
        }

        currentBroadcastId = response.BroadcastId;
        _logger.LogInformation($"WebSocket broadcast started with ID: {currentBroadcastId}");
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
                _logger.LogInformation($"WebSocket ���� �Ϸ�: {broadcastIdToStop}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WebSocket ���� ����: {broadcastIdToStop}");
            }
        }
    }
    #endregion
}
