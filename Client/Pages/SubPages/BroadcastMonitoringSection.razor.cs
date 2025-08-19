using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using WicsPlatform.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastMonitoringSection : IDisposable
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

        // 방송 소스 상태 파라미터
        [Parameter] public bool IsMicEnabled { get; set; }
        [Parameter] public bool IsMediaEnabled { get; set; }
        [Parameter] public bool IsTtsEnabled { get; set; }

        // JS 모듈 관련 파라미터
        [Parameter] public IJSObjectReference JSModule { get; set; }

        // 이벤트 콜백
        [Parameter] public EventCallback<string> OnBroadcastStatusChanged { get; set; }
        [Parameter] public EventCallback<BroadcastStoppedEventArgs> OnBroadcastStopped { get; set; }

        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected IJSRuntime JSRuntime { get; set; }
        [Inject] protected BroadcastLoggingService LoggingService { get; set; }

        // 방송 모니터링 관련 내부 필드
        private double audioLevel = 0.0;
        private DateTime broadcastStartTime = DateTime.Now;
        private string broadcastDuration = "00:00:00";
        private int totalDataPackets = 0;
        private double totalDataSize = 0.0;
        private double averageBitrate = 0.0;
        private int sampleRate = 44100;
        private bool isBroadcasting = true;

        // 로그 관련 - Services의 BroadcastLogEntry 사용
        private List<BroadcastLogEntry> broadcastLogs = new List<BroadcastLogEntry>();

        // 디버깅 패널 관련 필드
        private bool showDebugPanel = false;
        private string streamStatus = "대기 중";
        private int audioChannels = 2;
        private int bufferSize = 4096;
        private double peakLevel = 0.0;
        private double rmsLevel = 0.0;
        private bool isClipping = false;
        private double averageLatency = 0.0;
        private int droppedFrames = 0;
        private int bufferUnderruns = 0;
        private double processingTime = 0.0;

        // 오디오 데이터 디버깅
        private List<AudioDataDebugInfo> recentAudioData = new List<AudioDataDebugInfo>();
        private List<PerformanceDataPoint> performanceData = new List<PerformanceDataPoint>();
        private IJSObjectReference _canvasModule;

        // 프로퍼티들 (UI 바인딩용)
        public double AudioLevel => audioLevel;
        public DateTime BroadcastStartTime => broadcastStartTime;
        public string BroadcastDuration => broadcastDuration;
        public int TotalDataPackets => totalDataPackets;
        public double TotalDataSize => totalDataSize;
        public double AverageBitrate => averageBitrate;
        public int SampleRate => sampleRate;
        public bool IsBroadcasting => isBroadcasting;

        protected override async Task OnInitializedAsync()
        {
            // 기존 버퍼된 로그 로드
            var bufferedLogs = LoggingService.GetBufferedLogs();
            foreach (var log in bufferedLogs)
            {
                broadcastLogs.Add(log);
            }

            // 새 로그 구독
            LoggingService.OnLogAdded += OnLogAdded;
            LoggingService.OnLogsCleared += OnLogsCleared;

            await Task.CompletedTask;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && IsMicEnabled)
            {
                try
                {
                    _canvasModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/waveform.js");
                }
                catch (Exception ex)
                {
                    AddLog("ERROR", $"웨이브폼 모듈 로드 실패: {ex.Message}");
                }
            }
        }

        private void OnLogAdded(BroadcastLogEntry log)
        {
            broadcastLogs.Add(log);
            if (broadcastLogs.Count > 100)
            {
                broadcastLogs.RemoveAt(0);
            }
            InvokeAsync(StateHasChanged);
        }

        private void OnLogsCleared()
        {
            broadcastLogs.Clear();
            InvokeAsync(StateHasChanged);
        }

        private async Task TogglePanel()
        {
            await IsCollapsedChanged.InvokeAsync(!IsCollapsed);
        }

        // 외부에서 방송 데이터 업데이트
        public async Task UpdateBroadcastData(double audioLevelValue, DateTime startTime, string duration,
                                            int packets, double dataSize, double bitrate, int sampleRateValue)
        {
            audioLevel = audioLevelValue;
            broadcastStartTime = startTime;
            broadcastDuration = duration;
            totalDataPackets = packets;
            totalDataSize = dataSize;
            averageBitrate = bitrate;
            sampleRate = sampleRateValue;

            if (packets % 20 == 0)
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        // 방송 일시정지
        public async Task PauseBroadcast()
        {
            try
            {
                if (JSModule != null && IsMicEnabled)
                {
                    // audiomixer.js에 pause가 없다면 이 부분도 수정 필요
                    // 일단 주석 처리하거나 다른 방법 사용
                    // await JSModule.InvokeVoidAsync("pause");

                    // 대신 볼륨을 0으로 설정하여 일시정지 효과
                    await JSModule.InvokeVoidAsync("setVolumes", 0, 0, 0);
                }

                AddLog("WARN", "방송이 일시정지되었습니다");
                await OnBroadcastStatusChanged.InvokeAsync("paused");

                NotificationService.Notify(NotificationSeverity.Info, "방송 일시정지", "방송이 일시정지되었습니다.", 4000);
            }
            catch (Exception ex)
            {
                AddLog("ERROR", $"방송 일시정지 실패: {ex.Message}");
                NotificationService.Notify(NotificationSeverity.Error, "오류", $"방송 일시정지 중 오류: {ex.Message}", 4000);
            }
        }

        // 방송 종료
        public async Task StopBroadcast()
        {
            try
            {
                // stop이 아니라 dispose를 호출해야 함
                if (JSModule != null)
                {
                    await JSModule.InvokeVoidAsync("dispose");  // stop → dispose로 변경
                }

                var finalStats = new BroadcastStoppedEventArgs
                {
                    Duration = broadcastDuration,
                    TotalPackets = totalDataPackets,
                    TotalDataSize = totalDataSize,
                    AverageBitrate = averageBitrate
                };

                isBroadcasting = false;

                AddLog("INFO", "방송이 종료되었습니다");
                AddLog("INFO", $"총 방송시간: {broadcastDuration}");
                AddLog("INFO", $"총 전송 패킷: {totalDataPackets}개");

                await OnBroadcastStopped.InvokeAsync(finalStats);
                await OnBroadcastStatusChanged.InvokeAsync("stopped");

                NotificationService.Notify(NotificationSeverity.Info, "방송 종료", "방송이 종료되었습니다.", 4000);
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                AddLog("ERROR", $"방송 종료 실패: {ex.Message}");
                NotificationService.Notify(NotificationSeverity.Error, "오류", $"방송 종료 중 오류: {ex.Message}", 4000);
            }
        }

        // 오디오 캡처 데이터 처리
        public async Task OnAudioCaptured(byte[] data)
        {
            if (data != null && data.Length > 0 && isBroadcasting && IsMicEnabled)
            {
                if (showDebugPanel)
                {
                    await ProcessDebugData(data);
                }
            }
        }

        // 디버그 데이터 처리
        private async Task ProcessDebugData(byte[] data)
        {
            var startTime = DateTime.Now;

            try
            {
                var debugInfo = new AudioDataDebugInfo
                {
                    Timestamp = DateTime.Now,
                    Size = data.Length,
                    HexPreview = BitConverter.ToString(data.Take(32).ToArray()).Replace("-", " "),
                    BinaryPreview = Convert.ToString(data[0], 2).PadLeft(8, '0') + " " +
                                    Convert.ToString(data.Length > 1 ? data[1] : 0, 2).PadLeft(8, '0') + " ..."
                };

                recentAudioData.Add(debugInfo);
                if (recentAudioData.Count > 50) recentAudioData.RemoveAt(0);

                AnalyzeAudioLevels(data);
                UpdatePerformanceMetrics();

                if (_canvasModule != null && showDebugPanel)
                {
                    try
                    {
                        await _canvasModule.InvokeVoidAsync("drawWaveform", "waveformCanvas", data.Take(512).ToArray());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"웨이브폼 그리기 오류: {ex.Message}");
                    }
                }

                streamStatus = "활성 (스트리밍 중)";
                bufferSize = data.Length;
                processingTime = (DateTime.Now - startTime).TotalMilliseconds;
            }
            catch (Exception ex)
            {
                AddLog("ERROR", $"디버그 데이터 처리 오류: {ex.Message}");
            }
        }

        // 오디오 레벨 분석
        private void AnalyzeAudioLevels(byte[] data)
        {
            if (data.Length < 2) return;

            double sum = 0;
            double peak = 0;
            int sampleCount = data.Length / 2;

            for (int i = 0; i < data.Length - 1; i += 2)
            {
                short sample = (short)(data[i] | (data[i + 1] << 8));
                double normalized = sample / 32768.0;

                sum += normalized * normalized;
                peak = Math.Max(peak, Math.Abs(normalized));
            }

            rmsLevel = Math.Sqrt(sum / sampleCount);
            peakLevel = peak;
            isClipping = peak >= 0.99;

            if (isClipping)
            {
                AddLog("WARN", "오디오 클리핑 감지!");
            }
        }

        // 성능 지표 업데이트
        private void UpdatePerformanceMetrics()
        {
            var startTime = DateTime.Now;

            performanceData.Add(new PerformanceDataPoint
            {
                Time = DateTime.Now.ToString("mm:ss"),
                Latency = (DateTime.Now - startTime).TotalMilliseconds
            });

            if (performanceData.Count > 20)
            {
                performanceData.RemoveAt(0);
            }

            if (performanceData.Any())
            {
                averageLatency = performanceData.Average(p => p.Latency);
            }
        }

        // 로그 색상 결정
        private string GetLogColor(string level)
        {
            return level switch
            {
                "ERROR" => "var(--rz-danger)",
                "WARN" => "var(--rz-warning)",
                "INFO" => "var(--rz-info)",
                "SUCCESS" => "var(--rz-success)",
                "DEBUG" => "var(--rz-text-secondary-color)",
                _ => "var(--rz-text-color)"
            };
        }

        // 로그 추가 (LoggingService 사용)
        private void AddLog(string level, string message)
        {
            LoggingService.AddLog(level, message);
        }

        public void Dispose()
        {
            LoggingService.OnLogAdded -= OnLogAdded;
            LoggingService.OnLogsCleared -= OnLogsCleared;
            _canvasModule?.DisposeAsync();
        }

        // 방송 상태 리셋
        public void ResetBroadcastState()
        {
            isBroadcasting = false;
            audioLevel = 0.0;
            totalDataPackets = 0;
            totalDataSize = 0.0;
            averageBitrate = 0.0;
            broadcastDuration = "00:00:00";

            streamStatus = "대기 중";
            peakLevel = 0.0;
            rmsLevel = 0.0;
            isClipping = false;
            droppedFrames = 0;
            bufferUnderruns = 0;
            recentAudioData.Clear();

            InvokeAsync(StateHasChanged);
        }

        // 오디오 설정 업데이트 메서드
        public async Task UpdateAudioConfiguration(dynamic config)
        {
            sampleRate = config.SampleRate;
            audioChannels = config.ChannelCount;

            AddLog("INFO", $"오디오 설정 업데이트됨 - 샘플레이트: {sampleRate}Hz, 채널: {audioChannels}");
            await InvokeAsync(StateHasChanged);
        }
    }

    // 이벤트 인자 클래스들 (BroadcastLogEntry는 Services에서 사용)
    public class BroadcastStoppedEventArgs
    {
        public string Duration { get; set; }
        public int TotalPackets { get; set; }
        public double TotalDataSize { get; set; }
        public double AverageBitrate { get; set; }
    }

    public class AudioDataDebugInfo
    {
        public DateTime Timestamp { get; set; }
        public int Size { get; set; }
        public string HexPreview { get; set; }
        public string BinaryPreview { get; set; }
    }

    public class PerformanceDataPoint
    {
        public string Time { get; set; }
        public double Latency { get; set; }
    }
}