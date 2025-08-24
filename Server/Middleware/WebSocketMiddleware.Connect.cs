using Microsoft.EntityFrameworkCore;
using System.Net.WebSockets;
using System.Text.Json;
using WicsPlatform.Server.Contracts;
using WicsPlatform.Server.Data;
using WicsPlatform.Server.Services;

namespace WicsPlatform.Server.Middleware
{
    public partial class WebSocketMiddleware
    {
        private async Task HandleConnectAsync(WebSocket webSocket, string connectionId, ulong channelId, JsonElement root)
        {
            if (root.TryGetProperty("broadcastId", out var broadcastIdElement))
            {
                var broadcastId = broadcastIdElement.GetString();
                var selectedGroupIds = root.GetProperty("selectedGroupIds").EnumerateArray()
                    .Select(e => e.GetUInt64())
                    .ToList();

                // DB에서 온라인 스피커, 미디어, TTS 정보를 한번에 로드
                List<SpeakerInfo> onlineSpeakers;
                List<MediaInfo> selectedMedia;
                List<TtsInfo> selectedTts;

                using (var scope = serviceScopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<wicsContext>();

                    // 선택된 그룹의 온라인 스피커만 조회
                    onlineSpeakers = await (
                        from msg in context.MapSpeakerGroups
                        join s in context.Speakers on msg.SpeakerId equals s.Id
                        where selectedGroupIds.Contains(msg.GroupId)
                              && msg.LastYn == "Y"
                              && s.State == 1  // 온라인 상태
                              && s.DeleteYn == "N"
                        select new SpeakerInfo
                        {
                            Id = s.Id,
                            Ip = s.VpnUseYn == "Y" ? s.VpnIp : s.Ip,  // VPN 사용 여부에 따라 IP 선택
                            Name = s.Name,
                            ChannelId = channelId,
                            UseVpn = s.VpnUseYn == "Y"  // VPN 사용 여부 정보도 저장 (디버깅용)
                        }
                    ).Distinct().ToListAsync();

                    // 채널에 선택된 미디어 리스트 조회
                    selectedMedia = await (
                        from mcm in context.MapChannelMedia
                        join m in context.Media on mcm.MediaId equals m.Id
                        where mcm.ChannelId == channelId
                              && mcm.DeleteYn != "Y"
                              && m.DeleteYn != "Y"
                        orderby mcm.Id
                        select new MediaInfo
                        {
                            Id = m.Id,
                            FileName = m.FileName,
                            FullPath = m.FullPath
                        }
                    ).ToListAsync();

                    // 채널에 선택된 TTS 리스트 조회
                    selectedTts = await (
                        from mct in context.MapChannelTts
                        join t in context.Tts on mct.TtsId equals t.Id
                        where mct.ChannelId == channelId
                              && mct.DeleteYn != "Y"
                              && t.DeleteYn != "Y"
                        orderby mct.Id
                        select new TtsInfo
                        {
                            Id = t.Id,
                            Name = t.Name,
                            Content = t.Content
                        }
                    ).ToListAsync();
                }

                logger.LogInformation($"Broadcast {broadcastId} - Found {onlineSpeakers.Count} online speakers for channel {channelId}");
                logger.LogInformation($"  - 선택된 미디어: {selectedMedia.Count}개");
                logger.LogInformation($"  - 선택된 TTS: {selectedTts.Count}개");

                // VPN 사용 스피커 로그 (디버깅용)
                var vpnSpeakers = onlineSpeakers.Where(s => s.UseVpn).ToList();
                if (vpnSpeakers.Any())
                {
                    logger.LogInformation($"VPN speakers: {string.Join(", ", vpnSpeakers.Select(s => $"{s.Name}({s.Ip})"))}");
                }

                var session = new BroadcastSession
                {
                    BroadcastId = broadcastId,
                    ChannelId = channelId,
                    ConnectionId = connectionId,
                    StartTime = DateTime.UtcNow,
                    SelectedGroupIds = selectedGroupIds,
                    WebSocket = webSocket,
                    OnlineSpeakers = onlineSpeakers,
                    SelectedMedia = selectedMedia,
                    SelectedTts = selectedTts,
                };

                _broadcastSessions[broadcastId] = session;

                await audioMixingService.InitializeMixer(broadcastId, onlineSpeakers);

                // 연결 확인 메시지 전송
                var response = new
                {
                    type = "connected",
                    broadcastId = broadcastId,
                    channelId = channelId,
                    onlineSpeakerCount = onlineSpeakers.Count,
                    selectedMediaCount = selectedMedia.Count,
                    selectedTtsCount = selectedTts.Count,
                    status = "ok"
                };

                await SendMessageAsync(webSocket, JsonSerializer.Serialize(response));
            }
        }

        private async Task HandleDisconnectAsync(JsonElement root)
        {
            if (root.TryGetProperty("broadcastId", out var broadcastIdElement))
            {
                var broadcastId = broadcastIdElement.GetString();

                await audioMixingService.StopMixer(broadcastId);

                if (_broadcastSessions.TryRemove(broadcastId, out var session))
                {
                    logger.LogInformation($"Broadcast disconnected: {broadcastId}");
                }
            }
        }
    }
}
