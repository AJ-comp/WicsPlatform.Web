// Client/Services/TtsStreamingService.cs
// 전체 파일 교체

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
        /// TTS 스트리밍 시작 - TTS를 WAV로 변환 후 미디어 스트리머 사용
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
                _loggingService.AddLog("INFO", "TTS 스트리밍 초기화 시작...");

                // 1. TTS 섹션에서 선택된 TTS 가져오기
                if (ttsSection == null)
                {
                    _loggingService.AddLog("WARN", "TTS 섹션이 초기화되지 않았습니다");
                    return true;
                }

                var selectedTtsList = ttsSection.GetSelectedTts();
                if (selectedTtsList == null || !selectedTtsList.Any())
                {
                    _loggingService.AddLog("WARN", "선택된 TTS가 없습니다");
                    return true;
                }

                _currentTtsList = selectedTtsList.ToList();
                _loggingService.AddLog("INFO", $"선택된 TTS: {_currentTtsList.Count}개");

                // 2. 각 TTS를 WAV 파일로 변환
                var audioUrls = new List<string>();

                foreach (var tts in _currentTtsList)
                {
                    try
                    {
                        _loggingService.AddLog("INFO", $"TTS 변환 중: {tts.Name}");

                        // 서버 API 호출
                        var request = new TtsGenerateRequest
                        {
                            Text = tts.Content,
                            Name = tts.Name
                        };

                        var response = await _httpClient.PostAsJsonAsync("/api/tts/generate", request);

                        if (response.IsSuccessStatusCode)
                        {
                            var result = await response.Content.ReadFromJsonAsync<TtsGenerateResponse>();

                            if (result != null && result.Success && !string.IsNullOrEmpty(result.AudioUrl))
                            {
                                audioUrls.Add(result.AudioUrl);
                                _loggingService.AddLog("SUCCESS", $"TTS 변환 완료: {tts.Name} → {result.AudioUrl}");
                            }
                            else
                            {
                                _loggingService.AddLog("ERROR", $"TTS 변환 실패: {tts.Name} - {result?.Error}");
                            }
                        }
                        else
                        {
                            _loggingService.AddLog("ERROR", $"TTS API 호출 실패: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.AddLog("ERROR", $"TTS 변환 오류: {tts.Name} - {ex.Message}");
                        _logger.LogError(ex, $"TTS 변환 실패: {tts.Name}");
                    }
                }

                if (!audioUrls.Any())
                {
                    _loggingService.AddLog("ERROR", "변환된 TTS 오디오가 없습니다");
                    return false;
                }

                _loggingService.AddLog("SUCCESS", $"TTS 변환 완료: 총 {audioUrls.Count}개 오디오 파일");

                // 3. mediastreamer.js 모듈 로드 (미디어 스트리머 재사용)
                if (_ttsStreamerModule == null)
                {
                    _ttsStreamerModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./js/mediastreamer.js");  // 미디어 스트리머 재사용!
                    _loggingService.AddLog("INFO", "미디어 스트리머 모듈 로드 완료");
                }

                // 4. 미디어 스트리머 초기화
                var config = new
                {
                    sampleRate = preferredSampleRate,
                    channels = preferredChannels,
                    timeslice = 50,
                    bitrate = 64000,
                    localPlayback = false  // 로컬 재생 비활성화 (방송용)
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

                // 5. 변환된 TTS 오디오 URL로 스트리밍 시작
                _loggingService.AddLog("INFO", "TTS 오디오 스트리밍 시작...");

                // URL 목록 로깅
                foreach (var url in audioUrls)
                {
                    _loggingService.AddLog("DEBUG", $"스트리밍 URL: {url}");
                }

                // TTS는 OnTtsAudioCaptured로 전송
                var streamSuccess = await _ttsStreamerModule.InvokeAsync<bool>(
                    "loadAndStreamTtsPlaylist", (object)audioUrls.ToArray());

                if (!streamSuccess)
                {
                    _loggingService.AddLog("ERROR", "TTS 스트리밍 시작 실패");
                    return false;
                }

                _isTtsStreaming = true;
                _loggingService.AddLog("SUCCESS", $"TTS 스트리밍 시작됨 - {audioUrls.Count}개 TTS 오디오 재생 중");

                return true;
            }
            catch (Exception ex)
            {
                _loggingService.AddLog("ERROR", $"TTS 스트리밍 오류: {ex.Message}");
                _logger.LogError(ex, "TTS 스트리밍 시작 중 오류 발생");
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

                // 기존 TTS 리스트가 있으면 재변환
                if (_currentTtsList.Any())
                {
                    var audioUrls = new List<string>();

                    foreach (var tts in _currentTtsList)
                    {
                        try
                        {
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
                                    audioUrls.Add(result.AudioUrl);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"TTS 재변환 실패: {tts.Name}");
                        }
                    }

                    if (audioUrls.Any())
                    {
                        var success = await _ttsStreamerModule.InvokeAsync<bool>(
                            "loadAndStreamMediaPlaylist", (object)audioUrls.ToArray());

                        if (success)
                        {
                            _loggingService.AddLog("SUCCESS", "TTS 플레이리스트 재시작 완료");
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

        // 속성
        public bool IsTtsStreaming => _isTtsStreaming;
        public List<Tt> CurrentTtsList => _currentTtsList;

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