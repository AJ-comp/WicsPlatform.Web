using Radzen;
using System.Net.Http.Json;
using WicsPlatform.Client.Dialogs;

namespace WicsPlatform.Client.Pages;

public partial class ManageBroadCast
{
    protected async Task ToggleBroadcast(ulong channelId)
    {
        if (!isChannelBroadcasting.ContainsKey(channelId))
            isChannelBroadcasting[channelId] = false;

        isChannelBroadcasting[channelId] = !isChannelBroadcasting[channelId];

        var message = isChannelBroadcasting[channelId]
            ? $"채널 {channelId}의 방송이 시작되었습니다."
            : $"채널 {channelId}의 방송이 중지되었습니다.";

        var notifyAction = isChannelBroadcasting[channelId]
            ? (Action<string, string>)NotifySuccess
            : NotifyInfo;

        notifyAction(isChannelBroadcasting[channelId] ? "방송 시작" : "방송 중지", message);
        await InvokeAsync(StateHasChanged);
    }

    #region Audio Settings Dialog
    protected async Task OpenAudioSettingsDialog()
    {
        if (selectedChannel == null)
        {
            NotifyWarn("채널 선택", "먼저 채널을 선택하세요.");
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
            "오디오 설정",
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
            // 설정 적용
            _preferredSampleRate = audioSettings.SampleRate;
            _preferredChannels = audioSettings.Channels;

            // 채널에 저장
            if (selectedChannel != null)
            {
                await BroadcastDataService.UpdateChannelAudioSettingsAsync(
                    selectedChannel,
                    audioSettings.SampleRate,
                    audioSettings.Channels);
            }

            NotifySuccess("오디오 설정",
                $"설정이 변경되었습니다. (샘플레이트: {audioSettings.SampleRate}Hz, 채널: {audioSettings.Channels}ch)");
        }
    }
    #endregion

    #region Volume Control Dialog
    protected async Task OpenVolumeControlDialog()
    {
        if (selectedChannel == null)
        {
            NotifyWarn("채널 선택", "먼저 채널을 선택하세요.");
            return;
        }

        var parameters = new Dictionary<string, object>
        {
            { "Channel", selectedChannel },
            { "CurrentBroadcastId", currentBroadcastId?.ToString() },
            { "IsBroadcasting", isBroadcasting }
        };

        var result = await DialogService.OpenAsync<VolumeControlDialog>(
            "볼륨 제어",
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
            // 볼륨이 저장되었으면 현재 채널 정보를 다시 로드
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

            NotifyInfo("볼륨 설정", "볼륨 설정이 업데이트되었습니다.");
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
                _logger.LogInformation("플레이리스트 섹션이 초기화되지 않았습니다.");
                LoggingService.AddLog("WARN", "플레이리스트 섹션이 초기화되지 않았습니다.");
                return;
            }

            var selectedMedia = playlistSection.GetSelectedMedia();
            var selectedMediaIds = selectedMedia?.Select(m => m.Id).ToHashSet() ?? new HashSet<ulong>();

            LoggingService.AddLog("INFO", $"미디어 매핑 동기화 시작 - 선택된 미디어: {selectedMediaIds.Count}개");

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
                    LoggingService.AddLog("SUCCESS", $"신규 미디어 추가: {media.FileName} (ID: {mediaId})");
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
                        LoggingService.AddLog("INFO", $"미디어 제거: MediaID {mediaId} (delete_yn=Y)");
                    }
                    else
                    {
                        LoggingService.AddLog("ERROR", $"미디어 제거 실패: MediaID {mediaId}, Status: {response.StatusCode}");
                    }
                }
            }

            LoggingService.AddLog("SUCCESS",
                $"미디어 매핑 동기화 완료 - 추가: {toAdd.Count()}개, 제거: {toDelete.Count()}개");
        }
        catch (Exception ex)
        {
            LoggingService.AddLog("ERROR", $"미디어 매핑 실패: {ex.Message}");
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
                _logger.LogInformation("TTS 섹션이 초기화되지 않았습니다.");
                LoggingService.AddLog("WARN", "TTS 섹션이 초기화되지 않았습니다.");
                return;
            }

            var selectedTts = ttsSection.GetSelectedTts();
            var selectedTtsIds = selectedTts?.Select(t => t.Id).ToHashSet() ?? new HashSet<ulong>();

            LoggingService.AddLog("INFO", $"TTS 매핑 동기화 시작 - 선택된 TTS: {selectedTtsIds.Count}개");

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
                    LoggingService.AddLog("SUCCESS", $"신규 TTS 추가: {tts.Name} (ID: {ttsId})");
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
                        LoggingService.AddLog("INFO", $"TTS 제거: TtsID {ttsId} (delete_yn=Y)");
                    }
                }
            }

            LoggingService.AddLog("SUCCESS",
                $"TTS 매핑 동기화 완료 - 추가: {toAdd.Count()}개, 제거: {toDelete.Count()}개");
        }
        catch (Exception ex)
        {
            LoggingService.AddLog("ERROR", $"TTS 매핑 실패: {ex.Message}");
            _logger.LogError(ex, "Failed to save selected TTS to channel");
        }
    }
}
