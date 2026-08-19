using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Google 리프레시 토큰 발급 도구. Menu: IUM > AI > Google 리프레시 토큰 발급.
///
/// 리프레시 토큰은 값을 어디서 복사해 오는 것이 아니라 사용자가 브라우저에서 동의해야 나온다.
/// 그래서 이 도구가 필요하다 — 브라우저를 열고, 로컬 루프백으로 인가 코드를 받아, 토큰으로
/// 교환한 뒤 <c>ai_secrets.json</c>에 적는다.
///
/// 데스크톱 클라이언트라 루프백 리디렉션을 쓴다. 등록된 redirect_uri가 <c>http://localhost</c>
/// 하나뿐이어도 Google은 루프백에 한해 임의 포트를 허용하므로, 매번 비어 있는 포트를 잡는다.
///
/// PKCE(S256)를 함께 보낸다. 데스크톱 클라이언트에 Google이 권장하는 방식이고, client_secret과
/// 같이 보내도 문제가 없다.
///
/// 에디터 전용이다. 발급된 토큰만 파일에 남고 빌드에는 이 코드가 들어가지 않는다.
/// </summary>
public static class GoogleOAuthSetup
{
    /// <summary>TTS와 STT 모두를 덮는다. GoogleServiceAccountTokenProvider와 같은 스코프다.</summary>
    const string Scope = "https://www.googleapis.com/auth/cloud-platform";

    const string SecretsFileName = "ai_secrets.json";
    const string SecretsField = "googleRefreshToken";
    const int TimeoutSeconds = 180;

    [MenuItem("IUM/AI/Google 리프레시 토큰 발급")]
    public static void Run()
    {
        var clientPath = Path.Combine(Application.streamingAssetsPath, AiConfigLoader.OAuthClientFileName);
        if (!File.Exists(clientPath))
        {
            EditorUtility.DisplayDialog(
                "Google 토큰 발급",
                $"{AiConfigLoader.OAuthClientFileName}을 찾을 수 없습니다.\n\n{clientPath}",
                "확인");
            return;
        }

        GoogleOAuthClient client;
        try
        {
            client = new JsonDataSerializer().Deserialize<GoogleOAuthClient>(File.ReadAllText(clientPath));
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("Google 토큰 발급", $"클라이언트 파일을 읽지 못했습니다.\n\n{exception.Message}", "확인");
            return;
        }

        if (client?.Detail == null || !client.IsUsable)
        {
            EditorUtility.DisplayDialog(
                "Google 토큰 발급",
                "클라이언트 파일에 client_id 또는 client_secret이 없습니다.",
                "확인");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Google 토큰 발급",
                "브라우저가 열립니다. Google 계정으로 로그인하고 동의하십시오.\n\n" +
                "동의 화면이 '테스트' 모드이면 발급된 토큰이 7일 뒤 다시 만료됩니다. " +
                "OAuth 동의 화면을 '프로덕션'으로 게시한 뒤 발급하시는 편이 좋습니다.",
                "계속", "취소"))
            return;

