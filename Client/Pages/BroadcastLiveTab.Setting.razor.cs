using Radzen;
using System.Net.Http.Json;
using WicsPlatform.Client.Dialogs;

namespace WicsPlatform.Client.Pages;

public partial class BroadcastLiveTab
{
    protected async Task ToggleBroadcast(ulong channelId)
    {
        if (!isChannelBroadcasting.ContainsKey(channelId))
            isChannelBroadcasting[channelId] = false;

        isChannelBroadcasting[channelId] = !isChannelBroadcasting[channelId];

        var message = isChannelBroadcasting[channelId]
            ? $"ä�� {channelId}�� ����� ���۵Ǿ����ϴ�."
            : $"ä�� {channelId}�� ����� �����Ǿ����ϴ�.";

        var notifyAction = isChannelBroadcasting[channelId]
            ? (Action<string, string>)NotifySuccess
            : NotifyInfo;

        notifyAction(isChannelBroadcasting[channelId] ? "��� ����" : "��� ����", message);
        await InvokeAsync(StateHasChanged);
    }

    #region Audio Settings Dialog
    protected async Task OpenAudioSettingsDialog()
    {
        if (selectedChannel == null)
        {
            NotifyWarn("ä�� ����", "���� ä���� �����ϼ���.");
            return;
        }

        var parameters = new Dictionary<string, object>
        {
            { "Channel", selectedChannel },
            { "IsBroadcasting", isBroadcasting },
            { "PreferredSampleRate", _preferredSampleRate },
            { "PreferredChannels", _preferredChannels }
        };

        var result = await DialogService.OpenAsync<AudioSettingsDialog>(
            "����� ����",
            parameters,
            new DialogOptions
            {
                Width = "500px",
                Height = "auto",
                Resizable = false,
                Draggable = true
            });

        if (result is AudioSettingsResult audioSettings)
        {
            // ���� ����
            _preferredSampleRate = audioSettings.SampleRate;
            _preferredChannels = audioSettings.Channels;

            // ä�ο� ����
            if (selectedChannel != null)
            {
                await BroadcastDataService.UpdateChannelAudioSettingsAsync(
                    selectedChannel,
                    audioSettings.SampleRate,
                    audioSettings.Channels);
            }

            NotifySuccess("����� ����",
                $"������ ����Ǿ����ϴ�. (���÷���Ʈ: {audioSettings.SampleRate}Hz, ä��: {audioSettings.Channels}ch)");
        }
    }
    #endregion

    #region Volume Control Dialog
    protected async Task OpenVolumeControlDialog()
    {
        if (selectedChannel == null)
        {
            NotifyWarn("ä�� ����", "���� ä���� �����ϼ���.");
            return;
        }

        var parameters = new Dictionary<string, object>
        {
            { "Channel", selectedChannel },
            { "CurrentBroadcastId", currentBroadcastId?.ToString() },
            { "IsBroadcasting", isBroadcasting }
        };

        var result = await DialogService.OpenAsync<VolumeControlDialog>(
            "���� ����",
            parameters,
            new DialogOptions
            {
                Width = "600px",
                Height = "auto",
                Resizable = false,
                Draggable = true
            });

        if (result is bool saved && saved)
        {
            // ������ ����Ǿ����� ���� ä�� ������ �ٽ� �ε�
            if (selectedChannel != null)
            {
                var query = new Radzen.Query
                {
                    Filter = $"Id eq {selectedChannel.Id}"
                };
                var updatedChannel = await WicsService.GetChannels(query);
                if (updatedChannel.Value.Any())
                {
                    selectedChannel = updatedChannel.Value.First();
                    micVolume = (int)(selectedChannel.MicVolume * 100);
                    mediaVolume = (int)(selectedChannel.MediaVolume * 100);
                    ttsVolume = (int)(selectedChannel.TtsVolume * 100);
                }
            }

            NotifyInfo("���� ����", "���� ������ ������Ʈ�Ǿ����ϴ�.");
        }
    }
    #endregion



