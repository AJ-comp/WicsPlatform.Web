using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastMicDataSection : IDisposable
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }

        // JS 모듈 관련 파라미터
        [Parameter] public IJSObjectReference JSModule { get; set; }

        // 이벤트 콜백
        [Parameter] public EventCallback<string> OnBroadcastStatusChanged { get; set; }
        [Parameter] public EventCallback<BroadcastStoppedEventArgs> OnBroadcastStopped { get; set; }

        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected IJSRuntime JSRuntime { get; set; }

        // 마이크데이터 관련 내부 필드
        private double audioLevel = 0.0;
        private DateTime broadcastStartTime = DateTime.Now;
        private string broadcastDuration = "00:00:00";
        private int totalDataPackets = 0;
        private double totalDataSize = 0.0; // KB 단위
        private double averageBitrate = 0.0;
        private int sampleRate = 44100;
        private bool isBroadcasting = true; // 섹션이 표시되면 방송 중

        // 타이머 및 기타
        private Random _random = new Random();

        // 로그 관련
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
            // 초기 로그 메시지
            AddLog("INFO", "마이크데이터 섹션이 초기화되었습니다.");
            AddLog("INFO", $"채널: {Channel?.Name ?? "Unknown"}");

            await Task.CompletedTask;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
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

            // StateHasChanged 호출을 최소화 - 중요한 데이터 변경시에만 호출
            if (packets % 20 == 0) // 20번의 패킷마다 한 번씩만 UI 업데이트
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        // 방송 일시정지
        public async Task PauseBroadcast()
        {
            try
            {
                if (JSModule != null)
                {
                    await JSModule.InvokeVoidAsync("pause");
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
                if (JSModule != null)
                {
                    await JSModule.InvokeVoidAsync("stop");
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
            if (data != null && data.Length > 0 && isBroadcasting)
            {
                // 실제 데이터 크기 로그 (로그 빈도 줄이기)
                if (totalDataPackets % 100 == 0) // 100번째 패킷마다만 로그
                {
                    AddLog("INFO", $"오디오 데이터 수신: {data.Length} bytes");
                }

                // 디버깅 패널이 열려있으면 상세 데이터 처리
                if (showDebugPanel)
                {
                    await ProcessDebugData(data);
                }

                // StateHasChanged 호출 최소화
                // 오디오 데이터 처리는 빈번하므로 UI 업데이트는 별도로 관리
            }
        }

        // 디버깅 데이터 초기화 - 제거됨

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

                // 오디오 레벨 분석
                AnalyzeAudioLevels(data);

                // 성능 지표 업데이트
                UpdatePerformanceMetrics();

                // 웨이브폼 그리기
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

                // 스트림 상태 업데이트
                streamStatus = "활성 (스트리밍 중)";
                bufferSize = data.Length;

                // 실제 처리 시간 계산
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

            // 16비트 오디오 샘플로 가정
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
            // 실제 처리 시간 측정
            var startTime = DateTime.Now;

            // 성능 데이터 추가
            performanceData.Add(new PerformanceDataPoint
            {
                Time = DateTime.Now.ToString("mm:ss"),
                Latency = (DateTime.Now - startTime).TotalMilliseconds
            });

            if (performanceData.Count > 20)
            {
                performanceData.RemoveAt(0);
            }

            // 평균 레이턴시 계산
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
                "DEBUG" => "var(--rz-text-secondary-color)",
                _ => "var(--rz-text-color)"
            };
        }

        // 로그 추가
        private void AddLog(string level, string message)
        {
            broadcastLogs.Add(new BroadcastLogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            });

            // 최대 100개 로그만 유지
            if (broadcastLogs.Count > 100)
            {
                broadcastLogs.RemoveAt(0);
            }

            // 로그 추가 시 StateHasChanged 호출 최소화
            // 오류나 경고 로그의 경우에만 즉시 UI 업데이트
            if (level == "ERROR" || level == "WARN")
            {
                InvokeAsync(StateHasChanged);
            }
        }

        // 주기적 로그 업데이트 - 제거됨

        public void Dispose()
        {
            _canvasModule?.DisposeAsync();
        }

        // 외부에서 로그를 추가할 수 있는 메서드
        public void AddExternalLog(string level, string message)
        {
            AddLog(level, message);
        }

        // 방송 상태 리셋 (외부에서 호출 가능)
        public void ResetBroadcastState()
        {
            isBroadcasting = false;
            audioLevel = 0.0;
            totalDataPackets = 0;
            totalDataSize = 0.0;
            averageBitrate = 0.0;
            broadcastDuration = "00:00:00";

            // 디버깅 데이터도 초기화
            streamStatus = "대기 중";
            peakLevel = 0.0;
            rmsLevel = 0.0;
            isClipping = false;
            droppedFrames = 0;
            bufferUnderruns = 0;
            recentAudioData.Clear();

            AddLog("INFO", "방송 상태가 초기화되었습니다");
            InvokeAsync(StateHasChanged);
        }

        // 오디오 설정 업데이트 메서드 추가
        public async Task UpdateAudioConfiguration(dynamic config)
        {
            sampleRate = config.SampleRate;
            audioChannels = config.ChannelCount;
            
            AddLog("INFO", $"오디오 설정 업데이트됨 - 샘플레이트: {sampleRate}Hz, 채널: {audioChannels}");
            await InvokeAsync(StateHasChanged);
        }
    }

    // 방송 종료 시 전달할 이벤트 인자
    public class BroadcastStoppedEventArgs
    {
        public string Duration { get; set; }
        public int TotalPackets { get; set; }
        public double TotalDataSize { get; set; }
        public double AverageBitrate { get; set; }
    }

    // 로그 엔트리 클래스
    public class BroadcastLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    // 디버깅용 클래스들
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
