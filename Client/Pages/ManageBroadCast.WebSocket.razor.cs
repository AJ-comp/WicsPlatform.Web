using WicsPlatform.Client.Services;

namespace WicsPlatform.Client.Pages;

public partial class ManageBroadCast
{
    #region WebSocket Event Handlers
    private void SubscribeToWebSocketEvents()
    {
        WebSocketService.OnBroadcastStatusReceived += OnBroadcastStatusReceived;
        WebSocketService.OnConnectionStatusChanged += OnWebSocketConnectionStatusChanged;
    }

    private void UnsubscribeFromWebSocketEvents()
    {
        if (WebSocketService != null)
        {
            WebSocketService.OnBroadcastStatusReceived -= OnBroadcastStatusReceived;
            WebSocketService.OnConnectionStatusChanged -= OnWebSocketConnectionStatusChanged;
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
    #endregion

    #region WebSocket Broadcast Methods
    private async Task<bool> InitializeWebSocketBroadcast(List<ulong> onlineGroups)
    {
        var response = await WebSocketService.StartBroadcastAsync(selectedChannel.Id, onlineGroups);

        if (!response.Success)
        {
            NotifyError("방송 시작 실패", new Exception(response.Error));
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