using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Radzen;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using WicsPlatform.Shared;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastVolumeSection
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }
        [Parameter] public EventCallback OnVolumeSaved { get; set; }
        [Parameter] public string CurrentBroadcastId { get; set; } // 현재 방송 ID (실시간 조절용)

        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected HttpClient Http { get; set; }
        [Inject] protected ILogger<BroadcastVolumeSection> Logger { get; set; }

        private int micVolume = 50;
        private int ttsVolume = 50;
        private int mediaVolume = 50;
        private int globalVolume = 50;
        private bool isSavingVolumes = false;

        // 디바운싱을 위한 필드
        private Timer _debounceTimer;
        private bool _hasUnsavedChanges = false;
        private readonly object _debouncelock = new object();

        // 중복 초기화 방지를 위한 변수
        private ulong? _lastLoadedChannelId = null;

        protected override void OnParametersSet()
        {
            // 채널이 실제로 변경되었을 때만 볼륨 값을 초기화
            if (Channel != null && _lastLoadedChannelId != Channel.Id)
            {
                _lastLoadedChannelId = Channel.Id;
                LoadVolumeFromChannel();
            }
        }

        // 채널에서 볼륨 값을 로드하는 별도 메서드
        private void LoadVolumeFromChannel()
        {
            if (Channel != null)
            {
                // 채널의 볼륨 값을 로드 (테이블 값은 0~1이므로 100을 곱함)
                micVolume = (int)(Channel.MicVolume * 100);
                ttsVolume = (int)(Channel.TtsVolume * 100);
                mediaVolume = (int)(Channel.MediaVolume * 100);
                globalVolume = (int)(Channel.Volume * 100);
                _hasUnsavedChanges = false;
            }
        }

        private async Task TogglePanel()
        {
            await IsCollapsedChanged.InvokeAsync(!IsCollapsed);
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
            _hasUnsavedChanges = true;

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
                        BroadcastId = string.IsNullOrWhiteSpace(CurrentBroadcastId) ? null : ulong.Parse(CurrentBroadcastId), // 방송 중이면 실시간 적용
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

                _hasUnsavedChanges = false;

                // 로컬 채널 객체의 볼륨 값도 업데이트하여 동기화
                if (Channel != null)
                {
                    Channel.MicVolume = micVolume / 100f;
                    Channel.TtsVolume = ttsVolume / 100f;
                    Channel.MediaVolume = mediaVolume / 100f;
                    Channel.Volume = globalVolume / 100f;
                    Channel.UpdatedAt = DateTime.Now;
                }

                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "저장 완료",
                    Detail = CurrentBroadcastId != null
                        ? "볼륨 설정이 저장되고 실시간으로 적용되었습니다."
                        : "볼륨 설정이 저장되었습니다. 다음 방송부터 적용됩니다.",
                    Duration = 3000
                });

                // 부모 컴포넌트에 저장 완료 알림
                await OnVolumeSaved.InvokeAsync();
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

        public void Dispose()
        {
            _debounceTimer?.Dispose();
        }
    }
}