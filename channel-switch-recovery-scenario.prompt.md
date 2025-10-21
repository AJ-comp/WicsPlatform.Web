# 채널 전환 후 복귀 시나리오 상세 분석

## 시나리오 개요
1. 채널 A 선택 → 미디어 재생 시작
2. 채널 B로 전환
3. 다시 채널 A로 복귀

---

## 1단계: 채널 A 선택 + 미디어 재생

### 클라이언트 (BroadcastLiveTab.razor.cs)
1. `SelectChannel(채널 A)` 호출 (337-383줄)
   - `selectedChannel = 채널 A`
   - `LoadChannelData()` → 스피커/그룹/플레이리스트 로드
   - `channel.State` 확인 (374줄)
     - state=0 (대기) → 방송 복구 없이 종료

2. 사용자가 방송 시작 버튼 클릭
   - `StartBroadcast(isRecovery=false)` 호출 (483-609줄)
   - DB 저장 (스피커/그룹/플레이리스트/미디어/TTS)
   - WebSocket 연결: `InitializeWebSocketBroadcast()` (75-90줄, WebSocket.razor.cs)
     - `WebSocketService.StartBroadcastAsync(채널 A.Id, groupIds)`
     - `currentBroadcastId = 새로운 broadcastId` (예: 12345)
   - 마이크 활성화
   - `isBroadcasting = true`
   - **채널 상태 업데이트**: `UpdateChannelState(1)` (709-769줄)
     - DB: `Channels.State = 1` (방송 중)
     - 메모리: `selectedChannel.State = 1`

### 서버 (WebSocketMiddleware.Connect.cs)
1. WebSocket 연결 수신: `/broadcast/{채널 A.Id}`
2. `connect` 메시지 수신
3. `HandleConnectAsync` 실행 (13-96줄)
   - `broadcastId = 12345` (클라이언트가 보낸 값)
   - 스피커 소유권 확인 및 설정
   - `_broadcastSessions[12345] = new BroadcastSession { ... }`
   - 오디오 믹서 초기화
   - 볼륨 설정 적용

### 미디어 재생 시작
- 클라이언트: 플레이리스트에서 미디어 재생 버튼 클릭
- 서버: `api/mediaplayer/play` 호출
- 서버: 미디어 재생 시작, UDP 브로드캐스트 시작
- `MediaBroadcastService.IsPlaying = true`

### 현재 상태
```
클라이언트:
- selectedChannel = 채널 A
- isBroadcasting = true
- currentBroadcastId = 12345
- 채널 A.State = 1 (메모리)

서버:
- _broadcastSessions[12345] = { ChannelId: 채널 A, WebSocket: 연결됨, ... }
- MediaBroadcastService.IsPlaying = true (broadcastId: 12345)
- DB Channels.State = 1 (채널 A)
- UDP 브로드캐스트 실행 중
```

---

## 2단계: 채널 B로 전환

### 클라이언트 (BroadcastLiveTab.razor.cs)
1. `SelectChannel(채널 B)` 호출 (337-383줄)
2. 348-353줄: `isBroadcasting = true` 감지 → `CleanupCurrentBroadcast()` 호출

### CleanupCurrentBroadcast() 실행 (411-453줄)
```csharp
// 1. 방송 상태 초기화
isBroadcasting = false;
var broadcastIdToStop = currentBroadcastId;  // 12345
currentBroadcastId = null;

// 2. 타이머 정리
_broadcastTimer?.Dispose();

// 3. WebSocket 정리 ⭐ 핵심
await StopWebSocketBroadcast();

// 4. 마이크 정리
await CleanupMicrophone();

// 5. DB 업데이트 (이전 채널의 방송 상태를 종료로 변경)
await UpdateBroadcastRecordsToStopped();  // Broadcasts.OngoingYn = 'N'
// ⚠️ 주의: 여기서는 Channels.State를 업데이트하지 않음!

// 6. 루프백 설정 초기화
_currentLoopbackSetting = false;
```

