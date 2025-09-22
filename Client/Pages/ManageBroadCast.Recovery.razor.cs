using System.Net.Http.Json;

namespace WicsPlatform.Client.Pages;

public partial class ManageBroadCast
{
    #region Recovery Fields
    private bool _isRecoveringBroadcast = false;
    private WicsPlatform.Server.Models.wics.Broadcast _broadcastToRecover = null;
    #endregion

    #region Recovery Entry Point

    /// <summary>
    /// 채널 선택 시 복구 필요 여부를 확인하고 필요시 복구 프로세스 시작
    /// </summary>
    public async Task CheckAndRecoverIfNeeded(ulong channelId)
    {
        try
        {
            _logger.LogInformation($"CheckAndRecoverIfNeeded started for channel {channelId}");

            // 1. 진행 중인 방송이 있는지 확인
            var ongoingBroadcast = await FindOngoingBroadcast(channelId);

            if (ongoingBroadcast == null)
            {
                _logger.LogInformation($"No ongoing broadcast found for channel {channelId}");
                // 방송이 없으면 SubPage들을 비방송 상태로 초기화
                InitializeSubPagesForNoBroadcast();
                return;
            }

            // 2. 복구가 필요하다고 판단되면 복구 프로세스 시작
            _logger.LogInformation($"Ongoing broadcast found for channel {channelId}, starting recovery process");
            _broadcastToRecover = ongoingBroadcast;

            // 3. 복구 프로세스 실행
            await StartRecoveryProcess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking recovery need for channel {channelId}");
            // 복구 체크 실패는 치명적이지 않으므로 정상 진행
            InitializeSubPagesForNoBroadcast();
        }
    }

    /// <summary>
    /// SubPage들을 비방송 상태로 초기화
    /// </summary>
    private void InitializeSubPagesForNoBroadcast()
    {
        _logger.LogInformation("Initializing SubPages for non-broadcasting state");

        // 모니터링 섹션 초기화
        if (monitoringSection != null)
        {
            monitoringSection.ResetBroadcastState();
        }

        // 플레이리스트 섹션 초기화
        if (playlistSection != null)
        {
            playlistSection.ResetMediaPlaybackState();
        }

        // TTS 섹션 초기화
        if (ttsSection != null)
        {
            ttsSection.ResetTtsPlaybackState();
        }

        // 스피커 섹션은 선택 상태만 초기화
        if (speakerSection != null)
        {
            speakerSection.ClearSelection();
        }
    }

    #endregion

    #region Recovery Process

