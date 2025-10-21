# WebSocket Disconnect 시 코드 흐름 분석

## 파일 구조
- `WebSocketMiddleware.cs`: 메인 WebSocket 핸들러, finally 블록
- `WebSocketMiddleware.Connect.cs`: connect/disconnect 메시지 처리
- `WebSocketMiddleware.Dispose.cs`: CleanupBroadcastSessionAsync 메서드

## 시나리오 1: disconnect 메시지가 정상 처리되는 경우 (명시적으로 방송종료 버튼을 누른 경우)

### 클라이언트
1. `SelectChannel` 호출 (채널 전환)
2. `CleanupCurrentBroadcast` 호출
3. `StopWebSocketBroadcast` 호출
4. **disconnect 메시지 전송**
5. WebSocket 연결 닫기

### 서버
1. `ProcessMessageAsync`에서 disconnect 메시지 수신 (WebSocketMiddleware.cs 172-194줄)
2. `HandleDisconnectAsync` 호출 (WebSocketMiddleware.Connect.cs 99-104줄)
   ```csharp
   await CleanupBroadcastSessionAsync(req.BroadcastId, true);
   ```
3. `CleanupBroadcastSessionAsync(broadcastId, forceCleanup=true)` 실행 (WebSocketMiddleware.Dispose.cs 81-145줄)
   - forceCleanup=true이므로 86-102줄 조건문 건너뜀
   - 114줄: `audioMixingService.StopMixer(broadcastId)`
   - 117줄: `mediaBroadcastService.StopMediaByBroadcastIdAsync(broadcastId)`
   - 120줄: `ttsBroadcastService.StopTtsByBroadcastIdAsync(broadcastId)`
   - 124줄: `_broadcastSessions.TryRemove(broadcastId, out _)` - **세션 제거**
4. WebSocket 연결 닫힘
5. `finally` 블록 실행 (WebSocketMiddleware.cs 98-132줄)
   - 107줄: `_broadcastSessions.TryGetValue(broadcastId, out var session)` - **실패 (이미 제거됨)**
   - 아무 작업도 수행되지 않음

**결과**: 미디어/TTS/믹서 모두 정지, 세션 제거됨

## 시나리오 2: 비명시적 종료 (미디어/TTS 백엔드 실행 유지를 위한 종료, 명시적으로 방송 종료 버튼을 누르지 않고 채널을 나간 경우)

### 클라이언트
1. disconnect 메시지를 보내지 않고 WebSocket 연결만 닫음
2. 또는 브라우저 탭 닫기, 네트워크 끊김 등

### 서버
1. disconnect 메시지 수신 없이 WebSocket 연결만 닫힘
2. `HandleDisconnectAsync` **실행되지 않음**
3. `finally` 블록 실행 (WebSocketMiddleware.cs 98-132줄)
   - 100-103줄: connectionId로 세션 찾기
   - 107줄: `_broadcastSessions.TryGetValue(broadcastId, out var session)` - **성공**
   - 110줄: `session.WebSocket = null`
   - 113줄: `audioMixingService.RemoveMicrophoneStream(broadcastId)` - 마이크만 제거
   - 116줄: `mediaBroadcastService.GetStatusByBroadcastIdAsync(broadcastId)` - 미디어 재생 상태 확인
   - 118-121줄: **미디어가 재생 중인 경우**
     ```csharp
     if (mediaStatus?.IsPlaying == true)
     {
         // 미디어 재생 중 - 세션 유지, WebSocket만 null
         logger.LogInformation($"Client disconnected but media continues: {broadcastId}");
     }
     ```
     - 미디어/TTS 정지 **안 함**
     - 세션 제거 **안 함**
     - WebSocket만 null로 설정
   - 124-129줄: **미디어가 재생 중이지 않은 경우**
     ```csharp
     else
     {
         // 미디어도 없으면 전체 정리
         await audioMixingService.StopMixer(broadcastId);
         _broadcastSessions.TryRemove(broadcastId, out _);
         logger.LogInformation($"Broadcast session fully cleaned up: {broadcastId}");
     }
     ```

**결과 (미디어 재생 중)**: 마이크 스트림만 제거, 미디어/TTS는 계속 재생 (백엔드 유지), 세션 유지 (WebSocket=null)
**결과 (미디어 재생 중 아님)**: 믹서 정지, 세션 제거

### 의도
- 클라이언트 연결이 끊어져도 미디어/TTS 재생은 백엔드에서 계속 실행
- 재생이 완료되면 `OnPlaybackCompleted` 이벤트에서 정리

## CleanupBroadcastSessionAsync 메서드 상세

```csharp
private async Task<bool> CleanupBroadcastSessionAsync(
    ulong broadcastId, 
    bool forceCleanup = false, 
    bool updateDatabase = true)
```

### 파라미터
- `forceCleanup`: true면 미디어/TTS 재생 상태 무시하고 무조건 정리
- `updateDatabase`: DB 업데이트 여부

### 로직
1. **forceCleanup=false인 경우** (86-102줄)
   - 미디어 재생 중이면 → cleanup 건너뜀, false 반환
   - TTS 재생 중이면 → cleanup 건너뜀, false 반환

2. **forceCleanup=true인 경우** (또는 미디어/TTS 모두 재생 중이 아닌 경우)
   - 108-112줄: DB 업데이트 (스피커 소유권, 방송 상태)
   - 115줄: 오디오 믹서 정지
   - 118줄: 미디어 재생 정지
   - 121줄: TTS 재생 정지
   - 124줄: 세션 제거

## 핵심 차이점

| 항목 | 명시적 종료 (disconnect 메시지) | 비명시적 종료 (미디어 재생 중) |
|------|---------------------------|--------------------------------|
| 실행 경로 | HandleDisconnectAsync → CleanupBroadcastSessionAsync(forceCleanup=true) | finally 블록만 실행 |
| 미디어 정지 | O | X (백엔드 유지) |
| TTS 정지 | O | X (백엔드 유지) |
| 믹서 정지 | O | X (마이크만 제거) |
| 세션 제거 | O | X (세션 유지) |
| WebSocket 상태 | 세션 자체 제거됨 | session.WebSocket = null |
| 이후 정리 | 즉시 완전 정리 | OnPlaybackCompleted 이벤트 시 정리 (WebSocketMiddleware.Dispose.cs 12-72줄) |
