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
                var activities = new List<RecentActivity>();

                // Broadcast 테이블에서 최신 5개 가져오기
                try
                {
                    var broadcasts = await WicsService.GetBroadcasts(
                        filter: null, 
                        orderby: "CreatedAt desc", 
                        expand: "Channel", 
                        top: 5, 
                        skip: null, 
                        count: false
                    );
                    if (broadcasts?.Value != null)
                    {
                        foreach (var item in broadcasts.Value)
                        {
                            var channelName = item.Channel?.Name ?? "알 수 없는 채널";
                            activities.Add(new RecentActivity
                            {
                                Type = "broadcast",
                                Description = $"{channelName} 채널에서 방송을 시작했습니다",
                                User = "시스템",
                                Timestamp = item.CreatedAt
                            });
                        }
                    }
                }
                catch { /* 테이블이 없거나 오류 발생시 무시 */ }

                // Media 테이블에서 최신 5개 가져오기
                try
                {
                    var mediaQuery = new Radzen.Query
                    {
                        Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                        OrderBy = "CreatedAt desc",
                        Top = 5
                    };
                    var media = await WicsService.GetMedia(mediaQuery);
                    if (media?.Value != null)
                    {
                        foreach (var item in media.Value)
                        {
                            var fileName = item.FileName ?? "알 수 없는 파일";
                            activities.Add(new RecentActivity
                            {
                                Type = "media",
                                Description = $"{fileName} 미디어 파일을 추가했습니다",
                                User = "시스템",
                                Timestamp = item.CreatedAt
                            });
                        }
                    }
                }
                catch { /* 테이블이 없거나 오류 발생시 무시 */ }

                // Channel 테이블에서 최신 5개 가져오기
                try
                {
                    var channelQuery = new Radzen.Query
                    {
                        Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                        OrderBy = "CreatedAt desc",
                        Top = 5
                    };
                    var channels = await WicsService.GetChannels(channelQuery);
                    if (channels?.Value != null)
                    {
                        foreach (var item in channels.Value)
                        {
                            var channelName = item.Name ?? "알 수 없는 채널";
                            activities.Add(new RecentActivity
                            {
                                Type = "channel",
                                Description = $"{channelName} 채널을 생성했습니다",
                                User = "시스템",
                                Timestamp = item.CreatedAt
                            });
                        }
                    }
                }
                catch { /* 테이블이 없거나 오류 발생시 무시 */ }

                // Group 테이블에서 최신 5개 가져오기
                try
                {
                    var groupQuery = new Radzen.Query
                    {
                        Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                        OrderBy = "CreatedAt desc",
                        Top = 5
                    };
                    var groups = await WicsService.GetGroups(groupQuery);
                    if (groups?.Value != null)
                    {
                        foreach (var item in groups.Value)
                        {
                            var groupName = item.Name ?? "알 수 없는 그룹";
                            activities.Add(new RecentActivity
                            {
                                Type = "group",
                                Description = $"{groupName} 그룹을 생성했습니다",
                                User = "시스템",
                                Timestamp = item.CreatedAt
                            });
                        }
                    }
                }
                catch { /* 테이블이 없거나 오류 발생시 무시 */ }

                // Speaker 테이블에서 최신 5개 가져오기
                try
                {
                    var speakerQuery = new Radzen.Query
                    {
                        Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                        OrderBy = "CreatedAt desc",
                        Top = 5
                    };
                    var speakers = await WicsService.GetSpeakers(speakerQuery);
                    if (speakers?.Value != null)
                    {
                        foreach (var item in speakers.Value)
                        {
                            var speakerName = item.Name ?? "알 수 없는 스피커";
                            activities.Add(new RecentActivity
                            {
                                Type = "speaker",
                                Description = $"{speakerName} 스피커를 등록했습니다",
                                User = "시스템",
                                Timestamp = item.CreatedAt
                            });
                        }
                    }
                }
                catch { /* 테이블이 없거나 오류 발생시 무시 */ }

                // TTS 테이블에서 최신 5개 가져오기
                try
                {
                    var ttsQuery = new Radzen.Query
                    {
                        Filter = "DeleteYn eq 'N' or DeleteYn eq null",
                        OrderBy = "CreatedAt desc",
                        Top = 5
                    };
                    var tts = await WicsService.GetTts(ttsQuery);
                    if (tts?.Value != null)
                    {
                        foreach (var item in tts.Value)
                        {
                            var ttsName = item.Name ?? "알 수 없는 TTS";
                            activities.Add(new RecentActivity
                            {
                                Type = "tts",
                                Description = $"{ttsName} 음성 메시지를 생성했습니다",
                                User = "시스템",
                                Timestamp = item.CreatedAt
                            });
                        }
                    }
                }
                catch { /* 테이블이 없거나 오류 발생시 무시 */ }

                // 시간순으로 정렬하고 최대 10개만 가져오기
                recentActivities = activities
                    .OrderByDescending(a => a.Timestamp)
                    .Take(10)
                    .ToList();
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
                "channel" => "radio",
                "group" => "folder_special",
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
                "channel" => BadgeStyle.Info,
                "group" => BadgeStyle.Light,
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