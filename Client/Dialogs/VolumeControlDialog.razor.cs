using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Radzen;
using WicsPlatform.Shared;

namespace WicsPlatform.Client.Dialogs
{
    public partial class VolumeControlDialog : IDisposable
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public string CurrentBroadcastId { get; set; }
        [Parameter] public bool IsBroadcasting { get; set; }

        [Inject] protected DialogService DialogService { get; set; }
        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected HttpClient Http { get; set; }
        [Inject] protected ILogger<VolumeControlDialog> Logger { get; set; }

        private int micVolume = 50;
        private int ttsVolume = 50;
        private int mediaVolume = 50;
        private int globalVolume = 50;
        private bool isSavingVolumes = false;

        // 디바운싱을 위한 필드
        private Timer _debounceTimer;
        private bool _hasUnsavedChanges = false;
        private readonly object _debouncelock = new object();

        // 초기값 저장 (취소 시 복원용)
        private int _originalMicVolume;
        private int _originalTtsVolume;
        private int _originalMediaVolume;
        private int _originalGlobalVolume;

        protected override void OnInitialized()
        {
            if (Channel != null)
            {
                // 채널의 볼륨 값을 로드 (테이블 값은 0~1이므로 100을 곱함)
                micVolume = _originalMicVolume = (int)(Channel.MicVolume * 100);
                ttsVolume = _originalTtsVolume = (int)(Channel.TtsVolume * 100);
                mediaVolume = _originalMediaVolume = (int)(Channel.MediaVolume * 100);
                globalVolume = _originalGlobalVolume = (int)(Channel.Volume * 100);
            }
        }

        private void UpdateVolume(string volumeType, int value)
        {
            switch (volumeType)
            {
                case "mic":
                    micVolume = value;
                    break;
                case "tts":
                    ttsVolume = value;
                    break;
                case "media":
                    mediaVolume = value;
                    break;
                case "global":
                    globalVolume = value;
                    break;
            }

            // 변경 사항이 있음을 표시
            _hasUnsavedChanges = CheckForChanges();

            // 디바운싱을 위한 타이머 재설정
            lock (_debouncelock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(async _ =>
                {
                    await InvokeAsync(StateHasChanged);
                }, null, 100, Timeout.Infinite); // 100ms 후에만 UI 업데이트
            }
        }

        private bool CheckForChanges()
        {
            return micVolume != _originalMicVolume ||
                   ttsVolume != _originalTtsVolume ||
                   mediaVolume != _originalMediaVolume ||
                   globalVolume != _originalGlobalVolume;
        }

        private async Task SaveVolumes()
        {
            if (Channel == null) return;

            try
            {
                isSavingVolumes = true;
                await InvokeAsync(StateHasChanged);

                // VolumeController API 호출
                var volumeRequests = new[]
                {
                    new VolumeRequest
                    {
                        BroadcastId = string.IsNullOrWhiteSpace(CurrentBroadcastId) ? null : ulong.Parse(CurrentBroadcastId),
                        ChannelId = Channel.Id,
                        Source = AudioSource.Microphone,
                        Volume = micVolume / 100f
                    },
                    new VolumeRequest
                    {
                        BroadcastId = string.IsNullOrWhiteSpace(CurrentBroadcastId) ? null : ulong.Parse(CurrentBroadcastId),
                        ChannelId = Channel.Id,
                        Source = AudioSource.TTS,
                        Volume = ttsVolume / 100f
                    },
                    new VolumeRequest
                    {
                        BroadcastId = string.IsNullOrWhiteSpace(CurrentBroadcastId) ? null : ulong.Parse(CurrentBroadcastId),
                        ChannelId = Channel.Id,
                        Source = AudioSource.Media,
                        Volume = mediaVolume / 100f
                    },
                    new VolumeRequest
                    {
                        BroadcastId = string.IsNullOrWhiteSpace(CurrentBroadcastId) ? null : ulong.Parse(CurrentBroadcastId),
                        ChannelId = Channel.Id,
                        Source = AudioSource.Master,
                        Volume = globalVolume / 100f
                    }
                };

                // 각 볼륨 설정을 VolumeController로 전송
                foreach (var request in volumeRequests)
                {
                    var response = await Http.PostAsJsonAsync("api/volume/set", request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Logger.LogError($"Volume update failed for {request.Source}: {errorContent}");
                    }
                    else
                    {
                        var result = await response.Content.ReadFromJsonAsync<VolumeSetResponse>();
                        Logger.LogInformation($"Volume updated - Source: {request.Source}, " +
                            $"Volume: {request.Volume:P0}, SavedToDb: {result?.SavedToDb}, " +
                            $"BroadcastId: {result?.BroadcastId}");
                    }
                }

                // 로컬 채널 객체의 볼륨 값도 업데이트하여 동기화
                if (Channel != null)
                {
                    Channel.MicVolume = micVolume / 100f;
                    Channel.TtsVolume = ttsVolume / 100f;
                    Channel.MediaVolume = mediaVolume / 100f;
                    Channel.Volume = globalVolume / 100f;
                    Channel.UpdatedAt = DateTime.Now;
                }

                _hasUnsavedChanges = false;

                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "저장 완료",
                    Detail = IsBroadcasting && CurrentBroadcastId != null
                        ? "볼륨 설정이 저장되고 실시간으로 적용되었습니다."
                        : "볼륨 설정이 저장되었습니다. 다음 방송부터 적용됩니다.",
                    Duration = 3000
                });

                // 다이얼로그 닫기
                DialogService.Close(true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to save volume settings");
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"볼륨 설정 저장 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isSavingVolumes = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task ResetVolumes()
        {
            micVolume = 50;
            ttsVolume = 50;
            mediaVolume = 50;
            globalVolume = 50;
            _hasUnsavedChanges = true;

            await InvokeAsync(StateHasChanged);

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "재설정",
                Detail = "볼륨이 기본값(50%)으로 재설정되었습니다.",
                Duration = 3000
            });
        }

        private void ApplyPreset(string presetType)
        {
            switch (presetType)
            {
                case "music":
                    micVolume = 30;
                    ttsVolume = 40;
                    mediaVolume = 80;
                    globalVolume = 70;
                    break;
                case "voice":
                    micVolume = 80;
                    ttsVolume = 70;
                    mediaVolume = 40;
                    globalVolume = 70;
                    break;
                case "balanced":
                    micVolume = 50;
                    ttsVolume = 50;
                    mediaVolume = 50;
                    globalVolume = 50;
                    break;
                case "quiet":
                    micVolume = 30;
                    ttsVolume = 30;
                    mediaVolume = 30;
                    globalVolume = 30;
                    break;
            }

            _hasUnsavedChanges = true;
            StateHasChanged();

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "프리셋 적용",
                Detail = $"{GetPresetName(presetType)} 프리셋이 적용되었습니다.",
                Duration = 2000
            });
        }

        private string GetPresetName(string presetType) => presetType switch
        {
            "music" => "음악 모드",
            "voice" => "음성 모드",
            "balanced" => "균형 모드",
            "quiet" => "조용히",
            _ => "사용자 정의"
        };

        private void Cancel()
        {
            if (_hasUnsavedChanges)
            {
                // 원래 값으로 복원
                micVolume = _originalMicVolume;
                ttsVolume = _originalTtsVolume;
                mediaVolume = _originalMediaVolume;
                globalVolume = _originalGlobalVolume;
            }

            DialogService.Close(false);
        }

        public void Dispose()
        {
            _debounceTimer?.Dispose();
        }
    }
}