### StopWebSocketBroadcast() 실행 (WebSocket.razor.cs 92-109줄)
```csharp
await WebSocketService.StopBroadcastAsync(12345);
```

### WebSocketService.StopBroadcastAsync() 실행 (BroadcastWebSocketService.cs 81-118줄)
```csharp
// 87-97줄: disconnect 메시지 전송
if (channelWs.WebSocket.State == WebSocketState.Open)
{
    var disconnectMessage = new
    {
        type = "disconnect",
        broadcastId = 12345
    };
    
    await SendMessageAsync(channelWs.WebSocket, JsonSerializer.Serialize(disconnectMessage));
    await channelWs.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Broadcast stopped", CancellationToken.None);
}
```

### 서버: disconnect 메시지 처리 (두 가지 경로 가능)

#### 경로 1: disconnect 메시지가 정상 처리되는 경우

**서버 (WebSocketMiddleware.cs 172-194줄)**
1. `ProcessMessageAsync`에서 `disconnect` 메시지 수신
2. `HandleDisconnectAsync` 호출 (Connect.cs 99-104줄)

**HandleDisconnectAsync (Connect.cs)**
```csharp
var req = JsonSerializer.Deserialize<DisconnectBroadcastRequest>(root);
await CleanupBroadcastSessionAsync(req.BroadcastId, true);  // forceCleanup=true
```

**CleanupBroadcastSessionAsync (Dispose.cs 81-145줄)**
```csharp
// forceCleanup=true이므로 86-102줄 건너뜀

// 108-112줄: DB 업데이트
await HandleSpeakerOwnershipOnBroadcastEnd(session.ChannelId);  // 채널 A
await UpdateBroadcastStatusInDatabase(session.ChannelId, false);  // ⭐

// 115줄: 오디오 믹서 정지
await audioMixingService.StopMixer(12345);

// 118줄: ⭐ 미디어 재생 정지
await mediaBroadcastService.StopMediaByBroadcastIdAsync(12345);

// 121줄: TTS 재생 정지
await ttsBroadcastService.StopTtsByBroadcastIdAsync(12345);

// 124줄: 세션 제거
_broadcastSessions.TryRemove(12345, out _);
```

**UpdateBroadcastStatusInDatabase (Dispose.cs 152-185줄)**
```csharp
// 160-165줄: ⭐ 채널 상태 업데이트
var channel = await context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
if (channel != null)
{
    channel.State = isOngoing ? (sbyte)1 : (sbyte)0;  // isOngoing=false → State=0
    channel.UpdatedAt = DateTime.Now;
}

// 167-175줄: Broadcast 레코드 업데이트
var broadcasts = await context.Broadcasts
    .Where(b => b.ChannelId == channelId && b.OngoingYn == "Y")
    .ToListAsync();

foreach (var broadcast in broadcasts)
{
    broadcast.OngoingYn = isOngoing ? "Y" : "N";  // "N"
    broadcast.UpdatedAt = DateTime.Now;
}

await context.SaveChangesAsync();
```

**WebSocket 연결 닫힘 → finally 블록 실행 (WebSocketMiddleware.cs 98-132줄)**
```csharp
// 107줄: 세션 찾기
if (_broadcastSessions.TryGetValue(12345, out var session))
{
    // ⚠️ 세션이 이미 제거됨 (124줄에서 TryRemove) → 조건문 실행 안 됨
}
// → 아무 작업 없음
```

**결과 (경로 1)**
```
서버:
- _broadcastSessions[12345] 제거됨 ✅
- MediaBroadcastService.IsPlaying = false ✅
- UDP 브로드캐스트 정지 ✅
- DB Channels.State = 0 (채널 A) ✅
- DB Broadcasts.OngoingYn = 'N' ✅

클라이언트:
- isBroadcasting = false
- currentBroadcastId = null
- selectedChannel = 채널 A (아직 변경 전)
```