    private async Task SaveSelectedMediaToChannel()
    {
        try
        {
            if (selectedChannel == null) return;

            if (playlistSection == null)
            {
                _logger.LogInformation("�÷��̸���Ʈ ������ �ʱ�ȭ���� �ʾҽ��ϴ�.");
                LoggingService.AddLog("WARN", "�÷��̸���Ʈ ������ �ʱ�ȭ���� �ʾҽ��ϴ�.");
                return;
            }

            var selectedMedia = playlistSection.GetSelectedMedia();
            var selectedMediaIds = selectedMedia?.Select(m => m.Id).ToHashSet() ?? new HashSet<ulong>();

            LoggingService.AddLog("INFO", $"�̵�� ���� ����ȭ ���� - ���õ� �̵��: {selectedMediaIds.Count}��");

            var query = new Radzen.Query
            {
                Filter = $"ChannelId eq {selectedChannel.Id}"
            };
            var existingMappings = await WicsService.GetMapChannelMedia(query);
            var existingMediaIds = existingMappings.Value
                .Where(m => m.DeleteYn != "Y")
                .Select(m => m.MediaId)
                .ToHashSet();

            var toAdd = selectedMediaIds.Except(existingMediaIds);
            foreach (var mediaId in toAdd)
            {
                var media = selectedMedia.FirstOrDefault(m => m.Id == mediaId);
                if (media != null)
                {
                    var mapping = new WicsPlatform.Server.Models.wics.MapChannelMedium
                    {
                        ChannelId = selectedChannel.Id,
                        MediaId = mediaId,
                        DeleteYn = "N",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await WicsService.CreateMapChannelMedium(mapping);
                    LoggingService.AddLog("SUCCESS", $"�ű� �̵�� �߰�: {media.FileName} (ID: {mediaId})");
                }
            }

            var toDelete = existingMediaIds.Except(selectedMediaIds);
            foreach (var mediaId in toDelete)
            {
                var mapping = existingMappings.Value.FirstOrDefault(m => m.MediaId == mediaId && m.DeleteYn != "Y");
                if (mapping != null)
                {
                    var updateData = new
                    {
                        DeleteYn = "Y",
                        UpdatedAt = DateTime.Now
                    };

                    var response = await Http.PatchAsJsonAsync($"odata/wics/MapChannelMedia(Id={mapping.Id})", updateData);

                    if (response.IsSuccessStatusCode)
                    {
                        LoggingService.AddLog("INFO", $"�̵�� ����: MediaID {mediaId} (delete_yn=Y)");
                    }
                    else
                    {
                        LoggingService.AddLog("ERROR", $"�̵�� ���� ����: MediaID {mediaId}, Status: {response.StatusCode}");
                    }
                }
            }

            LoggingService.AddLog("SUCCESS",
                $"�̵�� ���� ����ȭ �Ϸ� - �߰�: {toAdd.Count()}��, ����: {toDelete.Count()}��");
        }
        catch (Exception ex)
        {
            LoggingService.AddLog("ERROR", $"�̵�� ���� ����: {ex.Message}");
            _logger.LogError(ex, "Failed to save selected media to channel");
        }
    }

    private async Task SaveSelectedTtsToChannel()
    {
        try
        {
            if (selectedChannel == null) return;

            if (ttsSection == null)
            {
                _logger.LogInformation("TTS ������ �ʱ�ȭ���� �ʾҽ��ϴ�.");
                LoggingService.AddLog("WARN", "TTS ������ �ʱ�ȭ���� �ʾҽ��ϴ�.");
                return;
            }

            var selectedTts = ttsSection.GetSelectedTts();
            var selectedTtsIds = selectedTts?.Select(t => t.Id).ToHashSet() ?? new HashSet<ulong>();

            LoggingService.AddLog("INFO", $"TTS ���� ����ȭ ���� - ���õ� TTS: {selectedTtsIds.Count}��");

            var query = new Radzen.Query
            {
                Filter = $"ChannelId eq {selectedChannel.Id}"
            };

            var existingMappings = await WicsService.GetMapChannelTts(query);
            var existingTtsIds = existingMappings.Value
                .Where(m => m.DeleteYn != "Y")
                .Select(m => m.TtsId)
                .ToHashSet();

            var toAdd = selectedTtsIds.Except(existingTtsIds);
            foreach (var ttsId in toAdd)
            {
                var tts = selectedTts.FirstOrDefault(t => t.Id == ttsId);
                if (tts != null)
                {
                    var mapping = new WicsPlatform.Server.Models.wics.MapChannelTt
                    {
                        ChannelId = selectedChannel.Id,
                        TtsId = ttsId,
                        DeleteYn = "N",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await WicsService.CreateMapChannelTt(mapping);
                    LoggingService.AddLog("SUCCESS", $"�ű� TTS �߰�: {tts.Name} (ID: {ttsId})");
                }
            }

            var toDelete = existingTtsIds.Except(selectedTtsIds);
            foreach (var ttsId in toDelete)
            {
                var mapping = existingMappings.Value.FirstOrDefault(m => m.TtsId == ttsId && m.DeleteYn != "Y");
                if (mapping != null)
                {
                    var updateData = new
                    {
                        DeleteYn = "Y",
                        UpdatedAt = DateTime.Now
                    };

                    var response = await Http.PatchAsJsonAsync($"odata/wics/MapChannelTts(Id={mapping.Id})", updateData);

                    if (response.IsSuccessStatusCode)
                    {
                        LoggingService.AddLog("INFO", $"TTS ����: TtsID {ttsId} (delete_yn=Y)");
                    }
                }
            }

            LoggingService.AddLog("SUCCESS",
                $"TTS ���� ����ȭ �Ϸ� - �߰�: {toAdd.Count()}��, ����: {toDelete.Count()}��");
        }
        catch (Exception ex)
        {
            LoggingService.AddLog("ERROR", $"TTS ���� ����: {ex.Message}");
            _logger.LogError(ex, "Failed to save selected TTS to channel");
        }
    }
}
