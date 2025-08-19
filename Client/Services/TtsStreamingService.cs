using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Json;
using WicsPlatform.Client.Pages;
using WicsPlatform.Client.Pages.SubPages;
using WicsPlatform.Server.Models.wics;

namespace WicsPlatform.Client.Services
{
    public class TtsStreamingService : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly wicsService _wicsService;
        private readonly ILogger<TtsStreamingService> _logger;
        private readonly BroadcastLoggingService _loggingService;
        private readonly HttpClient _httpClient;

        private IJSObjectReference _ttsStreamerModule;
        private bool _isTtsStreaming = false;
        private List<Tt> _currentTtsList = new List<Tt>();
        private DotNetObjectReference<ManageBroadCast> _currentDotNetRef;

        // TTS 음성 파일 URL 캐시
        private Dictionary<ulong, string> _ttsAudioCache = new Dictionary<ulong, string>();
        private List<string> _preparedAudioUrls = new List<string>();

        public TtsStreamingService(
            IJSRuntime jsRuntime,
            wicsService wicsService,
            ILogger<TtsStreamingService> logger,
            BroadcastLoggingService loggingService,
            HttpClient httpClient)
        {
            _jsRuntime = jsRuntime;
            _wicsService = wicsService;
            _logger = logger;
            _loggingService = loggingService;
            _httpClient = httpClient;
        }

