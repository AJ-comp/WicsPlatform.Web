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
                _logger.LogInformation("Media is disabled, skipping media streaming");
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
                    timeslice = 50  // 50ms 간격
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

                if (!mediaUrls.Any())
                {
                    _loggingService.AddLog("WARN", "선택된 미디어 파일이 없습니다. 미디어 스트리밍을 건너뜁니다.");
                    _logger.LogInformation("No media files selected for streaming");
                    return true;
                }

                _loggingService.AddLog("INFO", $"미디어 플레이리스트 로드 중... (총 {mediaUrls.Count}개 파일)");

                foreach (var url in mediaUrls)
                {
                    try
                    {
                        var fileName = System.IO.Path.GetFileName(new Uri(url).LocalPath);
                        _loggingService.AddLog("INFO", $"미디어 파일 준비: {fileName}");
                    }
                    catch
                    {
                        _loggingService.AddLog("INFO", $"미디어 파일 준비: {url}");
                    }
                }

                // JavaScript에서 미디어 파일 로드 및 스트리밍 시작
                var streamSuccess = await _mediaStreamerModule.InvokeAsync<bool>(
                    "loadAndStreamMediaPlaylist", mediaUrls.ToArray());

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
                _logger.LogInformation($"Getting media playlist URLs for channel: {selectedChannel?.Id}");

                var selectedMediaIds = await GetSelectedMediaIds(selectedChannel);
                if (!selectedMediaIds.Any())
                {
                    _logger.LogInformation("No media IDs found for channel");
                    return new List<string>();
                }

                var uniqueMediaIds = selectedMediaIds.Distinct().ToList();
                _logger.LogInformation($"Found {uniqueMediaIds.Count} unique media IDs");

                var query = new Radzen.Query
                {
                    Filter = $"Id in ({string.Join(",", uniqueMediaIds)}) and (DeleteYn eq 'N' or DeleteYn eq null)",
                    OrderBy = "CreatedAt asc"
                };

                var result = await _wicsService.GetMedia(query);
                var mediaFiles = result.Value.ToList();

                _logger.LogInformation($"Retrieved {mediaFiles.Count} media files from database");

                var urls = mediaFiles.Select(m => GetMediaFileUrl(m)).Where(url => !string.IsNullOrEmpty(url)).ToList();

                // 전체 URL 경로 로깅
                foreach (var url in urls)
                {
                    _logger.LogInformation($"Media URL: {url}");
                }

                return urls;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get media playlist URLs");
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
                    _logger.LogWarning("Selected channel is null");
                    return new List<ulong>();
                }

                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)",
                    Expand = "Medium"
                };

                _logger.LogInformation($"Querying MapChannelMedia with filter: {query.Filter}");

                var channelMedia = await _wicsService.GetMapChannelMedia(query);
                var mediaIds = channelMedia.Value.Select(m => m.MediaId).ToList();

                _logger.LogInformation($"Found {mediaIds.Count} media IDs for channel {selectedChannel.Id}");

                return mediaIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get selected media IDs");
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
                    _logger.LogWarning($"Media file {mediaFile.Id} has empty FullPath");
                    return null;
                }

                var baseUri = _navigationManager.BaseUri.TrimEnd('/');
                string finalUrl;

                // FullPath가 이미 전체 경로를 포함하는 경우
                if (mediaFile.FullPath.StartsWith("http://") || mediaFile.FullPath.StartsWith("https://"))
                {
                    finalUrl = mediaFile.FullPath;
                }
                // 절대 경로인 경우 (/로 시작)
                else if (mediaFile.FullPath.StartsWith("/"))
                {
                    // /Uploads/로 시작하면 그대로 사용
                    if (mediaFile.FullPath.StartsWith("/Uploads/"))
                    {
                        finalUrl = $"{baseUri}{mediaFile.FullPath}";
                    }
                    else
                    {
                        finalUrl = $"{baseUri}/Uploads{mediaFile.FullPath}";
                    }
                }
                // 상대 경로인 경우
                else
                {
                    // Uploads/로 시작하면 /를 추가
                    if (mediaFile.FullPath.StartsWith("Uploads/"))
                    {
                        finalUrl = $"{baseUri}/{mediaFile.FullPath}";
                    }
                    // 파일명만 있는 경우
                    else
                    {
                        finalUrl = $"{baseUri}/Uploads/{mediaFile.FullPath}";
                    }
                }

                _logger.LogInformation($"Generated URL for media {mediaFile.Id} ({mediaFile.FileName}): {finalUrl}");
                _logger.LogInformation($"  - Original FullPath: {mediaFile.FullPath}");
                _logger.LogInformation($"  - BaseUri: {baseUri}");

                return finalUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating URL for media file {mediaFile.Id}");
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