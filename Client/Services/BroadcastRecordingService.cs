using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using Radzen;

namespace WicsPlatform.Client.Services
{
    public class BroadcastRecordingService : IDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly NotificationService _notificationService;
        private readonly ILogger<BroadcastRecordingService> _logger;

        // 녹음 상태
        private bool _isRecording = false;
        private List<byte[]> _recordedChunks = new List<byte[]>();
        private DateTime _recordingStartTime;
        private string _recordingDuration = "00:00:00";
        private double _recordingDataSize = 0.0;
        private Timer _recordingTimer;

        // 녹음 상태 변경 이벤트
        public event Func<Task> OnRecordingStateChanged;

        public BroadcastRecordingService(
            IJSRuntime jsRuntime,
            NotificationService notificationService,
            ILogger<BroadcastRecordingService> logger)
        {
            _jsRuntime = jsRuntime;
            _notificationService = notificationService;
            _logger = logger;
        }

        #region Public Properties
        public bool IsRecording => _isRecording;
        public string RecordingDuration => _recordingDuration;
        public double RecordingDataSize => _recordingDataSize;
        public List<byte[]> RecordedChunks => _recordedChunks;
        #endregion

        #region Recording Control Methods
        /// <summary>
        /// 녹음 시작
        /// </summary>
        public async Task<bool> StartRecording(bool isBroadcasting)
        {
            if (!isBroadcasting)
            {
                NotifyWarn("방송 필요", "방송이 시작된 상태에서만 녹음할 수 있습니다.");
                return false;
            }

            try
            {
                InitializeRecordingState();
                NotifySuccess("녹음 시작", "방송 내용 녹음을 시작합니다.");

                // 상태 변경 이벤트 발생
                await RaiseRecordingStateChanged();

                _logger.LogInformation("Recording started");
                return true;
            }
            catch (Exception ex)
            {
                _isRecording = false;
                NotifyError("녹음 시작", ex);
                _logger.LogError(ex, "Failed to start recording");
                return false;
            }
        }

        /// <summary>
        /// 녹음 중지
        /// </summary>
        public async Task<bool> StopRecording()
        {
            if (!_isRecording) return false;

            try
            {
                StopRecordingTimer();

                if (_recordedChunks.Any())
                {
                    var combinedData = CombineRecordedChunks();
                    await SaveRecordingToFile(combinedData);
                }
                else
                {
                    NotifyWarn("녹음 없음", "저장할 녹음 데이터가 없습니다.");
                }

                _recordedChunks.Clear();

                // 상태 변경 이벤트 발생
                await RaiseRecordingStateChanged();

                _logger.LogInformation("Recording stopped");
                return true;
            }
            catch (Exception ex)
            {
                NotifyError("녹음 저장", ex);
                _logger.LogError(ex, "Failed to stop recording");
                return false;
            }
        }

        /// <summary>
        /// 오디오 데이터를 녹음 버퍼에 추가
        /// </summary>
        public void AddAudioData(byte[] data)
        {
            if (_isRecording && data != null && data.Length > 0)
            {
                _recordedChunks.Add(data);
            }
        }

        /// <summary>
        /// 녹음 시간 업데이트 (외부에서 호출용)
        /// </summary>
        public async Task UpdateRecordingStats()
        {
            if (!_isRecording) return;

            var elapsed = DateTime.Now - _recordingStartTime;
            _recordingDuration = elapsed.ToString(@"hh\:mm\:ss");
            _recordingDataSize = _recordedChunks.Sum(chunk => chunk.Length) / 1024.0 / 1024.0;

            // 상태 변경 이벤트 발생 (UI 업데이트용)
            await RaiseRecordingStateChanged();
        }
        #endregion

        #region Private Methods
        private void InitializeRecordingState()
        {
            _isRecording = true;
            _recordedChunks.Clear();
            _recordingStartTime = DateTime.Now;
            _recordingDataSize = 0.0;

            _recordingTimer = new Timer(
                async _ => await UpdateRecordingStats(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1));
        }

        private void StopRecordingTimer()
        {
            _isRecording = false;
            _recordingTimer?.Dispose();
            _recordingTimer = null;
        }

        private byte[] CombineRecordedChunks()
        {
            var totalSize = _recordedChunks.Sum(chunk => chunk.Length);
            var combinedData = new byte[totalSize];
            var offset = 0;

            foreach (var chunk in _recordedChunks)
            {
                Buffer.BlockCopy(chunk, 0, combinedData, offset, chunk.Length);
                offset += chunk.Length;
            }

            return combinedData;
        }

        private async Task SaveRecordingToFile(byte[] data)
        {
            var base64Data = Convert.ToBase64String(data);
            var fileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.webm";

            await _jsRuntime.InvokeVoidAsync("downloadBase64File", base64Data, fileName, "audio/webm");

            NotifySuccess("녹음 완료", $"녹음이 완료되어 '{fileName}' 파일로 저장되었습니다.");
            _logger.LogInformation($"Recording saved to {fileName}, size: {data.Length / 1024.0 / 1024.0:F2} MB");
        }

        private async Task RaiseRecordingStateChanged()
        {
            if (OnRecordingStateChanged != null)
            {
                await OnRecordingStateChanged.Invoke();
            }
        }
        #endregion

        #region Notification Helpers
        private void Notify(NotificationSeverity severity, string summary, string detail, int duration = 4000)
        {
            _notificationService.Notify(new NotificationMessage
            {
                Severity = severity,
                Summary = summary,
                Detail = detail,
                Duration = duration
            });
        }

        private void NotifySuccess(string summary, string detail) =>
            Notify(NotificationSeverity.Success, summary, detail);

        private void NotifyError(string summary, Exception ex) =>
            Notify(NotificationSeverity.Error, "오류", $"{summary} 중 오류: {ex.Message}");

        private void NotifyWarn(string summary, string detail) =>
            Notify(NotificationSeverity.Warning, summary, detail);
        #endregion

        #region IDisposable
        public void Dispose()
        {
            _recordingTimer?.Dispose();
            _recordedChunks.Clear();
        }
        #endregion
    }
}