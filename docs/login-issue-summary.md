# 내부망 접속 시 로그인 루프(계속 로그인 화면으로 돌아감) 원인과 해결 내역

본 문서는 내부망(IP/자체서명 인증서) 환경에서 발생하던 "로그인 성공 후 보호 페이지 진입 시 다시 로그인 페이지로 리다이렉트되는" 문제를 정리합니다.

## 증상 요약
- 로그아웃 후 방송 관리 페이지(/manage-broad-cast)로 이동하면 로그인 페이지로 리다이렉트됨.
- 로그인하면 서버 로그에는 SUCCESS 및 /manage-broad-cast로 redirect 표기되지만, 클라이언트는 다시 /Login으로 돌아가 루프 발생.
- 브라우저를 완전히 종료 후 재접속하면 자동 로그인 상태가 되는 경우가 있음.

## 로그 근거 (요약)
- POST /Account/Login 또는 /Account/LoginApi → 200/302 및 SUCCESS.
- 직후 GET /manage-broad-cast → 302 /Login.
- 즉시 뒤따르는 요청에 인증 쿠키(.WicsPlatform.Identity)가 첨부되지 않음 → 서버는 미인증으로 간주하고 /Login으로 보냄.

## 원인
1) 브라우저 쿠키 정책 + IP/미신뢰 HTTPS 환경
   - SameSite=None, Secure=Always 조합은 IP/자체서명 인증서, 혹은 HTTP에서 브라우저가 쿠키 저장을 거부할 수 있음.
   - 결과적으로 Set-Cookie는 내려가지만 브라우저가 저장/전송하지 않아 루프가 발생.

2) SSR(서버 프리렌더) 시점의 인증 체크
   - 보호 페이지가 서버에서 먼저 프리렌더될 때 쿠키가 아직 적용되지 않으면 [Authorize]에 걸려 302 /Login으로 리다이렉트.
   - 쿠키 저장 타이밍/전파 지연 + 프리렌더링이 결합되면 루프가 빈번.

## 적용한 해결책
### 1. 내부망용 쿠키 정책 완화 스위치 추가
- 설정 키: `AuthCookies:RelaxForIpClients` (Server/appsettings*.json)
- 동작
  - true일 때: Identity 쿠키 SameSite=Lax, Secure=None로 완화.
  - false일 때: 기본 보안 설정 유지(SameSite=None, Secure=Always).
- 관련 코드: Server/Program.cs (ConfigureApplicationCookie, CookiePolicyOptions)

### 2. 잘못된 구성 키 수정
- 기존 오타: `AuthCookies:ㅊㅊ` → 올바른 키: `AuthCookies:RelaxForIpClients`.

### 3. 로그아웃 시 쿠키 강제 삭제
- SignInManager.SignOutAsync() 후 `.WicsPlatform.Identity`를 명시적으로 `Response.Cookies.Delete(...)` 처리.
- 관련 파일: Server/Controllers/AccountController.cs

### 4. 로그인 흐름 개선(302 → API 기반)
- 서버: `POST /Account/LoginApi` 추가. 성공 시 JSON `{ success, redirectUrl }` 반환하며 쿠키만 설정(302 없음).
- 클라이언트: Login 페이지가 `HttpClient.PostAsJsonAsync("Account/LoginApi")`로 로그인 후 `NavigationManager.NavigateTo(..., forceLoad:true)` 이동.
- 302 즉시 재요청 타이밍에서 쿠키가 누락되는 문제를 회피.
- 관련 파일: 
  - Server/Controllers/AccountController.cs (LoginApi)
  - Client/Pages/Login.razor, Login.razor.cs

### 5. 서버 프리렌더 비활성화
- SSR 단계에서 [Authorize]로 302가 먼저 발생하지 않도록 프리렌더를 끔.
- 변경: Server/Components/App.razor
  - `<HeadOutlet @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />`
  - `<Routes @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />`
- 주의: 클라이언트 페이지(.razor)에는 @rendermode를 쓰지 않음(컴파일 오류 방지).

### 6. 인증 상태 전파 지연 대응(보조)
- RedirectToLogin에서 `AuthenticationStateProvider.GetAuthenticationStateAsync()`로 실제 인증 상태를 한 번 더 확인하도록 조정(전파 레이스 완화).

### 7. 쿠키 트레이스(옵션)
- 문제 재현 시 요청 Cookie/응답 Set-Cookie를 파일로 기록해 원인 확정.
- 활성화: `AuthLogging.TraceCookies=true` + `AuthLogging.LoginLogPath` 지정.
- 관련 코드: Server/Program.cs(진단용 미들웨어), AccountController의 파일 로그.

## 설정 예시 (내부망용)
```json
"AuthCookies": {
  "RelaxForIpClients": true
},
"AuthLogging": {
  "LoginLogPath": "C:/keys/log",
  "TraceCookies": true
}
```

## 테스트 체크리스트
1) 서버/IIS 재시작, 브라우저 쿠키/캐시 삭제.
2) 로그인 시 네트워크 탭 확인
   - LoginApi 응답 헤더에 `Set-Cookie: .WicsPlatform.Identity=...` 확인.
   - 이어지는 첫 보호 페이지 요청에 `Cookie: .WicsPlatform.Identity=...` 포함되는지 확인.
3) Application > Cookies에서 `.WicsPlatform.Identity`가 생성되었는지 확인.
4) 여전히 루프면 Trace 로그에서 `SET-COOKIE`와 다음 요청의 `COOKIE`를 비교.

## FAQ
- Q) 왜 브라우저를 껐다 켜면 자동 로그인되나요?
  - A) 과거에 저장된 유효 쿠키가 남아 있어 첫 접속은 인증됨. 그러나 로그아웃 후 재로그인 시 새 쿠키 저장이 거부되면 루프가 발생했습니다. 현재 완화/흐름 변경으로 해결됨.

- Q) 404 (WicsPlatform.Client.styles.css, favicon.png)가 보이는데 관련 있나요?
  - A) 로그인 문제와 무관하며, 필요 시 정적 리소스를 추가하면 사라집니다.

## 현재 상태
- 위 변경 적용 후 내부망 원격 접속에서 로그인 루프가 재현되지 않는 것을 확인.
- 보안 상 이유로 운영 환경에는 `RelaxForIpClients=false`를 권장하며, 내부 DNS/신뢰 인증서 구성이 가능해지면 원복하세요.