        /// <summary>
        /// TTS 음성 파일을 미리 생성하고 캐싱
        /// </summary>
        public async Task<bool> PreGenerateTtsAudioFiles(List<Tt> ttsList, Channel channel)
        {
            try
            {
                _preparedAudioUrls.Clear();
                _currentTtsList = ttsList;
                _loggingService.AddLog("INFO", "=== TTS 음성 파일 사전 생성 시작 ===");

                foreach (var tts in ttsList)
                {
                    // 캐시 확인
                    if (_ttsAudioCache.TryGetValue(tts.Id, out var cachedUrl))
                    {
                        _loggingService.AddLog("INFO", $"캐시된 TTS 사용: {tts.Name}");
                        _preparedAudioUrls.Add(cachedUrl);
                        continue;
                    }

                    // 새로 생성
                    _loggingService.AddLog("INFO", $"TTS 생성 중: {tts.Name}");

                    var request = new TtsGenerateRequest
                    {
                        Text = tts.Content,
                        Name = tts.Name
                    };

                    var response = await _httpClient.PostAsJsonAsync("/api/tts/generate", request);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<TtsGenerateResponse>();

                        if (result?.Success == true && !string.IsNullOrEmpty(result.AudioUrl))
                        {
                            _ttsAudioCache[tts.Id] = result.AudioUrl;
                            _preparedAudioUrls.Add(result.AudioUrl);
                            _loggingService.AddLog("SUCCESS", $"TTS 생성 완료: {tts.Name} → {result.AudioUrl}");
                        }
                        else
                        {
                            _loggingService.AddLog("ERROR", $"TTS 생성 실패: {tts.Name} - {result?.Error}");
                            return false;
                        }
                    }
                    else
                    {
                        _loggingService.AddLog("ERROR", $"TTS API 호출 실패: {response.StatusCode}");
                        return false;
                    }
                }

                _loggingService.AddLog("SUCCESS", $"총 {_preparedAudioUrls.Count}개 TTS 음성 파일 준비 완료");
                return _preparedAudioUrls.Count > 0;
            }
            catch (Exception ex)
            {
                _loggingService.AddLog("ERROR", $"TTS 사전 생성 오류: {ex.Message}");
                _logger.LogError(ex, "TTS 사전 생성 실패");
                return false;
            }
        }

        /// <summary>
        /// 미리 생성된 TTS 음성 파일로 스트리밍 시작
        /// </summary>
        public async Task<bool> StartTtsStreaming(
            bool isTtsEnabled,
            DotNetObjectReference<ManageBroadCast> dotNetRef,
            Channel selectedChannel,
            int preferredSampleRate,
            int preferredChannels,
            BroadcastTtsSection ttsSection)
        {
            if (!isTtsEnabled)
            {
                _loggingService.AddLog("WARN", "TTS가 비활성화되어 있습니다");
                return true;
            }

            try
            {
                _currentDotNetRef = dotNetRef;

                // ★ 이미 준비된 오디오 URL 사용
                if (!_preparedAudioUrls.Any())
                {
                    _loggingService.AddLog("WARN", "준비된 TTS 오디오가 없습니다");

                    // 준비된 것이 없으면 실시간으로 생성 시도 (폴백)
                    if (ttsSection != null && ttsSection.HasSelectedTts())
                    {
                        var selectedTts = ttsSection.GetSelectedTts();
                        var success = await PreGenerateTtsAudioFiles(selectedTts.ToList(), selectedChannel);
                        if (!success)
                        {
                            _loggingService.AddLog("ERROR", "TTS 실시간 생성도 실패");
                            return false;
                        }
                    }
                    else
                    {
                        return true; // TTS가 없는 것은 에러가 아님
                    }
                }

                _loggingService.AddLog("INFO", $"준비된 TTS 오디오 {_preparedAudioUrls.Count}개로 스트리밍 시작");

                // mediastreamer.js 모듈 로드
                if (_ttsStreamerModule == null)
                {
                    _ttsStreamerModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./js/mediastreamer.js");
                    _loggingService.AddLog("INFO", "미디어 스트리머 모듈 로드 완료");
                }

                // 미디어 스트리머 초기화
                var config = new
                {
                    sampleRate = preferredSampleRate,
                    channels = preferredChannels,
                    timeslice = 50,
                    bitrate = 64000,
                    localPlayback = false
                };

                _loggingService.AddLog("INFO", $"미디어 스트리머 초기화 - 샘플레이트: {preferredSampleRate}Hz, 채널: {preferredChannels}");

                var initSuccess = await _ttsStreamerModule.InvokeAsync<bool>(
                    "initializeMediaStreamer", dotNetRef, config);

                if (!initSuccess)
                {
                    _loggingService.AddLog("ERROR", "미디어 스트리머 초기화 실패");
                    return false;
                }

                _loggingService.AddLog("SUCCESS", "미디어 스트리머 초기화 완료");

                // URL 목록 로깅
                foreach (var url in _preparedAudioUrls)
                {
                    _loggingService.AddLog("DEBUG", $"스트리밍 URL: {url}");
                }

                // ★ 준비된 URL로 즉시 스트리밍 시작
                var streamSuccess = await _ttsStreamerModule.InvokeAsync<bool>(
                    "loadAndStreamTtsPlaylist", (object)_preparedAudioUrls.ToArray());

                if (!streamSuccess)
                {
                    _loggingService.AddLog("ERROR", "TTS 스트리밍 시작 실패");
                    return false;
                }

                _isTtsStreaming = true;
                _loggingService.AddLog("SUCCESS", $"TTS 스트리밍 시작됨 - {_preparedAudioUrls.Count}개 TTS 오디오 재생 중");

                return true;
            }
            catch (Exception ex)
            {
                _loggingService.AddLog("ERROR", $"TTS 스트리밍 오류: {ex.Message}");
                _logger.LogError(ex, "TTS 스트리밍 시작 중 오류");
                return false;
            }
        }

        /// <summary>
        /// TTS 스트리밍 중지
        /// </summary>
        public async Task StopTtsStreaming()
        {
            try
            {
                if (_ttsStreamerModule != null && _isTtsStreaming)
                {
                    await _ttsStreamerModule.InvokeVoidAsync("stopMediaStreaming");
                    _loggingService.AddLog("INFO", "TTS 스트리밍 중지");
                }

                _isTtsStreaming = false;
                _currentTtsList.Clear();
                // 준비된 URL은 유지 (캐시 효과)

                _logger.LogInformation("TTS 스트리밍이 중지되었습니다");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TTS 스트리밍 중지 중 오류");
            }
        }

        /// <summary>
        /// TTS 일시정지
        /// </summary>
        public async Task PauseTtsStreaming()
        {
            try
            {
                if (_ttsStreamerModule != null && _isTtsStreaming)
                {
                    await _ttsStreamerModule.InvokeVoidAsync("pauseMediaStreaming");
                    _loggingService.AddLog("INFO", "TTS 재생 일시정지");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TTS 일시정지 중 오류");
            }
        }

        /// <summary>
        /// TTS 재생 재개
        /// </summary>
        public async Task ResumeTtsStreaming()
        {
            try
            {
                if (_ttsStreamerModule != null && _isTtsStreaming)
                {
                    await _ttsStreamerModule.InvokeVoidAsync("resumeMediaStreaming");
                    _loggingService.AddLog("INFO", "TTS 재생 재개");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TTS 재생 재개 중 오류");
            }
        }

        /// <summary>
        /// TTS 재시작 (루프 재생)
        /// </summary>
        public async Task<bool> RestartTts(BroadcastTtsSection ttsSection, bool isTtsEnabled, bool isBroadcasting)
        {
            if (!isBroadcasting || !isTtsEnabled || _ttsStreamerModule == null)
            {
                _logger.LogInformation("TTS 재시작 불가 - 방송 중이 아니거나 TTS가 비활성화됨");
                return false;
            }

            try
            {
                _loggingService.AddLog("INFO", "TTS 플레이리스트 재시작 중...");

                // 준비된 URL이 있으면 재사용
                if (_preparedAudioUrls.Any())
                {
                    var success = await _ttsStreamerModule.InvokeAsync<bool>(
                        "loadAndStreamTtsPlaylist", (object)_preparedAudioUrls.ToArray());

                    if (success)
                    {
                        _loggingService.AddLog("SUCCESS", "TTS 플레이리스트 재시작 완료");
                        return true;
                    }
                }
                // 준비된 URL이 없으면 다시 생성
                else if (_currentTtsList.Any())
                {
                    var prepared = await PreGenerateTtsAudioFiles(_currentTtsList, null);
                    if (prepared && _preparedAudioUrls.Any())
                    {
                        var success = await _ttsStreamerModule.InvokeAsync<bool>(
                            "loadAndStreamTtsPlaylist", (object)_preparedAudioUrls.ToArray());

                        if (success)
                        {
                            _loggingService.AddLog("SUCCESS", "TTS 플레이리스트 재생성 및 재시작 완료");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TTS 재시작 중 오류");
                return false;
            }
        }

        /// <summary>
        /// 선택된 TTS ID 목록 가져오기
        /// </summary>
        public async Task<List<ulong>> GetSelectedTtsIds(Channel selectedChannel, BroadcastTtsSection ttsSection)
        {
            try
            {
                if (selectedChannel == null || ttsSection == null)
                    return new List<ulong>();

                var selectedTts = ttsSection.GetSelectedTts();
                return selectedTts.Select(t => t.Id).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "선택된 TTS ID 가져오기 실패");
                return new List<ulong>();
            }
        }

        /// <summary>
        /// 준비된 오디오 URL 목록 가져오기 (Public 메서드로 추가)
        /// </summary>
        public List<string> GetPreparedAudioUrls()
        {
            return new List<string>(_preparedAudioUrls);
        }

        /// <summary>
        /// 캐시 클리어 (메모리 관리)
        /// </summary>
        public void ClearTtsCache()
        {
            _ttsAudioCache.Clear();
            _preparedAudioUrls.Clear();
            _currentTtsList.Clear();
            _loggingService.AddLog("INFO", "TTS 캐시 클리어됨");
        }

        /// <summary>
        /// 특정 TTS의 캐시만 제거
        /// </summary>
        public void RemoveFromCache(ulong ttsId)
        {
            if (_ttsAudioCache.ContainsKey(ttsId))
            {
                _ttsAudioCache.Remove(ttsId);
                _loggingService.AddLog("INFO", $"TTS ID {ttsId} 캐시에서 제거됨");
            }
        }

        // 속성
        public bool IsTtsStreaming => _isTtsStreaming;
        public List<Tt> CurrentTtsList => _currentTtsList;
        public int CachedTtsCount => _ttsAudioCache.Count;
        public bool HasPreparedAudio => _preparedAudioUrls.Any();

        // Dispose
        public async ValueTask DisposeAsync()
        {
            if (_ttsStreamerModule != null)
            {
                try
                {
                    await StopTtsStreaming();
                    await _ttsStreamerModule.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TTS 스트리머 모듈 해제 중 오류");
                }
            }

            ClearTtsCache();
        }

        // DTO 클래스들
        private class TtsGenerateRequest
        {
            public string Text { get; set; }
            public string Name { get; set; }
        }

        private class TtsGenerateResponse
        {
            public bool Success { get; set; }
            public string AudioUrl { get; set; }
            public string Error { get; set; }
        }
    }
}