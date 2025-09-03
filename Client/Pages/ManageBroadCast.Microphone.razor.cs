// Client/Pages/ManageBroadCast.Microphone.razor.cs

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WicsPlatform.Audio;
using WicsPlatform.Client.Dialogs;
using WicsPlatform.Shared;

namespace WicsPlatform.Client.Pages
{
    public partial class ManageBroadCast
    {
        #region Microphone Configuration

        private MicConfig _micConfig = new MicConfig();

        #endregion

        #region Public Microphone Status Methods

        /// <summary>
        /// 마이크가 현재 활성화되어 있는지 확인
        /// </summary>
        public async Task<bool> IsMicrophoneActive()
        {
            try
            {
                if (_mixerModule == null)
                {
                    _logger.LogDebug("Mixer module is null");
                    return false;
                }

                var isActive = await _mixerModule.InvokeAsync<bool>("isMicrophoneEnabled");
                _logger.LogDebug($"Microphone active status: {isActive}");
                return isActive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check microphone status");
                return false;
            }
        }

        #endregion

        #region Audio Mixer Initialization

        /// <summary>
        /// 오디오 믹서 초기화 및 마이크 설정
        /// </summary>
        private async Task<bool> InitializeAudioMixer()
        {
            try
            {
                // 마이크 상태 확인 함수 호출
                if (await IsMicrophoneActive()) return true;

                _dotNetRef = DotNetObjectReference.Create(this);

                // 믹서 모듈이 없을 때만 import
                if (_mixerModule == null)
                {
                    _mixerModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/audiomixer.js");
                }

                var configWithVolume = CreateMicrophoneConfig();

                var success = await _mixerModule.InvokeAsync<bool>("createMixer", _dotNetRef, configWithVolume);

                if (!success)
                {
                    NotifyError("오디오 믹서 초기화 실패", new Exception("오디오 믹서를 초기화할 수 없습니다."));
                    return false;
                }

                LogMicrophoneInitialization();

                // 루프백 설정이 있으면 스피커 모듈 초기화
                if (_currentLoopbackSetting && _speakerModule == null)
                {
                    await InitializeSpeakerModule();
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오디오 믹서 초기화 실패");
                NotifyError("오디오 믹서 초기화 실패", ex);
                return false;
            }
        }

        /// <summary>
        /// 마이크 설정 객체 생성
        /// </summary>
        private Dictionary<string, object> CreateMicrophoneConfig()
        {
            return new Dictionary<string, object>
            {
                { "sampleRate", _micConfig.SampleRate },
                { "channels", _micConfig.Channels },
                { "timeslice", _micConfig.TimesliceMs },
                { "bitrate", _micConfig.Bitrate },
                { "echoCancellation", _micConfig.EchoCancellation },
                { "noiseSuppression", _micConfig.NoiseSuppression },
                { "autoGainControl", _micConfig.AutoGainControl },
                { "localPlayback", _micConfig.LocalPlayback },
                { "samplesPerSend", _micConfig.GetSamplesPerTimeslice() },
                { "micVolume", micVolume / 100.0 },
                { "mediaVolume", mediaVolume / 100.0 },
                { "ttsVolume", ttsVolume / 100.0 }
            };
        }

        /// <summary>
        /// 스피커 모듈 초기화 (루프백용)
        /// </summary>
        private async Task InitializeSpeakerModule()
        {
            try
            {
                _speakerModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/speaker.js");
                await _speakerModule.InvokeVoidAsync("init");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "스피커 모듈 초기화 실패");
            }
        }

        /// <summary>
        /// 마이크 초기화 로그
        /// </summary>
        private void LogMicrophoneInitialization()
        {
            _logger.LogInformation($"오디오 믹서 초기화 완료 - Mic: {_micConfig.SampleRate}Hz/{_micConfig.Channels}ch, Timeslice: {_micConfig.TimesliceMs}ms");
            LoggingService.AddLog("SUCCESS", $"마이크 초기화 완료 ({_micConfig.SampleRate}Hz/모노)");
        }

        #endregion

        #region Microphone Activation

        /// <summary>
        /// 마이크 활성화
        /// </summary>
        private async Task<bool> EnableMicrophone()
        {
            try
            {
                if (_mixerModule == null)
                {
                    _logger.LogError("믹서 모듈이 초기화되지 않았습니다.");
                    return false;
                }

                // 마이크 상태 확인 함수 호출
                if (await IsMicrophoneActive())
                {
                    _logger.LogInformation("마이크가 이미 활성화되어 있습니다.");
                    LoggingService.AddLog("INFO", "마이크 이미 활성화됨");
                    return true;
                }

                var micEnabled = await _mixerModule.InvokeAsync<bool>("enableMic");

                if (!micEnabled)
                {
                    NotifyWarn("마이크 활성화 실패", "마이크 권한을 확인해주세요.");
                    return false;
                }

                LoggingService.AddLog("SUCCESS", "마이크 활성화 완료");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "마이크 활성화 실패");
                NotifyError("마이크 활성화", ex);
                return false;
            }
        }