#### 경로 2: disconnect 메시지가 처리되지 않는 경우 (비명시적 종료)

**서버**
1. `disconnect` 메시지가 `ProcessMessageAsync`에 도달하기 전에 WebSocket 연결 닫힘 (97줄 CloseAsync)
2. `HandleDisconnectAsync` **실행 안 됨**
3. `finally` 블록만 실행 (WebSocketMiddleware.cs 98-132줄)

**finally 블록**
```csharp
// 100-103줄: connectionId로 세션 찾기
var sessionsToRemove = _broadcastSessions
    .Where(kvp => kvp.Value.ConnectionId == connectionId)
    .Select(kvp => kvp.Key)
    .ToList();  // [12345]

foreach (var broadcastId in sessionsToRemove)
{
    if (_broadcastSessions.TryGetValue(12345, out var session))
    {
        // 110줄: WebSocket만 null로 설정
        session.WebSocket = null;

        // 113줄: 마이크 스트림만 제거
        await audioMixingService.RemoveMicrophoneStream(12345);

        // 116줄: 미디어 재생 상태 확인
        var mediaStatus = await mediaBroadcastService.GetStatusByBroadcastIdAsync(12345);

        // 118-121줄: ⭐ 미디어가 재생 중인 경우
        if (mediaStatus?.IsPlaying == true)
        {
            // 미디어 재생 중 - 세션 유지, WebSocket만 null
            logger.LogInformation($"Client disconnected but media continues: {12345}");
            // ⚠️ 미디어 정지 안 함, 세션 제거 안 함
        }
        else
        {
            // 미디어도 없으면 전체 정리
            await audioMixingService.StopMixer(12345);
            _broadcastSessions.TryRemove(12345, out _);
        }
    }
}
```

**결과 (경로 2)**
```
서버:
- _broadcastSessions[12345] 유지 ⚠️ (WebSocket=null)
- MediaBroadcastService.IsPlaying = true ⚠️ (계속 재생)
- UDP 브로드캐스트 계속 실행 ⚠️
- DB Channels.State = 1 ⚠️ (여전히 방송 중)
- DB Broadcasts.OngoingYn = 'N' ✅ (클라이언트가 UpdateBroadcastRecordsToStopped 호출)

클라이언트:
- isBroadcasting = false
- currentBroadcastId = null
- selectedChannel = 채널 A (아직 변경 전)
```

### SelectChannel 계속 실행

```csharp
// 355-383줄
selectedChannel = 채널 B;
micVolume = (int)(채널 B.MicVolume * 100);
// ...

ResetAllPanels();
InitializeSubPages();
await LoadChannelData();  // 채널 B 데이터 로드

// 374-382줄: 방송 복구 확인
if (channel.State == 1)  // 채널 B의 State
{
    await RecoverBroadcast();
}
```

### 현재 상태

**경로 1인 경우**
```
서버:
- 채널 A의 모든 리소스 정리됨 ✅
- DB Channels.State = 0 (채널 A)

클라이언트:
- selectedChannel = 채널 B
- isBroadcasting = false
```

**경로 2인 경우**
```
서버:
- 채널 A의 미디어/UDP 계속 실행 중 ⚠️
- DB Channels.State = 1 (채널 A) ⚠️
- _broadcastSessions[12345] 유지 (WebSocket=null)

클라이언트:
- selectedChannel = 채널 B
- isBroadcasting = false
```

---

## 3단계: 다시 채널 A로 복귀

### 클라이언트
1. `SelectChannel(채널 A)` 호출

