using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;

namespace WicsPlatform.Client.Pages
{
    public partial class Index
    {
        [Inject]
        protected IJSRuntime JSRuntime { get; set; }

        [Inject]
        protected NavigationManager NavigationManager { get; set; }

        [Inject]
        protected DialogService DialogService { get; set; }

        [Inject]
        protected TooltipService TooltipService { get; set; }

        [Inject]
        protected ContextMenuService ContextMenuService { get; set; }

        [Inject]
        protected NotificationService NotificationService { get; set; }

        [Inject]
        protected SecurityService Security { get; set; }

        [Inject]
        protected wicsService WicsService { get; set; }

        // 통계 데이터
        protected int totalChannels = 0;
        protected int totalSpeakers = 0;
        protected int onlineSpeakers = 0;
        protected int totalMedia = 0;
        protected string totalSize = "0 MB";
        protected int totalTts = 0;
        protected int storageUsage = 0;

        // 최근 활동
        protected bool isLoadingActivities = true;
        protected List<RecentActivity> recentActivities = new List<RecentActivity>();

        protected override async Task OnInitializedAsync()
        {
            await LoadDashboardData();
            await LoadRecentActivities();
        }

        private async Task LoadDashboardData()
        {
            try
            {
                // 채널 수 가져오기
                var channelsQuery = new Radzen.Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                };
                var channelsResult = await WicsService.GetChannels(channelsQuery);
                totalChannels = channelsResult.Value?.Count() ?? 0;

                // 스피커 수 가져오기
                var speakersQuery = new Radzen.Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                };
                var speakersResult = await WicsService.GetSpeakers(speakersQuery);
                totalSpeakers = speakersResult.Value?.Count() ?? 0;

                // 온라인 스피커 수 가져오기
                var onlineSpeakersQuery = new Radzen.Query
                {
                    Filter = "(DeleteYn eq 'N' or DeleteYn eq null) and State eq 1"
                };
                var onlineSpeakersResult = await WicsService.GetSpeakers(onlineSpeakersQuery);
                onlineSpeakers = onlineSpeakersResult.Value?.Count() ?? 0;

                // 미디어 수 가져오기
                var mediaQuery = new Radzen.Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                };
                var mediaResult = await WicsService.GetMedia(mediaQuery);
                totalMedia = mediaResult.Value?.Count() ?? 0;

                // TTS 수 가져오기
                var ttsQuery = new Radzen.Query
                {
                    Filter = "DeleteYn eq 'N' or DeleteYn eq null"
                };
                var ttsResult = await WicsService.GetTts(ttsQuery);
                totalTts = ttsResult.Value?.Count() ?? 0;

                // ✅ 스토리지 사용량 계산 - 가상 데이터 제거
                // TODO: 실제 시스템에서는 서버에서 실제 스토리지 사용량을 계산해야 함
                storageUsage = 0; // 실제 데이터가 없을 때는 0으로 표시
                totalSize = "0 MB"; // 실제 데이터가 없을 때는 0으로 표시
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"대시보드 데이터를 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
        }

        private async Task LoadRecentActivities()
        {
            try
            {
                isLoadingActivities = true;

                // ✅ 가상의 최근 활동 데이터 시뮬레이션 제거
                // 실제 시스템에서는 데이터베이스에서 실제 로그 데이터를 가져와야 함
                await Task.Delay(500); // 로딩 시뮬레이션

                // TODO: 실제 데이터베이스에서 로그 데이터 조회
                // var query = new Radzen.Query
                // {
                //     Filter = "CreatedAt >= @0",
                //     FilterParameters = new object[] { DateTime.Now.AddDays(-7) },
                //     OrderBy = "CreatedAt desc",
                //     Top = 5
                // };
                // var result = await WicsService.GetSystemLogs(query);
                // recentActivities = result.Value.ToList();
                
                recentActivities = new List<RecentActivity>(); // 빈 목록으로 초기화
            }
            catch (Exception ex)
            {
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "오류",
                    Detail = $"최근 활동을 불러오는 중 오류가 발생했습니다: {ex.Message}",
                    Duration = 4000
                });
            }
            finally
            {
                isLoadingActivities = false;
            }
        }

        private string GetActivityIcon(string type)
        {
            return type?.ToLower() switch
            {
                "broadcast" => "settings_input_antenna",
                "speaker" => "speaker",
                "media" => "audio_file",
                "tts" => "record_voice_over",
                _ => "event"
            };
        }

        private BadgeStyle GetActivityBadgeStyle(string type)
        {
            return type?.ToLower() switch
            {
                "broadcast" => BadgeStyle.Primary,
                "speaker" => BadgeStyle.Secondary,
                "media" => BadgeStyle.Warning,
                "tts" => BadgeStyle.Success,
                _ => BadgeStyle.Light
            };
        }

        // 최근 활동 모델
        public class RecentActivity
        {
            public string Type { get; set; }
            public string Description { get; set; }
            public string User { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}