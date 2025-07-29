using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Radzen;
using System.Net.Http;
using System.Net.Http.Json;

namespace WicsPlatform.Client.Pages.SubPages
{
    public partial class BroadcastVolumeSection
    {
        [Parameter] public WicsPlatform.Server.Models.wics.Channel Channel { get; set; }
        [Parameter] public bool IsCollapsed { get; set; }
        [Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }
        [Parameter] public EventCallback OnVolumeSaved { get; set; }

        [Inject] protected NotificationService NotificationService { get; set; }
        [Inject] protected HttpClient Http { get; set; }

        private int micVolume = 50;
        private int ttsVolume = 50;
        private int mediaVolume = 50;
        private int globalVolume = 50;
        private bool isSavingVolumes = false;

        protected override void OnParametersSet()
        {
            if (Channel != null)
            {
                // 채널의 볼륨 값을 로드 (테이블 값은 0~1이므로 100을 곱함)
                micVolume = (int)(Channel.MicVolume * 100);
                ttsVolume = (int)(Channel.TtsVolume * 100);
                mediaVolume = (int)(Channel.MediaVolume * 100);
                globalVolume = (int)(Channel.Volume * 100);
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
        }

        private async Task SaveVolumes()
        {
            if (Channel == null) return;

            try
            {
                isSavingVolumes = true;

                var updateData = new
                {
                    MicVolume = micVolume / 100f,
                    TtsVolume = ttsVolume / 100f,
                    MediaVolume = mediaVolume / 100f,
                    Volume = globalVolume / 100f,
                    UpdatedAt = DateTime.Now
                };

                var response = await Http.PatchAsJsonAsync($"odata/wics/Channels(Id={Channel.Id})", updateData);

                if (response.IsSuccessStatusCode)
                {
                    NotificationService.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Success,
                        Summary = "저장 완료",
                        Detail = "볼륨 설정이 저장되었습니다.",
                        Duration = 3000
                    });

                    // 부모 컴포넌트에 저장 완료 알림
                    await OnVolumeSaved.InvokeAsync();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"서버 응답 오류: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
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
            }
        }

        private void ResetVolumes()
        {
            micVolume = 50;
            ttsVolume = 50;
            mediaVolume = 50;
            globalVolume = 50;

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "재설정",
                Detail = "볼륨이 기본값(50%)으로 재설정되었습니다.",
                Duration = 3000
            });
        }
    }
}
