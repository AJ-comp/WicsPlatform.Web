using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        private IJSObjectReference _ttsStreamerModule;
        private bool _isTtsStreaming = false;
        private List<Tt> _currentTtsList = new List<Tt>();
        private DotNetObjectReference<ManageBroadCast> _currentDotNetRef;

        public TtsStreamingService(
            IJSRuntime jsRuntime,
            wicsService wicsService,
            ILogger<TtsStreamingService> logger,
            BroadcastLoggingService loggingService)
        {
            _jsRuntime = jsRuntime;
            _wicsService = wicsService;
            _logger = logger;
            _loggingService = loggingService;
        }

        /// <summary>
        /// TTS 스트리밍 시작 (미디어 스트리밍과 동일한 패턴)
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
                _loggingService.AddLog("WARN", "TTS is disabled, skipping TTS streaming");
                return true;
            }

            try
            {
                _currentDotNetRef = dotNetRef;
                _loggingService.AddLog("INFO", "TTS 스트리밍 초기화 시작...");

                // JavaScript 모듈 로드
                if (_ttsStreamerModule == null)
                {
                    _ttsStreamerModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                        "import", "./js/ttsstreamer.js");
                    _loggingService.AddLog("INFO", "TTS 스트리머 모듈 로드 완료");
                }

                // TTS 스트리머 초기화
                var config = new
                {
                    sampleRate = preferredSampleRate,
                    channels = preferredChannels,
                    timeslice = 50,
                    bitrate = 64000,
                    localPlayback = false  // 로컬 재생 비활성화 (방송용)
                };

                var initSuccess = await _ttsStreamerModule.InvokeAsync<bool>(
                    "initializeTtsStreamer", dotNetRef, config);

                if (!initSuccess)
                {
                    _loggingService.AddLog("ERROR", "TTS 스트리머 초기화 실패");
                    return false;
                }

                _loggingService.AddLog("SUCCESS", $"TTS 스트리머 초기화 완료 (샘플레이트: {preferredSampleRate}Hz, 채널: {preferredChannels})");

                // ★ 미디어와 동일한 방식: TtsSection에서 선택된 TTS 가져오기
                if (ttsSection == null)
                {
                    _loggingService.AddLog("WARN", "TTS 섹션이 초기화되지 않았습니다.");
                    return true;
                }

                var selectedTtsList = ttsSection.GetSelectedTts();

                if (selectedTtsList == null || !selectedTtsList.Any())
                {
                    _loggingService.AddLog("WARN", "선택된 TTS가 없습니다. 테스트 TTS 사용");

                    // 테스트용 더미 TTS 생성
                    selectedTtsList = new List<Tt>
                    {
                        new Tt
                        {
                            Id = 0,
                            Name = "테스트 TTS",
                            Content = "안녕하세요. 이것은 테스트 TTS입니다. 정상적으로 작동하고 있습니다.",
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        }
                    };
                }

                _currentTtsList = selectedTtsList.ToList();

                _loggingService.AddLog("INFO", $"TTS {_currentTtsList.Count}개 로드 중...");

                // TTS 데이터 준비
                var ttsDataArray = _currentTtsList.Select(tts => new
                {
                    content = tts.Content,
                    lang = "ko-KR",
                    rate = 1.0,  // 속도 (0.1 ~ 10)
                    pitch = 1.0, // 음높이 (0 ~ 2)
                    voice = (string)null  // 특정 음성 선택 (null이면 기본)
                }).ToArray();

                // TTS 스트리밍 시작
                var streamSuccess = await _ttsStreamerModule.InvokeAsync<bool>(
                    "loadAndStreamTtsList", (object)ttsDataArray);

                if (!streamSuccess)
                {
                    _loggingService.AddLog("ERROR", "TTS 스트리밍 시작 실패");
                    return false;
                }

                _isTtsStreaming = true;

                _loggingService.AddLog("SUCCESS", $"TTS 스트리밍 시작 - {_currentTtsList.Count}개 TTS");
                _logger.LogInformation($"TTS streaming started with {_currentTtsList.Count} items");

                return true;
            }
            catch (Exception ex)
            {
                _loggingService.AddLog("ERROR", $"TTS 스트리밍 시작 오류: {ex.Message}");
                _logger.LogError(ex, "Error starting TTS streaming");
                return false;
            }
        }

        /// <summary>
        /// 선택된 TTS ID 목록 가져오기 (미디어와 동일한 패턴)
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
                _logger.LogError(ex, "Failed to get selected TTS IDs");
                return new List<ulong>();
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
                    await _ttsStreamerModule.InvokeVoidAsync("stopTtsStreaming");
                    _loggingService.AddLog("INFO", "TTS 스트리밍 중지");
                }

                _isTtsStreaming = false;
                _currentTtsList.Clear();

                _logger.LogInformation("TTS streaming stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping TTS streaming");
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
                    await _ttsStreamerModule.InvokeVoidAsync("pauseTtsStreaming");
                    _loggingService.AddLog("INFO", "TTS 재생 일시정지");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing TTS streaming");
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
                    await _ttsStreamerModule.InvokeVoidAsync("resumeTtsStreaming");
                    _loggingService.AddLog("INFO", "TTS 재생 재개");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming TTS streaming");
            }
        }

        /// <summary>
        /// TTS 재시작 (루프 재생)
        /// </summary>
        public async Task<bool> RestartTts(BroadcastTtsSection ttsSection, bool isTtsEnabled, bool isBroadcasting)
        {
            if (!isBroadcasting || !isTtsEnabled || _ttsStreamerModule == null)
            {
                _logger.LogInformation("Cannot restart TTS - broadcasting or TTS not enabled");
                return false;
            }

            try
            {
                _loggingService.AddLog("INFO", "TTS 재시작 중...");

                // 현재 TTS 리스트로 다시 시작
                if (_currentTtsList.Any())
                {
                    var ttsDataArray = _currentTtsList.Select(tts => new
                    {
                        content = tts.Content,
                        lang = "ko-KR",
                        rate = 1.0,
                        pitch = 1.0,
                        voice = (string)null
                    }).ToArray();

                    var success = await _ttsStreamerModule.InvokeAsync<bool>(
                        "loadAndStreamTtsList", (object)ttsDataArray);

                    if (success)
                    {
                        _loggingService.AddLog("SUCCESS", "TTS 재시작 완료");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restarting TTS");
                return false;
            }
        }

        public bool IsTtsStreaming => _isTtsStreaming;
        public List<Tt> CurrentTtsList => _currentTtsList;

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
                    _logger.LogError(ex, "Error disposing TTS streamer module");
                }
            }
        }
    }
}