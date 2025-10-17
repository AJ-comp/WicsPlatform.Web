using System.Net.Http.Json;

namespace WicsPlatform.Client.Pages;

public partial class BroadcastLiveTab
{
    #region Recovery Fields
    private bool _isRecoveringBroadcast = false;
    private WicsPlatform.Server.Models.wics.Broadcast _broadcastToRecover = null;
    #endregion

    #region Recovery Entry Point

    /// <summary>
    /// ä�� ���� �� ���� �ʿ� ���θ� Ȯ���ϰ� �ʿ�� ���� ���μ��� ����
    /// </summary>
    public async Task CheckAndRecoverIfNeeded(ulong channelId)
    {
        try
        {
            _logger.LogInformation($"CheckAndRecoverIfNeeded started for channel {channelId}");

            // 1. ���� ���� ����� �ִ��� Ȯ��
            var ongoingBroadcast = await FindOngoingBroadcast(channelId);

            if (ongoingBroadcast == null)
            {
                _logger.LogInformation($"No ongoing broadcast found for channel {channelId}");
                // ����� ������ SubPage���� ���� ���·� �ʱ�ȭ
                InitializeSubPagesForNoBroadcast();
                return;
            }

            // 2. ������ �ʿ��ϴٰ� �ǴܵǸ� ���� ���μ��� ����
            _logger.LogInformation($"Ongoing broadcast found for channel {channelId}, starting recovery process");
            _broadcastToRecover = ongoingBroadcast;

            // 3. ���� ���μ��� ����
            await StartRecoveryProcess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking recovery need for channel {channelId}");
            // ���� üũ ���д� ġ�������� �����Ƿ� ���� ����
            InitializeSubPagesForNoBroadcast();
        }
    }

    /// <summary>
    /// SubPage���� ���� ���·� �ʱ�ȭ
    /// </summary>
    private void InitializeSubPagesForNoBroadcast()
    {
        _logger.LogInformation("Initializing SubPages for non-broadcasting state");

        // ����͸� ���� �ʱ�ȭ
        if (monitoringSection != null)
        {
            monitoringSection.ResetBroadcastState();
        }

        // �÷��̸���Ʈ ���� �ʱ�ȭ
        if (playlistSection != null)
        {
            playlistSection.ResetMediaPlaybackState();
        }

        // TTS ���� �ʱ�ȭ
        if (ttsSection != null)
        {
            ttsSection.ResetTtsPlaybackState();
        }

        // ����Ŀ ������ ���� ���¸� �ʱ�ȭ
        if (speakerSection != null)
        {
            speakerSection.ClearSelection();
        }
    }

    #endregion

    #region Recovery Process

    /// <summary>
    /// ���� ���μ��� ���� ����
    /// </summary>
    private async Task StartRecoveryProcess()
    {
        if (_broadcastToRecover == null) return;

        try
        {
            // 1. ���� �� UI ǥ��
            ShowRecoveryUI();

            // 2. ���� ������ �غ� (����Ŀ, �̵��, TTS ���� ����)
            await PrepareRecoverySelections();

            // 3. �Ϲ� ��� ���� (������ �����ϰ�)
            await StartBroadcast();

            // 4. ��� ���� �� �߰� ���� �۾�
            await PerformPostStartRecovery();

            // 5. ���� �Ϸ� �˸�
            NotifyRecoveryComplete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery process failed");
            NotifyError("��� ���� ����", ex);
            // ���� �ÿ��� SubPage �ʱ�ȭ
            InitializeSubPagesForNoBroadcast();
        }
        finally
        {
            // 6. ���� UI �����
            HideRecoveryUI();
            _broadcastToRecover = null;
        }
    }

    /// <summary>
    /// ���� UI ǥ��
    /// </summary>
    private void ShowRecoveryUI()
    {
        _isRecoveringBroadcast = true;
        NotifyInfo("��� ����", $"'{selectedChannel.Name}' ä���� ���� ���� ����� �����ϴ� ���Դϴ�...");
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// ���� UI �����
    /// </summary>
    private void HideRecoveryUI()
    {
        _isRecoveringBroadcast = false;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// ��� ���� �� ���� ������ �غ�
    /// </summary>
    private async Task PrepareRecoverySelections()
    {
        if (_broadcastToRecover == null) return;

        try
        {
            _logger.LogInformation("Preparing recovery selections...");

            // 1. ����Ŀ �׷� ���� ����
            await RestoreSpeakerGroups(_broadcastToRecover.SpeakerIdList);

            // 2. �̵�� ���� ����
            await RestoreMediaSelection(_broadcastToRecover.MediaIdList);

            // 3. TTS ���� ����
            await RestoreTtsSelection(_broadcastToRecover.TtsIdList);

            // 4. ������ ���� ����
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
    /// ��� ���� �� �߰� ���� �۾�
    /// </summary>
    private async Task PerformPostStartRecovery()
    {
        if (_broadcastToRecover == null) return;

        try
        {
            _logger.LogInformation("Performing post-start recovery tasks...");

            // ���� ��ε�ĳ��Ʈ ���ڵ��� OngoingYn�� 'N'���� ����
            await MarkOldBroadcastAsStopped(_broadcastToRecover.Id);

            // �ʿ��� ��� �߰� ���� �۾�
            // ��: ��� ���̴� �̵�� ��ġ ����, ���� ���� ���� ��

            _logger.LogInformation("Post-start recovery completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post-start recovery failed");
            // �� ���д� ġ�������� �����Ƿ� ��� ����
        }
    }

    /// <summary>
    /// ���� �Ϸ� �˸�
    /// </summary>
    private void NotifyRecoveryComplete()
    {
        NotifySuccess("��� ���� �Ϸ�",
            $"'{selectedChannel.Name}' ä���� ����� ���������� �����Ǿ����ϴ�.");
    }

    #endregion

    #region Recovery Helper Methods

    /// <summary>
    /// ���� ���� ��� ã��
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
    /// ���� ��ε�ĳ��Ʈ�� ���� ���·� ����
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
    /// ����Ŀ �׷� ���� ����
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

            // ���� ���� �ʱ�ȭ
            speakerSection.ClearSelection();

            // �׷� ���� ����
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
    /// �̵�� ���� ����
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
    /// TTS ���� ����
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
    /// ID ����Ʈ �Ľ�
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
    /// ����Ŀ ID�κ��� �׷� ID ��������
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
