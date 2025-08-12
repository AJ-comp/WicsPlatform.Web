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
                // 선택된 미디어 파일 URL 목록 가져오기
                var mediaUrls = await GetMediaPlaylistUrls(selectedChannel);

                // ★ 미디어 로딩 로그 추가 (public 메서드 사용)
                if (dotNetRef?.Value is ManageBroadCast manageBroadCast)
                {
                    if (!mediaUrls.Any())
                    {
                        manageBroadCast.AddBroadcastLog("WARN", "선택된 미디어 파일이 없습니다. 미디어 스트리밍을 건너뜁니다.");
                        _logger.LogInformation("No media files selected for streaming");
                        return true; // 미디어가 없는 경우 성공으로 처리
                    }
                    else
                    {
                        manageBroadCast.AddBroadcastLog("INFO", $"미디어 플레이리스트 로드 완료 (총 {mediaUrls.Count}개 파일)");
                        foreach (var url in mediaUrls)
                        {
                            var fileName = System.IO.Path.GetFileName(new Uri(url).LocalPath);
                            manageBroadCast.AddBroadcastLog("INFO", $"미디어 파일 준비: {fileName}");
                        }

                        // ★ 일단 여기까지만 - JavaScript 호출 없이 미디어 파일 정보만 저장
                        _currentMediaPlaylist = mediaUrls.ToList();
                        _isMediaStreaming = false; // 실제 스트리밍은 하지 않음

                        manageBroadCast.AddBroadcastLog("SUCCESS", $"미디어 파일 {mediaUrls.Count}개 준비 완료");
                    }
                }

                _logger.LogInformation($"Media files prepared: {mediaUrls.Count} files");
                return true; // 성공으로 처리
            }
            catch (Exception ex)
            {
                // ★ 예외 발생 로그 추가
                if (dotNetRef?.Value is ManageBroadCast mbEx)
                {
                    mbEx.AddBroadcastLog("ERROR", $"미디어 준비 오류: {ex.Message}");
                }

                _logger.LogError(ex, "Error preparing media files");
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

                // ★ 중복 제거
                var uniqueMediaIds = selectedMediaIds.Distinct().ToList();

                var query = new Radzen.Query
                {
                    Filter = $"Id in ({string.Join(",", uniqueMediaIds)}) and DeleteYn eq 'N'",
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

                // MapChannelMedium에서 현재 채널에 연결된 미디어 가져오기 (delete_yn 조건 수정)
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and DeleteYn eq 'N'",
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

                // MapChannelTt에서 현재 채널에 연결된 TTS 가져오기 (delete_yn 조건 수정)
                var query = new Radzen.Query
                {
                    Filter = $"ChannelId eq {selectedChannel.Id} and DeleteYn eq 'N'",
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
            try
            {
                _isMediaStreaming = false;

                var playlistCount = _currentMediaPlaylist.Count;
                _currentMediaPlaylist.Clear();

                _logger.LogInformation($"Media streaming stopped (had {playlistCount} files prepared)");

                await Task.CompletedTask; // JavaScript 호출 없이 종료
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping media streaming");
            }
        }

        /// <summary>
        /// 플레이리스트 재시작 (루프 재생용)
        /// </summary>
        public async Task<bool> RestartPlaylist(Channel selectedChannel, bool isMediaEnabled, bool isBroadcasting)
        {
            // ★ JavaScript 호출 없이 단순히 false 반환
            _logger.LogInformation("Media playlist restart requested but JavaScript streaming is disabled");
            return await Task.FromResult(false);
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