        _ = AuthorizeAsync(client);
    }

    static async Task AuthorizeAsync(GoogleOAuthClient client)
    {
        var detail = client.Detail;
        HttpListener listener = null;

        try
        {
            var port = FindFreePort();
            var redirectUri = $"http://localhost:{port}/";

            listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            var verifier = CreateCodeVerifier();
            var state = CreateCodeVerifier();

            var authUrl =
                $"{detail.AuthUri}" +
                $"?client_id={UnityWebRequest.EscapeURL(detail.ClientId)}" +
                $"&redirect_uri={UnityWebRequest.EscapeURL(redirectUri)}" +
                "&response_type=code" +
                $"&scope={UnityWebRequest.EscapeURL(Scope)}" +
                "&access_type=offline" +

                // 동의를 매번 다시 받는다. 이미 승인한 계정은 refresh_token을 한 번만 주므로,
                // 이것이 없으면 재발급 시 응답에 refresh_token이 빠진다.
                "&prompt=consent" +
                $"&code_challenge={CreateChallenge(verifier)}" +
                "&code_challenge_method=S256" +
                $"&state={state}";

            Application.OpenURL(authUrl);

            var code = await WaitForCodeAsync(listener, state);
            if (string.IsNullOrEmpty(code)) return;

            EditorUtility.DisplayProgressBar("Google 토큰 발급", "토큰으로 교환하는 중...", 0.7f);

            var form =
                "grant_type=authorization_code" +
                $"&code={UnityWebRequest.EscapeURL(code)}" +
                $"&client_id={UnityWebRequest.EscapeURL(detail.ClientId)}" +
                $"&client_secret={UnityWebRequest.EscapeURL(detail.ClientSecret)}" +
                $"&redirect_uri={UnityWebRequest.EscapeURL(redirectUri)}" +
                $"&code_verifier={UnityWebRequest.EscapeURL(verifier)}";

            var response = await AiHttp.PostFormAsync(detail.TokenUri, form, null, 30f, CancellationToken.None);
            EditorUtility.ClearProgressBar();

            if (!response.Success)
            {
                // 응답 본문에 토큰은 없고 오류 코드만 있다. 그대로 보여 주는 편이 진단에 낫다.
                Fail($"토큰 교환에 실패했습니다 ({response.StatusCode}).\n\n{response.Text}");
                return;
            }

            var refreshToken = JObject.Parse(response.Text)["refresh_token"]?.ToString();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                Fail(
                    "응답에 refresh_token이 없습니다.\n\n" +
                    "이미 승인한 계정은 재동의 없이는 발급되지 않습니다. " +
                    "Google 계정 > 보안 > 서드파티 앱에서 이 앱의 권한을 제거한 뒤 다시 시도하십시오.");
                return;
            }

            WriteRefreshToken(refreshToken);
        }
        catch (Exception exception)
        {
            EditorUtility.ClearProgressBar();
            Fail(exception.Message);
        }
        finally
        {
            if (listener != null && listener.IsListening) listener.Stop();
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>브라우저가 리디렉션해 올 때까지 기다린다. 사용자가 창을 닫으면 시간 초과로 끝난다.</summary>
    static async Task<string> WaitForCodeAsync(HttpListener listener, string expectedState)
    {
        var contextTask = listener.GetContextAsync();
        var completed = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds)));

        if (completed != contextTask)
        {
            Fail($"{TimeoutSeconds}초 안에 응답이 오지 않아 취소했습니다.");
            return null;
        }

        var context = await contextTask;
        var query = context.Request.QueryString;
        var code = query["code"];
        var error = query["error"];
        var state = query["state"];

        Respond(context, string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(code));

        if (!string.IsNullOrEmpty(error))
        {
            Fail($"동의가 거부되었습니다: {error}");
            return null;
        }

        // 다른 곳에서 온 요청을 코드로 오인하지 않도록 확인한다.
        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            Fail("state 값이 일치하지 않아 응답을 신뢰할 수 없습니다. 다시 시도하십시오.");
            return null;
        }

        if (string.IsNullOrEmpty(code))
        {
            Fail("인가 코드를 받지 못했습니다.");
            return null;
        }

        return code;
    }

    static void Respond(HttpListenerContext context, bool success)
    {
        var message = success
            ? "발급이 끝났습니다. Unity로 돌아가십시오."
            : "발급에 실패했습니다. Unity의 안내를 확인하십시오.";

        var body = Encoding.UTF8.GetBytes(
            "<html><head><meta charset=\"utf-8\"></head>" +
            $"<body style=\"font-family:sans-serif;padding:40px\"><h3>{message}</h3></body></html>");

        try
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.OutputStream.Close();
        }
        catch (Exception exception)
        {
            // 브라우저가 이미 끊었을 수 있다. 발급 자체와는 무관하다.
            Debug.LogWarning($"[OAuth] 브라우저 응답에 실패했습니다: {exception.Message}");
        }
    }

    /// <summary>
    /// 다른 값은 건드리지 않고 리프레시 토큰만 갈아 끼운다. JObject로 다루는 이유는 모델에 없는
    /// 필드가 파일에 있어도 잃지 않기 위해서다.
    /// </summary>
    static void WriteRefreshToken(string refreshToken)
    {
        var path = Path.Combine(Application.streamingAssetsPath, SecretsFileName);

        JObject root;
        try
        {
            root = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
        }
        catch (Exception exception)
        {
            Fail($"{SecretsFileName}을 읽지 못했습니다.\n\n{exception.Message}");
            return;
        }

        root[SecretsField] = refreshToken;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.streamingAssetsPath);
            File.WriteAllText(path, root.ToString());
        }
        catch (Exception exception)
        {
            Fail($"{SecretsFileName}에 쓰지 못했습니다.\n\n{exception.Message}");
            return;
        }

        AssetDatabase.Refresh();

        // 토큰 값은 로그에 남기지 않는다. 콘솔은 공유되기 쉽다.
        Debug.Log($"[OAuth] 리프레시 토큰을 {SecretsFileName}에 저장했습니다.");
        EditorUtility.DisplayDialog(
            "Google 토큰 발급",
            $"발급이 끝났습니다. {SecretsFileName}의 {SecretsField}를 갱신했습니다.\n\n" +
            "플레이를 다시 시작하면 대사 음성이 나옵니다.",
            "확인");
    }

    static int FindFreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    static string CreateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64Url(bytes);
    }

    static string CreateChallenge(string verifier)
    {
        using var sha = SHA256.Create();
        return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static void Fail(string message)
    {
        Debug.LogError($"[OAuth] {message}");
        EditorUtility.DisplayDialog("Google 토큰 발급", message, "확인");
    }
}