        #endregion

        #region Audio Data Processing

        /// <summary>
        /// 믹서에서 캡처된 오디오 데이터 처리
        /// </summary>
        [JSInvokable]
        public async Task OnMixedAudioCaptured(string base64Data)
        {
            if (string.IsNullOrWhiteSpace(base64Data)) return;

            try
            {
                byte[] audioData = Convert.FromBase64String(base64Data);

                // 1. 통계 업데이트
                UpdateAudioStatistics(audioData);

                // 2. 녹음 데이터 처리
                ProcessRecordingData(audioData);

                // 3. 모니터링 섹션에 데이터 전달
                await SendToMonitoringSection(audioData);

                // 4. WebSocket으로 전송
                await SendToWebSocket(audioData);

                // 5. 루프백 처리
                await ProcessLoopback(base64Data);

                // 6. UI 업데이트 (100패킷마다)
                await UpdateUIIfNeeded();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"OnMixedAudioCaptured 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 오디오 통계 업데이트
        /// </summary>
        private void UpdateAudioStatistics(byte[] data)
        {
            totalDataPackets++;
            totalDataSize += data.Length / 1024.0;
            audioLevel = CalculateAudioLevel(data);
        }

        /// <summary>
        /// 오디오 레벨 계산
        /// </summary>
        private double CalculateAudioLevel(byte[] audioData)
        {
            if (audioData.Length < 2) return 0;

            double sum = 0;
            int sampleCount = audioData.Length / 2;

            for (int i = 0; i < audioData.Length - 1; i += 2)
            {
                short sample = (short)(audioData[i] | (audioData[i + 1] << 8));
                double normalized = sample / 32768.0;
                sum += normalized * normalized;
            }

            double rms = Math.Sqrt(sum / sampleCount);
            return Math.Min(100, rms * 100);
        }

        /// <summary>
        /// 모니터링 섹션에 오디오 데이터 전송
        /// </summary>
        private async Task SendToMonitoringSection(byte[] audioData)
        {
            if (monitoringSection != null)
            {
                await monitoringSection.OnAudioCaptured(audioData);
            }
        }

        /// <summary>
        /// WebSocket으로 오디오 데이터 전송
        /// </summary>
        private async Task SendToWebSocket(byte[] audioData)
        {
            if (!string.IsNullOrEmpty(currentBroadcastId))
            {
                await WebSocketService.SendAudioDataAsync(currentBroadcastId, audioData);
            }
        }

        /// <summary>
        /// 루프백 처리
        /// </summary>
        private async Task ProcessLoopback(string base64Data)
        {
            if (_currentLoopbackSetting && _speakerModule != null)
            {
                await _speakerModule.InvokeVoidAsync("feed", base64Data);
            }
        }

        /// <summary>
        /// UI 업데이트 (100패킷마다)
        /// </summary>
        private async Task UpdateUIIfNeeded()
        {
            if (totalDataPackets % 100 == 0)
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        #endregion

        #region Microphone Help

        /// <summary>
        /// 마이크 도움말 표시
        /// </summary>
        [JSInvokable]
        public Task ShowMicHelp()
        {
            DialogService.Open<MicHelpDialog>("마이크 권한 해제 방법",
                new Dictionary<string, object>(),
                new DialogOptions { Width = "600px", Resizable = true });
            return Task.CompletedTask;
        }

        #endregion

        #region Microphone Cleanup

        /// <summary>
        /// 마이크 및 오디오 믹서 정리
        /// </summary>
        private async Task CleanupMicrophone()
        {
            try
            {
                if (_mixerModule != null)
                {
                    try
                    {
                        await _mixerModule.InvokeVoidAsync("dispose");
                        await _mixerModule.DisposeAsync();
                        _logger.LogInformation("믹서 모듈 정리 완료");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "믹서 모듈 정리 실패");
                    }
                    finally
                    {
                        _mixerModule = null;
                        _jsModule = null;
                    }
                }

                if (_speakerModule != null)
                {
                    try
                    {
                        await _speakerModule.DisposeAsync();
                        _logger.LogInformation("스피커 모듈 정리 완료");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "스피커 모듈 정리 실패");
                    }
                    finally
                    {
                        _speakerModule = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "마이크 정리 중 오류 발생");
            }
        }

        #endregion

        #region Microphone Volume Control

        /// <summary>
        /// 실시간 마이크 볼륨 조절
        /// </summary>
        public async Task SetMicrophoneVolume(float volume)
        {
            if (_mixerModule != null)
            {
                await _mixerModule.InvokeVoidAsync("setVolumes",
                    volume,
                    mediaVolume / 100.0,
                    ttsVolume / 100.0);
            }
        }

        /// <summary>
        /// 전체 볼륨 설정 업데이트
        /// </summary>
        public async Task UpdateAllVolumes()
        {
            if (_mixerModule != null)
            {
                await _mixerModule.InvokeVoidAsync("setVolumes",
                    micVolume / 100.0,
                    mediaVolume / 100.0,
                    ttsVolume / 100.0);
            }
        }

        #endregion
    }
}