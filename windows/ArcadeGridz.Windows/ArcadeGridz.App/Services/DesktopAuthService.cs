using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ArcadeGridz.App.Services;

public sealed record DesktopAuthResult(string Token, string AccountName);

public sealed class DesktopAuthService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    sealed record ExchangeRequest(string Code, string Verifier, string Callback);
    sealed record ExchangeAccount(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);
    sealed record ExchangeResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("account")] ExchangeAccount? Account,
        [property: JsonPropertyName("error")] string? Error);

    public async Task<DesktopAuthResult> SignInWithGoogleAsync(
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var callback = $"http://127.0.0.1:{port}/callback";
            var authorize = "https://arcadegridz.com/?host=winapp&mode=desktop-signin"
                + $"&challenge={Uri.EscapeDataString(challenge)}"
                + $"&callback={Uri.EscapeDataString(callback)}"
                + $"&state={Uri.EscapeDataString(state)}";

            status?.Invoke("OPENING GOOGLE SIGN-IN IN YOUR BROWSER...");
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(authorize)))
                throw new InvalidOperationException("Windows could not open the system browser.");

            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IPAddress.IsLoopback(remote.Address))
                throw new InvalidOperationException("Rejected a non-loopback sign-in response.");

            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 2048, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(timeout.Token);
            var parts = requestLine?.Split(' ', 3) ?? [];
            var target = parts.Length == 3 && parts[0] == "GET" ? parts[1] : "";
            string? line;
            do { line = await reader.ReadLineAsync(timeout.Token); } while (!string.IsNullOrEmpty(line));

            var query = ParseQuery(new Uri($"http://127.0.0.1:{port}{target}").Query);
            if (!query.TryGetValue("state", out var returnedState) || returnedState != state)
                throw new InvalidOperationException("The Google sign-in state did not match.");
            if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Google sign-in did not return an authorization code.");

            await WriteBrowserResponseAsync(stream, true, timeout.Token);
            status?.Invoke("VERIFYING SECURE SIGN-IN...");

            using var response = await Http.PostAsJsonAsync(
                "https://arcadegridz.com/api/winapp/auth/exchange",
                new ExchangeRequest(code, verifier, callback),
                timeout.Token);
            var result = await response.Content.ReadFromJsonAsync<ExchangeResponse>(cancellationToken: timeout.Token);
            if (!response.IsSuccessStatusCode || result?.Ok != true || string.IsNullOrWhiteSpace(result.Token))
                throw new InvalidOperationException(result?.Error ?? "The desktop authorization exchange failed.");

            StartupLog.Write("auth", $"System-browser sign-in completed for account={result.Account?.Id}");
            return new DesktopAuthResult(result.Token, result.Account?.Name ?? "GRIDRUNNER");
        }
        catch
        {
            throw;
        }
        finally
        {
            listener.Stop();
        }
    }

    static async Task WriteBrowserResponseAsync(NetworkStream stream, bool success, CancellationToken cancellationToken)
    {
        var heading = success ? "YOU'RE SIGNED IN" : "SIGN-IN FAILED";
        var body = success
            ? "ArcadeGridz received the secure sign-in. You can close this tab and return to the app."
            : "Return to ArcadeGridz and try again.";
        var html = $"<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width'><title>ArcadeGridz</title><style>html{{color-scheme:dark}}body{{margin:0;min-height:100vh;display:grid;place-items:center;background:#050313;color:#fff;font-family:Consolas,monospace}}main{{max-width:680px;padding:48px;text-align:center;border:3px solid #55e8ff;border-radius:24px;background:#0c0923}}h1{{color:#55e8ff}}p{{line-height:1.6;color:#d7d7eb}}</style><main><h1>{heading}</h1><p>{body}</p></main>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = pair.Split('=', 2);
            result[Uri.UnescapeDataString(pieces[0].Replace("+", " "))] =
                Uri.UnescapeDataString((pieces.Length > 1 ? pieces[1] : "").Replace("+", " "));
        }
        return result;
    }

    static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
