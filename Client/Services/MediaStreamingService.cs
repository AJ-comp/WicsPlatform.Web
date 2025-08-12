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

        private IJSObjectReference _mediaStreamerModule;
        private bool _isMediaStreaming = false;
        private List<string> _currentMediaPlaylist = new List<string>();

        public MediaStreamingService(
            IJSRuntime jsRuntime,
            NavigationManager navigationManager,
            wicsService wicsService,
            ILogger<MediaStreamingService> logger)
        {
            _jsRuntime = jsRuntime;
            _navigationManager = navigationManager;
            _wicsService = wicsService;
            _logger = logger;
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
                return true; // 미디어가 비활성화된 경우 성공으로 처리
            }

            try
            {
                // 미디어 스트리머 모듈 로드
                if (_mediaStreamerModule == null)
                {
                    _mediaStreamerModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/mediastreamer.js");
                }

                // 오디오 설정 구성
                var channelSampleRate = selectedChannel?.SamplingRate > 0 ? (int)selectedChannel.SamplingRate : preferredSampleRate;
                var channelChannels = selectedChannel?.Channel1 == "mono" ? 1 : preferredChannels;

                var mediaConfig = new
                {
                    sampleRate = channelSampleRate,
                    channels = channelChannels,
                    timeslice = 50 // 50ms 간격
                };

                // 미디어 스트리머 초기화
                var initialized = await _mediaStreamerModule.InvokeAsync<bool>("initializeMediaStreamer", dotNetRef, mediaConfig);
                if (!initialized)
                {
                    _logger.LogError("Failed to initialize media streamer");
                    return false;
                }

                // 선택된 미디어 파일 URL 목록 가져오기
                var mediaUrls = await GetMediaPlaylistUrls(selectedChannel);
                if (!mediaUrls.Any())
                {
                    _logger.LogInformation("No media files selected for streaming");
                    return true; // 미디어가 없는 경우 성공으로 처리
                }

                // 플레이리스트 로드 및 스트리밍 시작
                var started = await _mediaStreamerModule.InvokeAsync<bool>("loadAndStreamMediaPlaylist", mediaUrls.ToArray());
                if (started)
                {
                    _isMediaStreaming = true;
                    _currentMediaPlaylist = mediaUrls.ToList();
                    _logger.LogInformation($"Media streaming started with {mediaUrls.Count} files");
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to start media streaming");
                    return false;
                }
            }
            catch (Exception ex)
            {
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
                var selectedMediaIds = await GetSelectedMediaIds(selectedChannel);
                if (!selectedMediaIds.Any())
                {
                    return new List<string>();
                }

                var query = new Radzen.Query
                {
                    Filter = $"Id in ({string.Join(",", selectedMediaIds)}) and (DeleteYn eq 'N' or DeleteYn eq null)",
                    OrderBy = "CreatedAt asc"
                };

                var result = await _wicsService.GetMedia(query);
                var mediaFiles = result.Value.ToList();

                return mediaFiles.Select(m => GetMediaFileUrl(m)).Where(url => !string.IsNullOrEmpty(url)).ToList();
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
                if (selectedChannel == null) return new List<ulong>();

                // MapChannelMedium에서 현재 채널에 연결된 미디어 가져오기
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and (DeleteYn eq 'N' or DeleteYn eq null)",
                    Expand = "Medium"
                };

                var channelMedia = await _wicsService.GetMapChannelMedia(query);
                return channelMedia.Value.Select(m => m.MediaId).ToList();
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

                // MapChannelTt에서 현재 채널에 연결된 TTS 가져오기
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
                    return null;
                }

                // 상대 경로를 절대 URL로 변환
                var baseUri = _navigationManager.BaseUri.TrimEnd('/');

                if (mediaFile.FullPath.StartsWith("/"))
                {
                    return $"{baseUri}{mediaFile.FullPath}";
                }
                else if (!mediaFile.FullPath.StartsWith("http"))
                {
                    return $"{baseUri}/Uploads/{mediaFile.FullPath}";
                }
                else
                {
                    return mediaFile.FullPath;
                }
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
            if (_mediaStreamerModule != null && _isMediaStreaming)
            {
                try
                {
                    await _mediaStreamerModule.InvokeVoidAsync("stopMediaStreaming");
                    _isMediaStreaming = false;
                    _currentMediaPlaylist.Clear();
                    _logger.LogInformation("Media streaming stopped");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping media streaming");
                }
            }
        }

        /// <summary>
        /// 플레이리스트 재시작 (루프 재생용)
        /// </summary>
        public async Task<bool> RestartPlaylist(Channel selectedChannel, bool isMediaEnabled, bool isBroadcasting)
        {
            if (isMediaEnabled && isBroadcasting)
            {
                var mediaUrls = await GetMediaPlaylistUrls(selectedChannel);
                if (mediaUrls.Any())
                {
                    await Task.Delay(1000); // 1초 대기 후 재시작
                    var restarted = await _mediaStreamerModule.InvokeAsync<bool>("loadAndStreamMediaPlaylist", mediaUrls.ToArray());
                    if (restarted)
                    {
                        _isMediaStreaming = true;
                        _logger.LogInformation("Media playlist restarted for loop playback");
                        return true;
                    }
                    else
                    {
                        _logger.LogError("Failed to restart media playlist");
                        return false;
                    }
                }
            }
            return false;
        }

        public bool IsMediaStreaming => _isMediaStreaming;
        public List<string> CurrentMediaPlaylist => _currentMediaPlaylist;

        public async ValueTask DisposeAsync()
        {
            if (_mediaStreamerModule != null)
            {
                await _mediaStreamerModule.DisposeAsync();
            }
        }
    }
}