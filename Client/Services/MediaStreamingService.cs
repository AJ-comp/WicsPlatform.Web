using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Radzen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WicsPlatform.Client.Pages;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Client.Services
{
    public class MediaStreamingService : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly NavigationManager _navigationManager;
        private readonly wicsService _wicsService;
        private readonly ILogger<MediaStreamingService> _logger;
        private readonly BroadcastLoggingService _loggingService;

        private IJSObjectReference _mediaStreamerModule;
        private bool _isMediaStreaming = false;
        private List<string> _currentMediaPlaylist = new List<string>();
        private DotNetObjectReference<ManageBroadCast> _currentDotNetRef;

        public MediaStreamingService(
            IJSRuntime jsRuntime,
            NavigationManager navigationManager,
            wicsService wicsService,
            ILogger<MediaStreamingService> logger,
            BroadcastLoggingService loggingService)
        {
            _jsRuntime = jsRuntime;
            _navigationManager = navigationManager;
            _wicsService = wicsService;
            _logger = logger;
            _loggingService = loggingService;
        }

        /// <summary>
        /// 미디어 스트리밍 시작
        /// </summary>
        public async Task<bool> StartMediaStreaming(
            bool isMediaEnabled,
            DotNetObjectReference<ManageBroadCast> dotNetRef,
            Channel selectedChannel,
            int preferredSampleRate,
            int preferredChannels)
        {
            if (!isMediaEnabled)
            {
                _loggingService.AddLog("WARN", "Media is disabled, skipping media streaming");
                return true;
            }

            try
            {
                _currentDotNetRef = dotNetRef;
                _loggingService.AddLog("INFO", "미디어 스트리밍 초기화 시작...");

                // JavaScript 모듈 로드
                if (_mediaStreamerModule == null)
                {
                    _mediaStreamerModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./js/mediastreamer.js");
                    _loggingService.AddLog("INFO", "미디어 스트리머 모듈 로드 완료");
                }

                // 미디어 스트리머 초기화
                var config = new
                {
                    sampleRate = preferredSampleRate,
                    channels = preferredChannels,
                    timeslice = 50,
                    localPlayback = false
                };

                var initSuccess = await _mediaStreamerModule.InvokeAsync<bool>(
                    "initializeMediaStreamer", dotNetRef, config);

                if (!initSuccess)
                {
                    _loggingService.AddLog("ERROR", "미디어 스트리머 초기화 실패");
                    return false;
                }

                _loggingService.AddLog("SUCCESS", $"미디어 스트리머 초기화 완료 (샘플레이트: {preferredSampleRate}Hz, 채널: {preferredChannels})");

                // 선택된 미디어 파일 URL 목록 가져오기
                var mediaUrls = await GetMediaPlaylistUrls(selectedChannel);

                if (mediaUrls == null || !mediaUrls.Any())
                {
                    _loggingService.AddLog("WARN", "선택된 미디어 파일이 없습니다. 미디어 스트리밍을 건너뜁니다.");
                    _logger.LogInformation("No media files selected for streaming");
                    return true; // 에러가 아닌 성공으로 처리 (미디어가 없을 뿐)
                }

                _loggingService.AddLog("INFO", $"미디어 플레이리스트 로드 중... (총 {mediaUrls.Count}개 파일)");

                // URL 배열을 object[]로 변환하여 전달
                var urlArray = mediaUrls.ToArray();

                // 미디어는 OnMediaAudioCaptured로 전송 (기존 그대로)
                var streamSuccess = await _mediaStreamerModule.InvokeAsync<bool>(
                    "loadAndStreamMediaPlaylist", (object)mediaUrls.ToArray());

                if (!streamSuccess)
                {
                    _loggingService.AddLog("ERROR", "미디어 플레이리스트 로드 실패");
                    return false;
                }

                _currentMediaPlaylist = mediaUrls.ToList();
                _isMediaStreaming = true;

                _loggingService.AddLog("SUCCESS", $"미디어 스트리밍 시작 - {mediaUrls.Count}개 파일 재생 중");
                _logger.LogInformation($"Media streaming started with {mediaUrls.Count} files");

                return true;
            }
            catch (Exception ex)
            {
                _loggingService.AddLog("ERROR", $"미디어 스트리밍 시작 오류: {ex.Message}");
                _logger.LogError(ex, "Error starting media streaming");
                return false;
            }
        }

        /// <summary>
        /// 선택된 미디어 파일들의 URL 목록 가져오기
        /// </summary>
        public async Task<List<string>> GetMediaPlaylistUrls(Channel selectedChannel)
        {
            try
            {
                _loggingService.AddLog("INFO", $"Getting media playlist URLs for channel: {selectedChannel?.Id}");

                var selectedMediaIds = await GetSelectedMediaIds(selectedChannel);
                if (!selectedMediaIds.Any())
                {
                    _loggingService.AddLog("WARN", "No media IDs found for channel");
                    return new List<string>();
                }

                var uniqueMediaIds = selectedMediaIds.Distinct().ToList();
                _loggingService.AddLog("INFO", $"Found {uniqueMediaIds.Count} media IDs: {string.Join(", ", uniqueMediaIds)}");

                // 한 번에 모든 미디어 조회 (개별 조회 대신)
                var idFilter = string.Join(" or ", uniqueMediaIds.Select(id => $"Id eq {id}"));
                var query = new Radzen.Query
                {
                    Filter = $"({idFilter}) and (DeleteYn eq 'N' or DeleteYn eq null)",
                    OrderBy = "CreatedAt asc"
                };

                _loggingService.AddLog("INFO", $"Media query filter: {query.Filter}");

                var result = await _wicsService.GetMedia(query);
                var mediaFiles = result.Value.ToList();

                _loggingService.AddLog("INFO", $"Retrieved {mediaFiles.Count} media files from database");

                // 각 미디어 파일 정보 로깅
                foreach (var media in mediaFiles)
                {
                    _loggingService.AddLog("INFO", $"Media: ID={media.Id}, File={media.FileName}, Path={media.FullPath}");
                }

                var urls = new List<string>();
                foreach (var media in mediaFiles)
                {
                    var url = GetMediaFileUrl(media);
                    if (!string.IsNullOrEmpty(url))
                    {
                        urls.Add(url);
                        _loggingService.AddLog("SUCCESS", $"Added URL: {url}");
                    }
                }

                if (urls.Count == 0)
                {
                    _loggingService.AddLog("ERROR", "No valid URLs generated from media files!");
                }
                else
                {
                    _loggingService.AddLog("SUCCESS", $"Generated {urls.Count} URLs for playlist");
                }

                return urls;
            }
            catch (Exception ex)
            {
                _loggingService.AddLog("ERROR", $"Failed to get media playlist URLs: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 현재 채널에 설정된 미디어 ID 가져오기
        /// </summary>
        public async Task<List<ulong>> GetSelectedMediaIds(Channel selectedChannel)
        {
            try
            {
                if (selectedChannel == null)
                {
                    _loggingService.AddLog("WARN", "Selected channel is null");
                    return new List<ulong>();
                }

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)",
                    Expand = "Medium"
                };

                _loggingService.AddLog("INFO", $"Querying MapChannelMedia with filter: {query.Filter}");

                var channelMedia = await _wicsService.GetMapChannelMedia(query);
                var mediaIds = channelMedia.Value.Select(m => m.MediaId).ToList();

                _loggingService.AddLog("INFO", $"Found {mediaIds.Count} media IDs in MapChannelMedia");

                if (mediaIds.Any())
                {
                    _loggingService.AddLog("INFO", $"Media IDs: {string.Join(", ", mediaIds)}");
                }

                return mediaIds;
            }
            catch (Exception ex)
            {
                _loggingService.AddLog("ERROR", $"Failed to get selected media IDs: {ex.Message}");
                return new List<ulong>();
            }
        }

        /// <summary>
        /// 현재 채널에 설정된 TTS ID 가져오기
        /// </summary>
        public async Task<List<ulong>> GetSelectedTtsIds(Channel selectedChannel)
        {
            try
            {
                if (selectedChannel == null) return new List<ulong>();

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)",
                    Expand = "Tt"
                };

                var channelTts = await _wicsService.GetMapChannelTts(query);
                return channelTts.Value.Select(t => t.TtsId).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get selected TTS IDs");
                return new List<ulong>();
            }
        }

        /// <summary>
        /// 미디어 파일의 실제 URL 생성
        /// </summary>
        private string GetMediaFileUrl(Medium mediaFile)
        {
            try
            {
                if (string.IsNullOrEmpty(mediaFile.FullPath))
                {
                    _loggingService.AddLog("WARN", $"Media file {mediaFile.Id} has empty FullPath");
                    return null;
                }

                string finalUrl = mediaFile.FullPath;

                // /media/로 시작하면 /Uploads/로 변경
                if (finalUrl.StartsWith("/media/"))
                {
                    var fileName = Path.GetFileName(finalUrl);
                    finalUrl = $"/Uploads/{fileName}";
                    _loggingService.AddLog("INFO", $"Converted /media/ to /Uploads/: {finalUrl}");
                }

                _loggingService.AddLog("DEBUG", $"URL for {mediaFile.FileName}: {finalUrl}");

                return finalUrl;
            }
            catch (Exception ex)
            {
                _loggingService.AddLog("ERROR", $"Error generating URL: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 미디어 스트리밍 중지
        /// </summary>
        public async Task StopMediaStreaming()
        {
            try
            {
                if (_mediaStreamerModule != null && _isMediaStreaming)
                {
                    await _mediaStreamerModule.InvokeVoidAsync("stopMediaStreaming");
                    _loggingService.AddLog("INFO", "미디어 스트리밍 중지");
                }

                _isMediaStreaming = false;

                var playlistCount = _currentMediaPlaylist.Count;
                _currentMediaPlaylist.Clear();

                _logger.LogInformation($"Media streaming stopped (had {playlistCount} files)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping media streaming");
            }
        }

        /// <summary>
        /// 미디어 일시정지
        /// </summary>
        public async Task PauseMediaStreaming()
        {
            try
            {
                if (_mediaStreamerModule != null && _isMediaStreaming)
                {
                    await _mediaStreamerModule.InvokeVoidAsync("pauseMediaStreaming");
                    _loggingService.AddLog("INFO", "미디어 재생 일시정지");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing media streaming");
            }
        }

        /// <summary>
        /// 미디어 재생 재개
        /// </summary>
        public async Task ResumeMediaStreaming()
        {
            try
            {
                if (_mediaStreamerModule != null && _isMediaStreaming)
                {
                    await _mediaStreamerModule.InvokeVoidAsync("resumeMediaStreaming");
                    _loggingService.AddLog("INFO", "미디어 재생 재개");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming media streaming");
            }
        }

        /// <summary>
        /// 플레이리스트 재시작 (루프 재생)
        /// </summary>
        public async Task<bool> RestartPlaylist(Channel selectedChannel, bool isMediaEnabled, bool isBroadcasting)
        {
            if (!isBroadcasting || !isMediaEnabled || _mediaStreamerModule == null)
            {
                _logger.LogInformation("Cannot restart playlist - broadcasting or media not enabled");
                return false;
            }

            try
            {
                _loggingService.AddLog("INFO", "플레이리스트 재시작 중...");

                // 현재 플레이리스트로 다시 시작
                if (_currentMediaPlaylist.Any())
                {
                    var success = await _mediaStreamerModule.InvokeAsync<bool>(
                        "loadAndStreamMediaPlaylist", _currentMediaPlaylist.ToArray());

                    if (success)
                    {
                        _loggingService.AddLog("SUCCESS", "플레이리스트 재시작 완료");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restarting playlist");
                return false;
            }
        }

        public bool IsMediaStreaming => _isMediaStreaming;
        public List<string> CurrentMediaPlaylist => _currentMediaPlaylist;

        public async ValueTask DisposeAsync()
        {
            if (_mediaStreamerModule != null)
            {
                try
                {
                    await StopMediaStreaming();
                    await _mediaStreamerModule.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing media streamer module");
                }
            }
        }
    }
}