### SelectChannel 실행 (337-383줄)
```csharp
// 342-346줄: 같은 채널 선택 체크
if (selectedChannel?.Id == channel?.Id)  // 채널 B와 채널 A 비교 → false
{
    return;  // 실행 안 됨
}

// 348-353줄: 방송 중 체크
if (isBroadcasting)  // false (2단계에서 false로 변경)
{
    await CleanupCurrentBroadcast();  // 실행 안 됨
}

// 355-368줄: 채널 선택
selectedChannel = 채널 A;
micVolume = (int)(채널 A.MicVolume * 100);
// ...
ResetAllPanels();
InitializeSubPages();
await LoadChannelData();

// 374-382줄: ⭐ 방송 복구 확인
if (channel.State == 1)
{
    _logger.LogInformation($"[4단계] state=1 감지 - 방송 복구 시작");
    await RecoverBroadcast();
}
else
{
    _logger.LogInformation($"[4단계] state=0 - 완료");
}
```

### 시나리오 분기

#### 시나리오 3-A: 경로 1로 진행된 경우 (정상 종료)

**채널 A.State = 0** (서버에서 UpdateBroadcastStatusInDatabase 호출)

```csharp
// 380-382줄
else
{
    _logger.LogInformation($"[4단계] state=0 - 완료");
}
// → 방송 복구 없음, 채널 선택만 완료
```

**최종 상태**
```
서버:
- 채널 A 관련 리소스 모두 정리됨
- DB Channels.State = 0 (채널 A)

클라이언트:
- selectedChannel = 채널 A
- isBroadcasting = false
- 방송 복구 없음
```

**결과**: 정상적으로 채널 A로 복귀, 미디어 재생 없음 ✅

#### 시나리오 3-B: 경로 2로 진행된 경우 (비명시적 종료)

**채널 A.State = 1** (서버 finally 블록에서 State 업데이트 안 함)

```csharp
// 374-378줄
if (channel.State == 1)
{
    _logger.LogInformation($"[4단계] state=1 감지 - 방송 복구 시작");
    await RecoverBroadcast();  // ⭐ 실행됨
}
```

### RecoverBroadcast() 실행 (Recovery.razor.cs 12-37줄)
```csharp
_logger.LogInformation("========== 방송 복구 시작 ==========");

ShowRecoveryUI();

// 방송 시작 (복구 모드 - DB 저장 건너뜀)
await StartBroadcast(isRecovery: true);

NotifySuccess("방송 복구 완료", $"'{selectedChannel.Name}' 채널의 방송이 복구되었습니다.");
```

### StartBroadcast(isRecovery=true) 실행 (BroadcastLiveTab.razor.cs 483-609줄)

**1단계: DB 저장 건너뜀**
```csharp
// 514-543줄
if (!isRecovery)
{
    // DB 저장 작업
}
else
{
    _logger.LogInformation("1단계: DB 저장 건너뜀 (복구 모드)");
}
```

**2단계: 오디오 믹서 초기화**
```csharp
if (!await InitializeAudioMixer())
{
    return;  // 실패 시 중단
}
```

**3단계: WebSocket 연결**
```csharp
if (!await InitializeWebSocketBroadcast(onlineGroups))
{
    return;  // 실패 시 중단
}
// currentBroadcastId = 새로운 broadcastId (예: 67890)
```

### 서버: 새 WebSocket 연결 처리

**HandleConnectAsync (Connect.cs 13-96줄)**
```csharp
var broadcastId = req.BroadcastId;  // 67890 (클라이언트가 생성한 새 ID)
var channelId = 채널 A.Id;

// 스피커 소유권 확인
var prepService = scope.ServiceProvider.GetRequiredService<IBroadcastPreparationService>();
var prepared = await prepService.PrepareAsync(channelId, selectedGroupIds);

// ⚠️ 기존 세션 확인
if (_broadcastSessions.ContainsKey(12345))  // 기존 broadcastId (경로 2에서 남아있음)
{
    // 기존 세션이 존재하지만 WebSocket=null 상태
    // 새로운 broadcastId로 세션 생성
}

// 새 세션 생성
_broadcastSessions[67890] = new BroadcastSession { ... };

// 오디오 믹서 초기화
await audioMixingService.InitializeMixer(67890, channelId, onlineSpeakers);

// 볼륨 설정 적용
// ...
```

