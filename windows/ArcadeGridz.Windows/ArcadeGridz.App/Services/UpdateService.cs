using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ArcadeGridz.App.Services;

public sealed record UpdateInfo(Version Version, Uri InstallerUri, string Sha256, string ReleasePage);
public sealed record UpdateCheckResult(Version InstalledVersion, UpdateInfo? Update, string Status);

public sealed class UpdateService
{
    const string LatestReleaseApi = "https://api.github.com/repos/GRiDDiM/arcadegridz/releases/latest";
    const string ManifestAssetName = "update-manifest.json";
    readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public UpdateService()
    {
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ArcadeGridz-Windows", InstalledVersion.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public static Version InstalledVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } value
            ? new Version(value.Major, value.Minor, Math.Max(0, value.Build))
            : new Version(0, 0, 0);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var releaseResponse = await http.GetAsync(LatestReleaseApi, cancellationToken);
        if (releaseResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("The official update feed is not publicly available yet.");
        releaseResponse.EnsureSuccessStatusCode();

        using var releaseJson = JsonDocument.Parse(await releaseResponse.Content.ReadAsStreamAsync(cancellationToken));
        var root = releaseJson.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            return new(InstalledVersion, null, "The latest release is still a draft.");

        var releasePage = root.GetProperty("html_url").GetString() ?? "https://github.com/GRiDDiM/arcadegridz/releases";
        var assets = root.GetProperty("assets").EnumerateArray();
        Uri? manifestUri = null;
        foreach (var asset in assets)
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), ManifestAssetName, StringComparison.OrdinalIgnoreCase))
                continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed)) manifestUri = parsed;
            break;
        }
        if (manifestUri is null) throw new InvalidOperationException("The latest release has no update manifest.");
        EnsureTrustedDownloadUri(manifestUri);

        using var manifestResponse = await http.GetAsync(manifestUri, cancellationToken);
        manifestResponse.EnsureSuccessStatusCode();
        using var manifestJson = JsonDocument.Parse(await manifestResponse.Content.ReadAsStreamAsync(cancellationToken));
        var manifest = manifestJson.RootElement;

        if (manifest.GetProperty("schema").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported update manifest schema.");

        var versionText = manifest.GetProperty("version").GetString();
        if (!Version.TryParse(versionText, out var available))
            throw new InvalidOperationException("The update manifest contains an invalid version.");
        if (available <= InstalledVersion)
            return new(InstalledVersion, null, $"ArcadeGridz {InstalledVersion} is up to date.");

        var installerText = manifest.GetProperty("installerUrl").GetString();
        if (!Uri.TryCreate(installerText, UriKind.Absolute, out var installerUri))
            throw new InvalidOperationException("The update manifest contains an invalid installer URL.");
        EnsureTrustedDownloadUri(installerUri);

        var sha256 = (manifest.GetProperty("sha256").GetString() ?? "").Trim().ToLowerInvariant();
        if (sha256.Length != 64 || sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("The update manifest contains an invalid SHA-256 hash.");

        return new(InstalledVersion, new UpdateInfo(available, installerUri, sha256, releasePage),
            $"ArcadeGridz {available} is available.");
    }

    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(AppPaths.Root, "updates", update.Version.ToString());
        Directory.CreateDirectory(updateDirectory);
        var destination = Path.Combine(updateDirectory, "ArcadeGridz-Setup-x64.exe");
        var temporary = destination + ".download";

        using var response = await http.GetAsync(update.InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
        {
            var buffer = new byte[1024 * 128];
            long received = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                received += count;
                if (total > 0) progress?.Report((double)received / total.Value);
            }
        }

        await using (var stream = File.OpenRead(temporary))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actual), Convert.FromHexString(update.Sha256)))
            {
                File.Delete(temporary);
                throw new InvalidDataException("The installer failed SHA-256 verification and was deleted.");
            }
        }

        File.Move(temporary, destination, true);
        return destination;
    }

    public static void InstallAndRestart(string installerPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!
        };
        _ = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start the verified installer.");
    }

    static void EnsureTrustedDownloadUri(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update downloads must use HTTPS.");
        var host = uri.Host.ToLowerInvariant();
        if (host != "github.com" &&
            host != "objects.githubusercontent.com" &&
            !host.EndsWith(".githubusercontent.com", StringComparison.Ordinal))
            throw new InvalidOperationException($"Untrusted update host: {uri.Host}");
    }
}
