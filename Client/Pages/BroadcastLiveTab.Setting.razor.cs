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



    private async Task SaveSelectedSpeakersToChannel()
    {
        try
        {
            if (selectedChannel == null) return;

            if (speakerSection == null)
            {
                _logger.LogInformation("스피커 섹션이 초기화되지 않았습니다.");
                LoggingService.AddLog("WARN", "스피커 섹션이 초기화되지 않았습니다.");
                return;
            }

            // 현재 선택된 그룹과 스피커 가져오기
            var selectedGroupIds = speakerSection.GetSelectedGroups()?.ToHashSet() ?? new HashSet<ulong>();
            var selectedSpeakerIds = speakerSection.GetSelectedSpeakers()?.ToHashSet() ?? new HashSet<ulong>();

            LoggingService.AddLog("INFO", $"스피커 매핑 동기화 시작");
            LoggingService.AddLog("INFO", $"현재 선택된 그룹: {string.Join(", ", selectedGroupIds)}");
            LoggingService.AddLog("INFO", $"현재 선택된 스피커: {string.Join(", ", selectedSpeakerIds)}");

            // ========== 1. map_channel_group 동기화 (스피커 그룹만, Type=0) ==========
            var groupQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {selectedChannel.Id}"
            };
            var existingGroupMappings = await WicsService.GetMapChannelGroups(groupQuery);
            
            // 기존 매핑에서 스피커 그룹만 필터링 (Group.Type == 0)
            var existingGroupIds = new HashSet<ulong>();
            foreach (var mapping in existingGroupMappings.Value.Where(m => m.DeleteYn != "Y"))
            {
                // Group 정보 조회
                var groupQuery2 = new Radzen.Query { Filter = $"Id eq {mapping.GroupId}" };
                var groups = await WicsService.GetGroups(groupQuery2);
                var group = groups.Value.FirstOrDefault();
                
                if (group != null && group.Type == 0) // Type 0 = 스피커 그룹
                {
                    existingGroupIds.Add(mapping.GroupId);
                }
            }

            // 추가할 그룹
            var groupsToAdd = selectedGroupIds.Except(existingGroupIds);
            foreach (var groupId in groupsToAdd)
            {
                var mapping = new WicsPlatform.Server.Models.wics.MapChannelGroup
                {
                    ChannelId = selectedChannel.Id,
                    GroupId = groupId,
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await WicsService.CreateMapChannelGroup(mapping);
                LoggingService.AddLog("SUCCESS", $"신규 스피커 그룹 추가: GroupID {groupId}");
            }

            // 제거할 그룹
            var groupsToDelete = existingGroupIds.Except(selectedGroupIds);
            foreach (var groupId in groupsToDelete)
            {
                var mapping = existingGroupMappings.Value.FirstOrDefault(m => m.GroupId == groupId && m.DeleteYn != "Y");
                if (mapping != null)
                {
                    // 전체 엔티티를 업데이트 (OData PATCH 요구사항)
                    mapping.DeleteYn = "Y";
                    mapping.UpdatedAt = DateTime.Now;

                    try
                    {
                        await WicsService.UpdateMapChannelGroup(mapping.Id, mapping);
                        LoggingService.AddLog("INFO", $"스피커 그룹 제거: GroupID {groupId} (delete_yn=Y)");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.AddLog("ERROR", $"스피커 그룹 제거 실패 (ID={mapping.Id}, GroupID={groupId}): {ex.Message}");
                    }
                }
            }

            // ========== 2. map_channel_speaker 동기화 (선택된 모든 스피커) ==========
            // 그룹 여부와 관계없이 최종 선택된 모든 스피커를 저장
            // 그룹은 단지 빠른 선택을 위한 UI 편의 기능일 뿐

            var speakerQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {selectedChannel.Id}"
            };
            var existingSpeakerMappings = await WicsService.GetMapChannelSpeakers(speakerQuery);
            var existingSpeakerIds = existingSpeakerMappings.Value
                .Where(m => m.DeleteYn != "Y")
                .Select(m => m.SpeakerId)
                .ToHashSet();

            LoggingService.AddLog("INFO", $"DB에 저장된 스피커: {string.Join(", ", existingSpeakerIds)}");

            // 추가할 스피커 (현재 선택된 스피커 중 DB에 없는 것)
            var speakersToAdd = selectedSpeakerIds.Except(existingSpeakerIds);
            LoggingService.AddLog("INFO", $"추가할 스피커: {string.Join(", ", speakersToAdd)}");
            foreach (var speakerId in speakersToAdd)
            {
                var mapping = new WicsPlatform.Server.Models.wics.MapChannelSpeaker
                {
                    ChannelId = selectedChannel.Id,
                    SpeakerId = speakerId,
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await WicsService.CreateMapChannelSpeaker(mapping);
                LoggingService.AddLog("SUCCESS", $"스피커 추가: SpeakerID {speakerId}");
            }

            // 제거할 스피커 (DB에 있지만 현재 선택되지 않은 것)
            var speakersToDelete = existingSpeakerIds.Except(selectedSpeakerIds);
            LoggingService.AddLog("INFO", $"제거할 스피커: {string.Join(", ", speakersToDelete)}");
            foreach (var speakerId in speakersToDelete)
            {
                var mapping = existingSpeakerMappings.Value.FirstOrDefault(m => m.SpeakerId == speakerId && m.DeleteYn != "Y");
                if (mapping != null)
                {
                    // 전체 엔티티를 업데이트 (OData PATCH 요구사항)
                    mapping.DeleteYn = "Y";
                    mapping.UpdatedAt = DateTime.Now;

                    try
                    {
                        await WicsService.UpdateMapChannelSpeaker(mapping.Id, mapping);
                        LoggingService.AddLog("INFO", $"스피커 제거: SpeakerID {speakerId} (delete_yn=Y)");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.AddLog("ERROR", $"스피커 제거 실패 (ID={mapping.Id}, SpeakerID={speakerId}): {ex.Message}");
                    }
                }
            }

            LoggingService.AddLog("SUCCESS",
                $"스피커 매핑 동기화 완료 - 그룹: 추가 {groupsToAdd.Count()}개, 제거 {groupsToDelete.Count()}개 | 스피커: 추가 {speakersToAdd.Count()}개, 제거 {speakersToDelete.Count()}개");
        }
        catch (Exception ex)
        {
            LoggingService.AddLog("ERROR", $"스피커 매핑 실패: {ex.Message}");
            LoggingService.AddLog("ERROR", $"스택 트레이스: {ex.StackTrace}");
            _logger.LogError(ex, "Failed to save selected speakers to channel");
            
            // 에러 발생해도 계속 진행 (다른 저장 작업은 수행)
        }
    }

    private async Task SaveSelectedPlaylistsToChannel()
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

            // 현재 선택된 플레이리스트 그룹 가져오기
            var selectedPlaylists = playlistSection.GetSelectedPlaylists();
            var selectedPlaylistIds = selectedPlaylists?.Select(p => p.Id).ToHashSet() ?? new HashSet<ulong>();

            LoggingService.AddLog("INFO", $"플레이리스트 매핑 동기화 시작 - 선택된 플레이리스트: {selectedPlaylistIds.Count}개");

            // map_channel_group에서 플레이리스트 그룹 조회 (Type = 1)
            var groupQuery = new Radzen.Query
            {
                Filter = $"ChannelId eq {selectedChannel.Id}"
            };
            var existingGroupMappings = await WicsService.GetMapChannelGroups(groupQuery);
            
            // 기존 매핑에서 플레이리스트 그룹만 필터링 (Group.Type == 1)
            var existingPlaylistIds = new HashSet<ulong>();
            foreach (var mapping in existingGroupMappings.Value.Where(m => m.DeleteYn != "Y"))
            {
                // Group 정보 조회
                var groupQuery2 = new Radzen.Query { Filter = $"Id eq {mapping.GroupId}" };
                var groups = await WicsService.GetGroups(groupQuery2);
                var group = groups.Value.FirstOrDefault();
                
                if (group != null && group.Type == 1) // Type 1 = 플레이리스트
                {
                    existingPlaylistIds.Add(mapping.GroupId);
                }
            }

            // 추가할 플레이리스트
            var playlistsToAdd = selectedPlaylistIds.Except(existingPlaylistIds);
            foreach (var playlistId in playlistsToAdd)
            {
                var mapping = new WicsPlatform.Server.Models.wics.MapChannelGroup
                {
                    ChannelId = selectedChannel.Id,
                    GroupId = playlistId,
                    DeleteYn = "N",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await WicsService.CreateMapChannelGroup(mapping);
                LoggingService.AddLog("SUCCESS", $"신규 플레이리스트 그룹 추가: PlaylistID {playlistId}");
            }

            // 제거할 플레이리스트
            var playlistsToDelete = existingPlaylistIds.Except(selectedPlaylistIds);
            foreach (var playlistId in playlistsToDelete)
            {
                var mapping = existingGroupMappings.Value.FirstOrDefault(m => m.GroupId == playlistId && m.DeleteYn != "Y");
                if (mapping != null)
                {
                    // 전체 엔티티를 업데이트 (OData PATCH 요구사항)
                    mapping.DeleteYn = "Y";
                    mapping.UpdatedAt = DateTime.Now;

                    try
                    {
                        await WicsService.UpdateMapChannelGroup(mapping.Id, mapping);
                        LoggingService.AddLog("INFO", $"플레이리스트 그룹 제거: PlaylistID {playlistId} (delete_yn=Y)");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.AddLog("ERROR", $"플레이리스트 그룹 제거 실패 (ID={mapping.Id}, PlaylistID={playlistId}): {ex.Message}");
                    }
                }
            }

            LoggingService.AddLog("SUCCESS",
                $"플레이리스트 매핑 동기화 완료 - 추가: {playlistsToAdd.Count()}개, 제거: {playlistsToDelete.Count()}개");
        }
        catch (Exception ex)
        {
            LoggingService.AddLog("ERROR", $"플레이리스트 매핑 실패: {ex.Message}");
            _logger.LogError(ex, "Failed to save selected playlists to channel");
        }
    }

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
                    // 전체 엔티티를 업데이트 (OData PATCH 요구사항)
                    mapping.DeleteYn = "Y";
                    mapping.UpdatedAt = DateTime.Now;

                    try
                    {
                        await WicsService.UpdateMapChannelMedium(mapping.Id, mapping);
                        LoggingService.AddLog("INFO", $"미디어 제거: MediaID {mediaId} (delete_yn=Y)");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.AddLog("ERROR", $"미디어 제거 실패 (ID={mapping.Id}, MediaID={mediaId}): {ex.Message}");
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
                    // 전체 엔티티를 업데이트 (OData PATCH 요구사항)
                    mapping.DeleteYn = "Y";
                    mapping.UpdatedAt = DateTime.Now;

                    try
                    {
                        await WicsService.UpdateMapChannelTt(mapping.Id, mapping);
                        LoggingService.AddLog("INFO", $"TTS 제거: TtsID {ttsId} (delete_yn=Y)");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.AddLog("ERROR", $"TTS 제거 실패 (ID={mapping.Id}, TtsID={ttsId}): {ex.Message}");
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