**현재 서버 상태**
```
_broadcastSessions[12345] = { 
    ChannelId: 채널 A, 
    WebSocket: null,  // ⚠️ 기존 세션 (WebSocket 없음)
}

_broadcastSessions[67890] = { 
    ChannelId: 채널 A, 
    WebSocket: 연결됨,  // ✅ 새 세션
}

MediaBroadcastService:
- broadcastId 12345 → IsPlaying = true ⚠️ (기존 미디어 계속 재생 중)
- broadcastId 67890 → IsPlaying = false (새 세션)

UDP 브로드캐스트:
- broadcastId 12345로 계속 실행 중 ⚠️
```

**4단계: 마이크 활성화**
```csharp
if (!await EnableMicrophone())
{
    await CleanupFailedBroadcast();
    return;
}
```

**5-7단계: 방송 상태 초기화 및 DB 업데이트**
```csharp
InitializeBroadcastState();
await CreateBroadcastRecords(onlineSpeakers);
await UpdateChannelState(1);  // Channels.State = 1 (이미 1이지만 재확인)
```

### 최종 상태 (시나리오 3-B)

```
서버:
_broadcastSessions[12345] = { ChannelId: 채널 A, WebSocket: null } ⚠️ 좀비 세션
_broadcastSessions[67890] = { ChannelId: 채널 A, WebSocket: 연결됨 } ✅ 정상 세션

MediaBroadcastService:
- broadcastId 12345 → 미디어 계속 재생 ⚠️
- broadcastId 67890 → 미디어 없음

UDP:
- broadcastId 12345로 UDP 패킷 전송 중 ⚠️
- broadcastId 67890은 미디어 재생 시작 전까지 UDP 없음

DB:
- Channels.State = 1 (채널 A)
- Broadcasts: 
  - 기존: OngoingYn='N' (broadcastId 12345 관련)
  - 새로: OngoingYn='Y' (broadcastId 67890 관련)

클라이언트:
- selectedChannel = 채널 A
- isBroadcasting = true
- currentBroadcastId = 67890
```

### 문제점 (시나리오 3-B)

1. **좀비 세션**: `_broadcastSessions[12345]`가 제거되지 않고 남아있음
2. **미디어 중복**: 
   - 기존 broadcastId 12345의 미디어가 계속 재생 중
   - 새로운 broadcastId 67890으로 또 미디어를 재생하면 **두 개가 동시 재생됨**
3. **UDP 충돌**: 
   - 두 broadcastId가 같은 채널 A의 스피커로 UDP 전송
   - 스피커에서 두 개의 오디오 스트림이 섞여서 재생될 수 있음
4. **리소스 누수**: 기존 세션의 리소스가 해제되지 않음

---

## 정리

### 정상 시나리오 (경로 1)
```
채널 A 선택 → 미디어 재생 → 채널 B 전환 (disconnect 정상 처리) → 채널 A 복귀
                                ↓
                        모든 리소스 정리됨, State=0
                                ↓
                        복구 없이 깨끗한 상태로 시작
```

### 문제 시나리오 (경로 2)
```
채널 A 선택 → 미디어 재생 → 채널 B 전환 (비명시적 종료) → 채널 A 복귀
                                ↓
                    미디어 계속 재생, State=1, 좀비 세션
                                ↓
                        State=1 감지 → 방송 복구
                                ↓
                    새 세션 생성, 기존 세션과 충돌 ⚠️
```

### 핵심 차이점

| 항목 | 경로 1 (정상) | 경로 2 (문제) |
|------|-------------|-------------|
| disconnect 메시지 처리 | O | X |
| 미디어 정지 | O | X (계속 재생) |
| 세션 제거 | O | X (좀비 세션) |
| Channels.State | 0 | 1 |
| 채널 A 복귀 시 | 복구 없음 | 복구 실행 |
| 최종 세션 수 | 1개 (새로운) | 2개 (기존 + 새로운) ⚠️ |
| 미디어 재생 | 정상 | 중복 재생 가능 ⚠️ |
