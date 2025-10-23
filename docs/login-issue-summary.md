# ���θ� ���� �� �α��� ����(��� �α��� ȭ������ ���ư�) ���ΰ� �ذ� ����

�� ������ ���θ�(IP/��ü���� ������) ȯ�濡�� �߻��ϴ� "�α��� ���� �� ��ȣ ������ ���� �� �ٽ� �α��� �������� �����̷�Ʈ�Ǵ�" ������ �����մϴ�.

## ���� ���
- �α׾ƿ� �� ��� ���� ������(/manage-broad-cast)�� �̵��ϸ� �α��� �������� �����̷�Ʈ��.
- �α����ϸ� ���� �α׿��� SUCCESS �� /manage-broad-cast�� redirect ǥ�������, Ŭ���̾�Ʈ�� �ٽ� /Login���� ���ư� ���� �߻�.
- �������� ������ ���� �� �������ϸ� �ڵ� �α��� ���°� �Ǵ� ��찡 ����.

## �α� �ٰ� (���)
- POST /Account/Login �Ǵ� /Account/LoginApi �� 200/302 �� SUCCESS.
- ���� GET /manage-broad-cast �� 302 /Login.
- ��� �ڵ����� ��û�� ���� ��Ű(.WicsPlatform.Identity)�� ÷�ε��� ���� �� ������ ���������� �����ϰ� /Login���� ����.

## ����
1) ������ ��Ű ��å + IP/�̽ŷ� HTTPS ȯ��
   - SameSite=None, Secure=Always ������ IP/��ü���� ������, Ȥ�� HTTP���� �������� ��Ű ������ �ź��� �� ����.
   - ��������� Set-Cookie�� ���������� �������� ����/�������� �ʾ� ������ �߻�.

2) SSR(���� ��������) ������ ���� üũ
   - ��ȣ �������� �������� ���� ���������� �� ��Ű�� ���� ������� ������ [Authorize]�� �ɷ� 302 /Login���� �����̷�Ʈ.
   - ��Ű ���� Ÿ�̹�/���� ���� + ������������ ���յǸ� ������ ���.

## ������ �ذ�å
### 1. ���θ��� ��Ű ��å ��ȭ ����ġ �߰�
- ���� Ű: `AuthCookies:RelaxForIpClients` (Server/appsettings*.json)
- ����
  - true�� ��: Identity ��Ű SameSite=Lax, Secure=None�� ��ȭ.
  - false�� ��: �⺻ ���� ���� ����(SameSite=None, Secure=Always).
- ���� �ڵ�: Server/Program.cs (ConfigureApplicationCookie, CookiePolicyOptions)

### 2. �߸��� ���� Ű ����
- ���� ��Ÿ: `AuthCookies:����` �� �ùٸ� Ű: `AuthCookies:RelaxForIpClients`.

### 3. �α׾ƿ� �� ��Ű ���� ����
- SignInManager.SignOutAsync() �� `.WicsPlatform.Identity`�� ��������� `Response.Cookies.Delete(...)` ó��.
- ���� ����: Server/Controllers/AccountController.cs

### 4. �α��� �帧 ����(302 �� API ���)
- ����: `POST /Account/LoginApi` �߰�. ���� �� JSON `{ success, redirectUrl }` ��ȯ�ϸ� ��Ű�� ����(302 ����).
- Ŭ���̾�Ʈ: Login �������� `HttpClient.PostAsJsonAsync("Account/LoginApi")`�� �α��� �� `NavigationManager.NavigateTo(..., forceLoad:true)` �̵�.
- 302 ��� ���û Ÿ�ֿ̹��� ��Ű�� �����Ǵ� ������ ȸ��.
- ���� ����: 
  - Server/Controllers/AccountController.cs (LoginApi)
  - Client/Pages/Login.razor, Login.razor.cs

### 5. ���� �������� ��Ȱ��ȭ
- SSR �ܰ迡�� [Authorize]�� 302�� ���� �߻����� �ʵ��� ���������� ��.
- ����: Server/Components/App.razor
  - `<HeadOutlet @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />`
  - `<Routes @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />`
- ����: Ŭ���̾�Ʈ ������(.razor)���� @rendermode�� ���� ����(������ ���� ����).

### 6. ���� ���� ���� ���� ����(����)
- RedirectToLogin���� `AuthenticationStateProvider.GetAuthenticationStateAsync()`�� ���� ���� ���¸� �� �� �� Ȯ���ϵ��� ����(���� ���̽� ��ȭ).

### 7. ��Ű Ʈ���̽�(�ɼ�)
- ���� ���� �� ��û Cookie/���� Set-Cookie�� ���Ϸ� ����� ���� Ȯ��.
- Ȱ��ȭ: `AuthLogging.TraceCookies=true` + `AuthLogging.LoginLogPath` ����.
- ���� �ڵ�: Server/Program.cs(���ܿ� �̵����), AccountController�� ���� �α�.

## ���� ���� (���θ���)
```json
"AuthCookies": {
  "RelaxForIpClients": true
},
"AuthLogging": {
  "LoginLogPath": "C:/keys/log",
  "TraceCookies": true
}
```

## �׽�Ʈ üũ����Ʈ
1) ����/IIS �����, ������ ��Ű/ĳ�� ����.
2) �α��� �� ��Ʈ��ũ �� Ȯ��
   - LoginApi ���� ����� `Set-Cookie: .WicsPlatform.Identity=...` Ȯ��.
   - �̾����� ù ��ȣ ������ ��û�� `Cookie: .WicsPlatform.Identity=...` ���ԵǴ��� Ȯ��.
3) Application > Cookies���� `.WicsPlatform.Identity`�� �����Ǿ����� Ȯ��.
4) ������ ������ Trace �α׿��� `SET-COOKIE`�� ���� ��û�� `COOKIE`�� ��.

## FAQ
- Q) �� �������� ���� �Ѹ� �ڵ� �α��εǳ���?
  - A) ���ſ� ����� ��ȿ ��Ű�� ���� �־� ù ������ ������. �׷��� �α׾ƿ� �� ��α��� �� �� ��Ű ������ �źεǸ� ������ �߻��߽��ϴ�. ���� ��ȭ/�帧 �������� �ذ��.

- Q) 404 (WicsPlatform.Client.styles.css, favicon.png)�� ���̴µ� ���� �ֳ���?
  - A) �α��� ������ �����ϸ�, �ʿ� �� ���� ���ҽ��� �߰��ϸ� ������ϴ�.

## ���� ����
- �� ���� ���� �� ���θ� ���� ���ӿ��� �α��� ������ �������� �ʴ� ���� Ȯ��.
- ���� �� ������ � ȯ�濡�� `RelaxForIpClients=false`�� �����ϸ�, ���� DNS/�ŷ� ������ ������ ���������� �����ϼ���.