    /// <summary>
    /// 복구 프로세스 메인 로직
    /// </summary>
    private async Task StartRecoveryProcess()
    {
        if (_broadcastToRecover == null) return;

        try
        {
            // 1. 복구 중 UI 표시
            ShowRecoveryUI();

            // 2. 복구 데이터 준비 (스피커, 미디어, TTS 선택 복원)
            await PrepareRecoverySelections();

            // 3. 일반 방송 시작 (복구와 무관하게)
            await StartBroadcast();

            // 4. 방송 시작 후 추가 복구 작업
            await PerformPostStartRecovery();

            // 5. 복구 완료 알림
            NotifyRecoveryComplete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery process failed");
            NotifyError("방송 복구 실패", ex);
            // 실패 시에도 SubPage 초기화
            InitializeSubPagesForNoBroadcast();
        }
        finally
        {
            // 6. 복구 UI 숨기기
            HideRecoveryUI();
            _broadcastToRecover = null;
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
    /// 방송 시작 전 복구 데이터 준비
    /// </summary>
    private async Task PrepareRecoverySelections()
    {
        if (_broadcastToRecover == null) return;

        try
        {
            _logger.LogInformation("Preparing recovery selections...");

            // 1. 스피커 그룹 선택 복원
            await RestoreSpeakerGroups(_broadcastToRecover.SpeakerIdList);

            // 2. 미디어 선택 복원
            await RestoreMediaSelection(_broadcastToRecover.MediaIdList);

            // 3. TTS 선택 복원
            await RestoreTtsSelection(_broadcastToRecover.TtsIdList);

            // 4. 루프백 설정 복원
            _currentLoopbackSetting = _broadcastToRecover.LoopbackYn == "Y";

            _logger.LogInformation($"Recovery selections prepared - " +
                $"Speakers: {_broadcastToRecover.SpeakerIdList}, " +
                $"Media: {_broadcastToRecover.MediaIdList}, " +
                $"TTS: {_broadcastToRecover.TtsIdList}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare recovery selections");
            throw;
        }
    }

    /// <summary>
    /// 방송 시작 후 추가 복구 작업
    /// </summary>
    private async Task PerformPostStartRecovery()
    {
        if (_broadcastToRecover == null) return;

        try
        {
            _logger.LogInformation("Performing post-start recovery tasks...");

            // 기존 브로드캐스트 레코드의 OngoingYn을 'N'으로 변경
            await MarkOldBroadcastAsStopped(_broadcastToRecover.Id);

            // 필요한 경우 추가 복구 작업
            // 예: 재생 중이던 미디어 위치 복원, 볼륨 설정 복원 등

            _logger.LogInformation("Post-start recovery completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post-start recovery failed");
            // 이 실패는 치명적이지 않으므로 계속 진행
        }
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

    #region Recovery Helper Methods

    /// <summary>
    /// 진행 중인 방송 찾기
    /// </summary>
    private async Task<WicsPlatform.Server.Models.wics.Broadcast> FindOngoingBroadcast(ulong channelId)
    {
        var query = new Radzen.Query
        {
            Filter = $"ChannelId eq {channelId} and OngoingYn eq 'Y'",
            Top = 1,
            OrderBy = "CreatedAt desc"
        };

        var broadcasts = await WicsService.GetBroadcasts(query);
        return broadcasts.Value.FirstOrDefault();
    }

    /// <summary>
    /// 기존 브로드캐스트를 종료 상태로 변경
    /// </summary>
    private async Task MarkOldBroadcastAsStopped(ulong broadcastId)
    {
        try
        {
            var updateData = new
            {
                OngoingYn = "N",
                UpdatedAt = DateTime.Now
            };

            var response = await Http.PatchAsJsonAsync(
                $"odata/wics/Broadcasts(Id={broadcastId})",
                updateData
            );

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"Old broadcast {broadcastId} marked as stopped");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to mark old broadcast {broadcastId} as stopped");
        }
    }

    /// <summary>
    /// 스피커 그룹 선택 복원
    /// </summary>
    private async Task RestoreSpeakerGroups(string speakerIdList)
    {
        if (string.IsNullOrEmpty(speakerIdList) || speakerSection == null)
            return;

        try
        {
            var speakerIds = ParseIdList(speakerIdList);
            if (!speakerIds.Any()) return;

            var groupIds = await GetGroupIdsFromSpeakers(speakerIds);

            // 먼저 선택 초기화
            speakerSection.ClearSelection();

            // 그룹 선택 복원
            foreach (var groupId in groupIds)
            {
                await speakerSection.ToggleGroupSelection(groupId);
            }

            _logger.LogInformation($"Restored {groupIds.Count} speaker groups");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore speaker groups");
        }
    }

    /// <summary>
    /// 미디어 선택 복원
    /// </summary>
    private async Task RestoreMediaSelection(string mediaIdList)
    {
        if (string.IsNullOrEmpty(mediaIdList) || playlistSection == null)
            return;

        try
        {
            var mediaIds = ParseIdList(mediaIdList);
            if (mediaIds.Any())
            {
                await playlistSection.RecoverSelectedMedia(mediaIds);
                _logger.LogInformation($"Restored {mediaIds.Count} media selections");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore media selection");
        }
    }

    /// <summary>
    /// TTS 선택 복원
    /// </summary>
    private async Task RestoreTtsSelection(string ttsIdList)
    {
        if (string.IsNullOrEmpty(ttsIdList) || ttsSection == null)
            return;

        try
        {
            var ttsIds = ParseIdList(ttsIdList);
            if (ttsIds.Any())
            {
                await ttsSection.RecoverSelectedTts(ttsIds);
                _logger.LogInformation($"Restored {ttsIds.Count} TTS selections");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore TTS selection");
        }
    }

    /// <summary>
    /// ID 리스트 파싱
    /// </summary>
    private List<ulong> ParseIdList(string idList)
    {
        if (string.IsNullOrEmpty(idList))
            return new List<ulong>();

        return idList.Split(' ')
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => ulong.TryParse(s, out var id) ? id : (ulong?)null)
            .Where(id => id.HasValue)
            .Select(id => id.Value)
            .ToList();
    }

    /// <summary>
    /// 스피커 ID로부터 그룹 ID 가져오기
    /// </summary>
    private async Task<List<ulong>> GetGroupIdsFromSpeakers(List<ulong> speakerIds)
    {
        var mappings = await WicsService.GetMapSpeakerGroups(
            new Radzen.Query { Filter = $"LastYn eq 'Y'" });

        return mappings.Value
            .Where(m => speakerIds.Contains(m.SpeakerId))
            .Select(m => m.GroupId)
            .Distinct()
            .ToList();
    }

    #endregion
}