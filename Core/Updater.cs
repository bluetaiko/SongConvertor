using System.Net.Http;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace SongConverter.Core;

public class Updater
{
#if TEST
    private const string GitHubApiUrl = "https://api.github.com/repos/bluetaiko/SongConvertor/releases";
#else
    private const string GitHubApiUrl = "https://api.github.com/repos/bluetaiko/SongConvertor/releases/latest";
#endif
    private static readonly HttpClient _httpClient = new HttpClient();

    static Updater()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SongConverter-Updater");
    }

    public static async Task<(bool hasUpdate, string? latestVersion, string? downloadUrl, string currentVersion, string debugInfo)> CheckForUpdateAsync()
    {
        string debugInfo = "";
        try
        {
            var response = await _httpClient.GetStringAsync(GitHubApiUrl);
            using var json = System.Text.Json.JsonDocument.Parse(response);

            var root = json.RootElement;

#if TEST
            // Testビルドの場合は最新のリリースを探す
            System.Text.Json.JsonElement releaseToUse = default;
            foreach (var release in root.EnumerateArray())
            {
                releaseToUse = release;
                break; // 最新のリリースを使用
            }
            var rootToUse = releaseToUse;
#else
            var rootToUse = root;
#endif

            string? tagName = rootToUse.GetProperty("tag_name").GetString();
            string? downloadUrl = null;

            // SCvInstaller.exeを探す
            var assets = rootToUse.GetProperty("assets");
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name != null && name.Equals("SCvInstaller.exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(downloadUrl))
            {
                debugInfo = $"tagName: {tagName}, downloadUrl: {downloadUrl}";
                return (false, null, null, "", debugInfo);
            }

            var currentVersion = GetCurrentVersion();
            var isNewer = IsNewerVersion(currentVersion, tagName);
            
            // 正規化後のバージョンもデバッグ表示
            var currentNormalized = currentVersion.Trim().TrimStart('v', 'V');
            var latestNormalized = tagName.Trim().TrimStart('v', 'V');
            debugInfo = $"Current: {currentVersion} ({currentNormalized}), Latest: {tagName} ({latestNormalized}), IsNewer: {isNewer}";

            return (isNewer, tagName, downloadUrl, currentVersion, debugInfo);
        }
        catch (Exception ex)
        {
            debugInfo = $"Exception: {ex.Message}";
            return (false, null, null, "", debugInfo);
        }
    }

    public static async Task<bool> DownloadAndUpdateAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SongConverter_Update");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            string installerPath = Path.Combine(tempDir, "SCvInstaller.exe");

            // インストーラーをダウンロード
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? 0;

                using (var fs = File.Create(installerPath))
                using (var stream = await response.Content.ReadAsStreamAsync(ct))
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    long totalRead = 0;

                    while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        totalRead += bytesRead;
                        if (totalBytes > 0)
                        {
                            progress?.Report((int)((totalRead * 100) / totalBytes));
                        }
                    }
                }
            }

            // インストーラーを起動
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });

            // 自分自身を終了
            var currentProcess = Process.GetCurrentProcess();
            Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/F /PID {currentProcess.Id}",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version == null) return "0.0.0";
        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private static bool IsNewerVersion(string current, string latest)
    {
        try
        {
            // バージョン文字列を正規化（先頭の 'v' または 'V' を削除、空白をトリム）
            var currentNormalized = current.Trim().TrimStart('v', 'V');
            var latestNormalized = latest.Trim().TrimStart('v', 'V');
            
            var currentVer = new Version(currentNormalized);
            var latestVer = new Version(latestNormalized);
            
            var result = latestVer > currentVer;
            
            // デバッグ用に内部情報も追加（必要に応じて）
            // debugInfoには渡せないので、ここでは比較だけを正しく行う
            return result;
        }
        catch
        {
            // バージョン解析に失敗した場合は false を返す
            return false;
        }
    }
}
