namespace WicsPlatform.Client.Pages;

public partial class BroadcastLiveTab
{
    #region Recovery

    private bool _isRecoveringBroadcast = false;

    /// <summary>
    /// 방송 복구: 이미 로드된 데이터로 방송 세션만 복구
    /// </summary>
    private async Task RecoverBroadcast()
    {
        if (selectedChannel == null) return;

        _logger.LogInformation("========== 방송 복구 시작 ==========");

        try
        {
            ShowRecoveryUI();

            // 방송 시작 (복구 모드 - DB 저장 건너뜀)
            await StartBroadcast(isRecovery: true);

            NotifySuccess("방송 복구 완료", $"'{selectedChannel.Name}' 채널의 방송이 복구되었습니다.");
            _logger.LogInformation("========== 방송 복구 완료 ✅ ==========");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 방송 복구 실패");
            NotifyError("방송 복구 실패", ex);
        }
        finally
        {
            HideRecoveryUI();
        }
    }

    /// <summary>
    /// 복구 UI 표시
    /// </summary>
    private void ShowRecoveryUI()
    {
        _isRecoveringBroadcast = true;
        NotifyInfo("방송 복구", $"'{selectedChannel.Name}' 채널의 진행 중인 방송을 복구하는 중입니다...");
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// 복구 UI 숨기기
    /// </summary>
    private void HideRecoveryUI()
    {
        _isRecoveringBroadcast = false;
        InvokeAsync(StateHasChanged);
    }



    /// <summary>
    /// 복구 완료 알림
    /// </summary>
    private void NotifyRecoveryComplete()
    {
        NotifySuccess("방송 복구 완료",
            $"'{selectedChannel.Name}' 채널의 방송이 성공적으로 복구되었습니다.");
    }

    #endregion
}
