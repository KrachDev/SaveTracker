using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class RcloneInstaller
    {
        private readonly RcloneExecutor _executor = new RcloneExecutor();

        private SaveTrackerSettingsViewModel UploaderSettings { get; set; }
        private readonly RcloneConfigManager _rcloneConfigManager = new RcloneConfigManager();

        public RcloneInstaller(SaveTrackerSettingsViewModel settingsView)
        {
            UploaderSettings = settingsView;
        }
        private static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");
        private static readonly string ToolsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools"
        );
        private readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");
        private async Task<string> GetLatestRcloneZipUrl()
        {
            DebugConsole.WriteSection("Getting Latest Rclone URL");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SaveTracker-Rclone-Updater/1.0");
                client.Timeout = TimeSpan.FromSeconds(30);

                DebugConsole.WriteInfo("Fetching GitHub releases API...");
                var json = await client.GetStringAsync(
                    "https://api.github.com/repos/rclone/rclone/releases/latest"
                );

                JObject root = JObject.Parse(json);

                // Get tag_name
                var version = root["tag_name"]?.ToString();
                DebugConsole.WriteInfo($"Latest version found: {version}");

                // Iterate assets array
                if (root["assets"] is JArray assets)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString();
                        if (name != null && name.Contains("windows-amd64.zip"))
                        {
                            var url = asset["browser_download_url"]?.ToString();
                            DebugConsole.WriteSuccess($"Found download URL: {url}");
                            return url;
                        }
                    }
                }

                DebugConsole.WriteError("No windows-amd64.zip asset found in release");
                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to get latest Rclone URL");
                return null;
            }
        }

        public async Task RcloneCheckAsync(IPlayniteAPI api, CloudProvider provider)
        {
            DebugConsole.WriteSection("Rclone Setup Check");

            try
            {
                if (_rcloneConfigManager == null)
                {
                    DebugConsole.WriteError("_rcloneConfigManager is null.");
                    return;
                }

                if (UploaderSettings?.Settings == null)
                {
                    DebugConsole.WriteError("UploaderSettings.Settings is null.");
                    return;
                }

                DebugConsole.WriteKeyValue("Rclone Path", RcloneExePath);
                DebugConsole.WriteKeyValue("Config Path", _configPath);
                DebugConsole.WriteKeyValue("Tools Directory", ToolsPath);
                DebugConsole.WriteKeyValue("Selected Provider", provider.ToString());

                if (string.IsNullOrWhiteSpace(RcloneExePath) || !File.Exists(RcloneExePath))
                {
                    DebugConsole.WriteWarning(
                        "Rclone executable not found, initiating download..."
                    );
                    await DownloadAndInstallRclone(api);
                }
                else
                {
                    DebugConsole.WriteSuccess("Rclone executable found");
                    var version = await GetRcloneVersion();
                    DebugConsole.WriteInfo($"Rclone version: {version}");
                }

                if (
                    string.IsNullOrWhiteSpace(_configPath)
                    || !File.Exists(_configPath)
                    || !await _rcloneConfigManager.IsValidConfig(_configPath, provider)
                )
                {
                    DebugConsole.WriteWarning("Rclone config invalid or missing, setting up...");
                    await _rcloneConfigManager.CreateConfig(api, provider);
                }
                else
                {
                    DebugConsole.WriteSuccess("Rclone configuration is valid");
                    await _rcloneConfigManager.TestConnection(api, provider);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Rclone setup failed");
                api.Notifications.Add(
                    new NotificationMessage(
                        "RCLONE_ERROR",
                        $"Error setting up Rclone: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
        }
        private async Task DownloadAndInstallRclone(IPlayniteAPI api)
        {
            DebugConsole.WriteSection("Downloading Rclone");

            try
            {
                api.Notifications.Add(
                    new NotificationMessage(
                        "RCLONE_DOWNLOAD",
                        "Downloading Rclone...",
                        NotificationType.Info
                    )
                );

                if (!Directory.Exists(ToolsPath))
                {
                    Directory.CreateDirectory(ToolsPath);
                    DebugConsole.WriteInfo($"Created tools directory: {ToolsPath}");
                }

                var downloadUrl = await GetLatestRcloneZipUrl();
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("Could not determine Rclone download URL");
                }

                string zipPath = Path.Combine(
                    ToolsPath,
                    $"rclone_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                );
                DebugConsole.WriteInfo($"Downloading to: {zipPath}");

                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (s, e) =>
                    {
                        if (e.ProgressPercentage % 10 == 0)
                        {
                            DebugConsole.WriteDebug(
                                $"Download progress: {e.ProgressPercentage}% ({e.BytesReceived:N0}/{e.TotalBytesToReceive:N0} bytes)"
                            );
                        }
                    };

                    await client.DownloadFileTaskAsync(downloadUrl, zipPath);
                }

                DebugConsole.WriteSuccess(
                    $"Download completed: {new FileInfo(zipPath).Length:N0} bytes"
                );

                api.Notifications.Add(
                    new NotificationMessage(
                        "RCLONE_EXTRACT",
                        "Extracting Rclone...",
                        NotificationType.Info
                    )
                );
                await ExtractRclone(zipPath);

                File.Delete(zipPath);
                DebugConsole.WriteInfo("Cleanup: Deleted temporary zip file");

                api.Notifications.Add(
                    new NotificationMessage(
                        "RCLONE_READY",
                        "Rclone installation completed successfully",
                        NotificationType.Info
                    )
                );
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Rclone download/installation failed");
                throw;
            }
        }

        private Task ExtractRclone(string zipPath)
        {
            DebugConsole.WriteInfo("Extracting Rclone archive...");

            try
            {
                string tempExtractPath = Path.Combine(
                    ToolsPath,
                    $"temp_extract_{DateTime.Now:yyyyMMdd_HHmmss}"
                );
                Directory.CreateDirectory(tempExtractPath);

                ZipFile.ExtractToDirectory(zipPath, tempExtractPath);
                DebugConsole.WriteDebug($"Extracted to temporary directory: {tempExtractPath}");

                var extractedFolders = Directory.GetDirectories(tempExtractPath);
                DebugConsole.WriteList(
                    "Extracted folders",
                    extractedFolders.Select(Path.GetFileName)
                );

                var rcloneFolder = extractedFolders.FirstOrDefault(
                    d => d.Contains("windows-amd64")
                );
                if (rcloneFolder == null)
                {
                    throw new Exception(
                        $"Could not find rclone folder in extracted files. Found: {string.Join(", ", extractedFolders.Select(Path.GetFileName))}"
                    );
                }

                string rcloneExeSource = Path.Combine(rcloneFolder, "rclone.exe");
                if (!File.Exists(rcloneExeSource))
                {
                    throw new Exception($"rclone.exe not found in {rcloneFolder}");
                }

                File.Copy(rcloneExeSource, RcloneExePath, true);
                DebugConsole.WriteSuccess($"Rclone executable copied to: {RcloneExePath}");

                Directory.Delete(tempExtractPath, true);
                DebugConsole.WriteDebug("Cleanup: Deleted temporary extraction directory");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Rclone extraction failed");
                throw;
            }

            return Task.CompletedTask;
        }

        private async Task<string> GetRcloneVersion()
        {
            try
            {
                var result = await _executor.ExecuteRcloneCommand(
                    "version --check=false",
                    TimeSpan.FromSeconds(10)
                );
                if (result.Success)
                {
                    var lines = result.Output.Split('\n');
                    var versionLine = lines.FirstOrDefault(l => l.StartsWith("rclone v"));
                    return versionLine?.Trim() ?? "Unknown";
                }
                return "Version check failed";
            }
            catch
            {
                return "Version unavailable";
            }
        }
    }
}
