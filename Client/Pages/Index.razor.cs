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

                // 스토리지 사용량 시뮬레이션 (실제로는 서버에서 계산)
                storageUsage = new Random().Next(20, 85);
                totalSize = $"{new Random().Next(100, 999)} MB";
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

                // 최근 활동 데이터 시뮬레이션 (실제로는 로그 테이블에서 가져와야 함)
                await Task.Delay(500); // 로딩 시뮬레이션

                recentActivities = new List<RecentActivity>
                {
                    new RecentActivity
                    {
                        Type = "broadcast",
                        Description = "채널 '1층 로비' 방송 시작",
                        User = Security.User?.Name ?? "관리자",
                        Timestamp = DateTime.Now.AddMinutes(-5)
                    },
                    new RecentActivity
                    {
                        Type = "speaker",
                        Description = "스피커 'SP-001' 추가됨",
                        User = Security.User?.Name ?? "관리자",
                        Timestamp = DateTime.Now.AddMinutes(-30)
                    },
                    new RecentActivity
                    {
                        Type = "media",
                        Description = "배경음악 3개 파일 업로드",
                        User = Security.User?.Name ?? "관리자",
                        Timestamp = DateTime.Now.AddHours(-1)
                    },
                    new RecentActivity
                    {
                        Type = "tts",
                        Description = "안내 방송 TTS 생성",
                        User = Security.User?.Name ?? "관리자",
                        Timestamp = DateTime.Now.AddHours(-2)
                    },
                    new RecentActivity
                    {
                        Type = "system",
                        Description = "시스템 백업 완료",
                        User = "시스템",
                        Timestamp = DateTime.Now.AddHours(-3)
                    }
                };
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
            return type switch
            {
                "broadcast" => "play_circle",
                "speaker" => "speaker",
                "media" => "audio_file",
                "tts" => "record_voice_over",
                "system" => "settings",
                _ => "info"
            };
        }

        private string GetActivityColor(string type)
        {
            return type switch
            {
                "broadcast" => "var(--rz-primary)",
                "speaker" => "var(--rz-info)",
                "media" => "var(--rz-warning)",
                "tts" => "var(--rz-success)",
                "system" => "var(--rz-secondary)",
                _ => "var(--rz-text-color)"
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