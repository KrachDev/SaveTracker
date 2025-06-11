using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playnite.SDK;
using JsonException = Newtonsoft.Json.JsonException;

namespace SaveTracker
{
    public class RcloneUploader
    {
        public static string RcloneExePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");
        private static readonly string ToolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools");
        private static readonly string ConfigPath = Path.Combine(ToolsPath, "rclone.conf");
        private static readonly int MaxRetries = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(10);

        public static async Task<string> GetLatestRcloneZipUrl()
        {
            DebugConsole.WriteSection("Getting Latest Rclone URL");
            
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SaveTracker-Rclone-Updater/1.0");
                client.Timeout = TimeSpan.FromSeconds(30);

                DebugConsole.WriteInfo("Fetching GitHub releases API...");
                var json = await client.GetStringAsync("https://api.github.com/repos/rclone/rclone/releases/latest");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var version = root.GetProperty("tag_name").GetString();
                DebugConsole.WriteInfo($"Latest version found: {version}");

                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name != null && name.Contains("windows-amd64.zip"))
                    {
                        var url = asset.GetProperty("browser_download_url").GetString();
                        DebugConsole.WriteSuccess($"Found download URL: {url}");
                        return url;
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

        public static async Task RcloneCheckAsync(IPlayniteAPI api)
        {
            DebugConsole.WriteSection("Rclone Setup Check");
            
            try
            {
                DebugConsole.WriteKeyValue("Rclone Path", RcloneExePath);
                DebugConsole.WriteKeyValue("Config Path", ConfigPath);
                DebugConsole.WriteKeyValue("Tools Directory", ToolsPath);

                if (!File.Exists(RcloneExePath))
                {
                    DebugConsole.WriteWarning("Rclone executable not found, initiating download...");
                    await DownloadAndInstallRclone(api);
                }
                else
                {
                    DebugConsole.WriteSuccess("Rclone executable found");
                    var version = await GetRcloneVersion();
                    DebugConsole.WriteInfo($"Rclone version: {version}");
                }

                if (!File.Exists(ConfigPath) || !await IsValidConfig(ConfigPath))
                {
                    DebugConsole.WriteWarning("Rclone config invalid or missing, setting up...");
                    await CreateConfig(api);
                }
                else
                {
                    DebugConsole.WriteSuccess("Rclone configuration is valid");
                    await TestConnection(api);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Rclone setup failed");
                api.Notifications.Add(new NotificationMessage("RCLONE_ERROR", $"Error setting up Rclone: {ex.Message}", NotificationType.Error));
            }
        }

        private static async Task DownloadAndInstallRclone(IPlayniteAPI api)
        {
            DebugConsole.WriteSection("Downloading Rclone");
            
            try
            {
                api.Notifications.Add(new NotificationMessage("RCLONE_DOWNLOAD", "Downloading Rclone...", NotificationType.Info));
                
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

                string zipPath = Path.Combine(ToolsPath, $"rclone_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                DebugConsole.WriteInfo($"Downloading to: {zipPath}");

                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (s, e) => {
                        if (e.ProgressPercentage % 10 == 0)
                        {
                            DebugConsole.WriteDebug($"Download progress: {e.ProgressPercentage}% ({e.BytesReceived:N0}/{e.TotalBytesToReceive:N0} bytes)");
                        }
                    };
                    
                    await client.DownloadFileTaskAsync(downloadUrl, zipPath);
                }

                DebugConsole.WriteSuccess($"Download completed: {new FileInfo(zipPath).Length:N0} bytes");

                api.Notifications.Add(new NotificationMessage("RCLONE_EXTRACT", "Extracting Rclone...", NotificationType.Info));
                await ExtractRclone(zipPath);

                File.Delete(zipPath);
                DebugConsole.WriteInfo("Cleanup: Deleted temporary zip file");

                api.Notifications.Add(new NotificationMessage("RCLONE_READY", "Rclone installation completed successfully", NotificationType.Info));
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Rclone download/installation failed");
                throw;
            }
        }

        private static async Task ExtractRclone(string zipPath)
        {
            DebugConsole.WriteInfo("Extracting Rclone archive...");
            
            try
            {
                string tempExtractPath = Path.Combine(ToolsPath, $"temp_extract_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(tempExtractPath);

                ZipFile.ExtractToDirectory(zipPath, tempExtractPath);
                DebugConsole.WriteDebug($"Extracted to temporary directory: {tempExtractPath}");

                var extractedFolders = Directory.GetDirectories(tempExtractPath);
                DebugConsole.WriteList("Extracted folders", extractedFolders.Select(Path.GetFileName));

                var rcloneFolder = extractedFolders.FirstOrDefault(d => d.Contains("windows-amd64"));
                if (rcloneFolder == null)
                {
                    throw new Exception($"Could not find rclone folder in extracted files. Found: {string.Join(", ", extractedFolders.Select(Path.GetFileName))}");
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
        }

        private static async Task<string> GetRcloneVersion()
        {
            try
            {
                var result = await ExecuteRcloneCommand("version --check=false", TimeSpan.FromSeconds(10));
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

        private static async Task<bool> IsValidConfig(string path)
        {
            DebugConsole.WriteInfo($"Validating config file: {path}");
            
            try
            {
                if (!File.Exists(path))
                {
                    DebugConsole.WriteWarning("Config file does not exist");
                    return false;
                }

                string content =  File.ReadAllText(path);
                DebugConsole.WriteDebug($"Config file size: {content.Length} characters");

                if (!content.Contains("[gdrive]"))
                {
                    DebugConsole.WriteWarning("Config missing [gdrive] section");
                    return false;
                }

                if (!content.Contains("type = drive"))
                {
                    DebugConsole.WriteWarning("Config missing 'type = drive' setting");
                    return false;
                }

                var tokenMatch = Regex.Match(content, @"token\s*=\s*(.+)");
                if (!tokenMatch.Success)
                {
                    DebugConsole.WriteWarning("Config missing or invalid token");
                    return false;
                }

                try
                {
                    JsonConvert.DeserializeObject(tokenMatch.Groups[1].Value.Trim());
                    DebugConsole.WriteSuccess("Config validation passed");
                    return true;
                }
                catch (JsonException)
                {
                    DebugConsole.WriteWarning("Config token is not valid JSON");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Config validation failed");
                return false;
            }
        }

        private static async Task CreateConfig(IPlayniteAPI api)
        {
            DebugConsole.WriteSection("Creating Rclone Config");
            
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string backupPath = $"{ConfigPath}.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Move(ConfigPath, backupPath);
                    DebugConsole.WriteInfo($"Backed up existing config to: {backupPath}");
                }

                var result = await ExecuteRcloneCommand($"config create gdrive drive --config \"{ConfigPath}\"", TimeSpan.FromMinutes(5), false);

                if (result.Success && await IsValidConfig(ConfigPath))
                {
                    DebugConsole.WriteSuccess("Google Drive configuration completed successfully");
                    api.Notifications.Add(new NotificationMessage("RCLONE_CONFIG_OK", "Google Drive is configured.", NotificationType.Info));
                }
                else
                {
                    string errorMsg = $"Rclone config failed. Exit code: {result.ExitCode}, Error: {result.Error}";
                    DebugConsole.WriteError(errorMsg);
                    throw new Exception(errorMsg);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Config creation failed");
                throw;
            }
        }

        private static async Task TestConnection(IPlayniteAPI api)
        {
            DebugConsole.WriteInfo("Testing Google Drive connection...");
            
            try
            {
                var result = await ExecuteRcloneCommand($"lsd gdrive: --max-depth 1 --config \"{ConfigPath}\"", TimeSpan.FromSeconds(30));
                
                if (result.Success)
                {
                    DebugConsole.WriteSuccess("Google Drive connection test passed");
                }
                else
                {
                    DebugConsole.WriteWarning($"Connection test failed: {result.Error}");
                    api.Notifications.Add(new NotificationMessage("RCLONE_CONNECTION_WARNING", 
                        "Rclone configured but connection test failed. Upload may not work properly.", 
                        NotificationType.Error));
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Connection test error");
            }
        }

        public async Task Upload(List<string> SaveFilesList, IPlayniteAPI PlayniteApi, string gameName)
        {
            await RcloneCheckAsync(PlayniteApi);
            var overallStartTime = DateTime.Now;
            DebugConsole.WriteSection($"Upload Process for {gameName}");
            DebugConsole.WriteKeyValue("Process started at", overallStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            DebugConsole.WriteKeyValue("Files to process", SaveFilesList.Count);
            DebugConsole.WriteList("Save files", SaveFilesList.Select(Path.GetFileName));

            if (!File.Exists(RcloneExePath) || !File.Exists(ConfigPath))
            {
                string error = "Rclone is not installed or configured.";
                DebugConsole.WriteError(error);
                PlayniteApi.Notifications.Add(new NotificationMessage("RCLONE_MISSING", error, NotificationType.Error));
                return;
            }

            string remoteBasePath = $"gdrive:PlayniteCloudSave/{SanitizeGameName(gameName)}";
            DebugConsole.WriteKeyValue("Remote base path", remoteBasePath);

            var stats = new UploadStats { StartTime = overallStartTime };
            var validFiles = SaveFilesList.Where(File.Exists).ToList();
            var invalidFiles = SaveFilesList.Except(validFiles).ToList();

            if (invalidFiles.Any())
            {
                DebugConsole.WriteWarning($"Skipping {invalidFiles.Count} missing files:");
                DebugConsole.WriteList("Missing files", invalidFiles.Select(Path.GetFileName));
            }

             PlayniteApi.Dialogs.ActivateGlobalProgress(async (prog) =>
            {
                prog.ProgressMaxValue = validFiles.Count;
                prog.CurrentProgressValue = 0;
                prog.Text = $"Processing save files for {gameName}...";

                foreach (var file in validFiles)
                {
                    await ProcessFile(file, remoteBasePath, prog, stats);
                    prog.CurrentProgressValue++;
                }

                var overallEndTime = DateTime.Now;
                stats.TotalTime = overallEndTime - overallStartTime;
                DebugConsole.WriteKeyValue("Process completed at", overallEndTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));

                ShowUploadResults(PlayniteApi, gameName, stats);

            }, new GlobalProgressOptions($"Uploading {gameName} saves", true));
        }

        private async Task ProcessFile(string filePath, string remoteBasePath, GlobalProgressActionArgs prog, UploadStats stats)
        {
            string fileName = Path.GetFileName(filePath);
            string remotePath = $"{remoteBasePath}/{fileName}";
            
            DebugConsole.WriteSeparator('-', 40);
            DebugConsole.WriteInfo($"Processing: {fileName}");
            
            try
            {
                prog.Text = $"Checking {fileName}...";
                
                var fileInfo = new FileInfo(filePath);
                DebugConsole.WriteKeyValue("File size", $"{fileInfo.Length:N0} bytes");
                DebugConsole.WriteKeyValue("Last modified", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));

                bool needsUpload = await ShouldUploadFile(filePath, remotePath);

                if (!needsUpload)
                {
                    DebugConsole.WriteSuccess($"SKIPPED: {fileName} - Identical to remote version");
                    stats.SkippedCount++;
                    stats.SkippedSize += fileInfo.Length;
                    return;
                }

                prog.Text = $"Uploading {fileName}...";
                DebugConsole.WriteInfo($"UPLOADING: {fileName}");

                bool uploadSuccess = await UploadFileWithRetry(filePath, remotePath, fileName);
                
                if (uploadSuccess)
                {
                    DebugConsole.WriteSuccess($"Upload completed: {fileName}");
                    stats.UploadedCount++;
                    stats.UploadedSize += fileInfo.Length;
                }
                else
                {
                    DebugConsole.WriteError($"Upload failed after retries: {fileName}");
                    stats.FailedCount++;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Error processing {fileName}");
                stats.FailedCount++;
            }
        }

        private async Task<bool> UploadFileWithRetry(string localPath, string remotePath, string fileName)
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    DebugConsole.WriteDebug($"Upload attempt {attempt}/{MaxRetries} for {fileName}");
                    
                    var result = await ExecuteRcloneCommand($"copyto \"{localPath}\" \"{remotePath}\" --config \"{ConfigPath}\" --progress", ProcessTimeout);
                    
                    if (result.Success)
                    {
                        DebugConsole.WriteSuccess($"Upload successful on attempt {attempt}");
                        return true;
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Attempt {attempt} failed: {result.Error}");
                        
                        if (attempt < MaxRetries)
                        {
                            DebugConsole.WriteInfo($"Waiting {RetryDelay.TotalSeconds} seconds before retry...");
                            await Task.Delay(RetryDelay);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Upload attempt {attempt} exception");
                    
                    if (attempt < MaxRetries)
                    {
                        await Task.Delay(RetryDelay);
                    }
                }
            }
            
            return false;
        }

        private async Task<bool> ShouldUploadFile(string localFilePath, string remotePath)
        {
            try
            {
                string localChecksum = await GetLocalFileChecksum(localFilePath);
                DebugConsole.WriteDebug($"Local MD5: {localChecksum}");

                string remoteChecksum = await GetRemoteFileChecksum(remotePath);
                
                if (string.IsNullOrEmpty(remoteChecksum))
                {
                    DebugConsole.WriteInfo("Remote file doesn't exist - upload needed");
                    return true;
                }

                DebugConsole.WriteDebug($"Remote MD5: {remoteChecksum}");
                
                bool different = !localChecksum.Equals(remoteChecksum, StringComparison.OrdinalIgnoreCase);
                DebugConsole.WriteDebug($"Files {(different ? "DIFFERENT" : "IDENTICAL")}");
                
                return different;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Checksum comparison failed - uploading to be safe");
                return true;
            }
        }

        private async Task<string> GetLocalFileChecksum(string filePath)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = await Task.Run(() => md5.ComputeHash(stream));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private async Task<string> GetRemoteFileChecksum(string remotePath)
        {
            try
            {
                var result = await ExecuteRcloneCommand($"md5sum \"{remotePath}\" --config \"{ConfigPath}\"", TimeSpan.FromSeconds(30));
                
                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    string[] parts = result.Output.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1)
                    {
                        return parts[0].ToLowerInvariant();
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to get remote checksum");
                return null;
            }
        }

        private static async Task<RcloneResult> ExecuteRcloneCommand(string arguments, TimeSpan timeout, bool hideWindow = true)
        {
            DebugConsole.WriteDebug($"Executing: rclone {arguments}");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = RcloneExePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = hideWindow
            };

            var result = new RcloneResult();
            
            try
            {
                using var process = Process.Start(startInfo);
                using var cts = new System.Threading.CancellationTokenSource(timeout);
                
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                bool finished = process.WaitForExit((int)timeout.TotalMilliseconds);
                
                if (!finished)
                {
                    DebugConsole.WriteWarning($"Process timed out after {timeout.TotalSeconds} seconds");
                    try { process.Kill(); } catch { }
                    result.Success = false;
                    result.Error = "Process timed out";
                    result.ExitCode = -1;
                    return result;
                }

                result.Output = await outputTask;
                result.Error = await errorTask;
                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
                
                if (!result.Success)
                {
                    DebugConsole.WriteWarning($"Process failed with exit code {result.ExitCode}");
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        DebugConsole.WriteError($"Error output: {result.Error}");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Process execution failed");
                result.Success = false;
                result.Error = ex.Message;
                result.ExitCode = -1;
                return result;
            }
        }

        private static string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";
                
            var invalidChars = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            string sanitized = invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_'));
            return sanitized.Trim();
        }

        private static void ShowUploadResults(IPlayniteAPI api, string gameName, UploadStats stats)
        {
            DebugConsole.WriteSection("Upload Results");
            DebugConsole.WriteKeyValue("Uploaded", $"{stats.UploadedCount} files ({stats.UploadedSize:N0} bytes)");
            DebugConsole.WriteKeyValue("Skipped", $"{stats.SkippedCount} files ({stats.SkippedSize:N0} bytes)");
            DebugConsole.WriteKeyValue("Failed", $"{stats.FailedCount} files");
            
            string message = $"Upload complete for {gameName}: " +
                           $"{stats.UploadedCount} uploaded, {stats.SkippedCount} skipped, {stats.FailedCount} failed.";
            
            var notificationType = stats.FailedCount > 0 ? NotificationType.Error : NotificationType.Info;
            
            api.Notifications.Add(new NotificationMessage("RCLONE_UPLOAD_COMPLETE", message, notificationType));
        }
public async Task<DownloadResult> Download(string gameName, string localDownloadPath, IPlayniteAPI PlayniteApi, bool overwriteExisting = false)
{            await RcloneCheckAsync(PlayniteApi);

    var overallStartTime = DateTime.Now;
    DebugConsole.WriteSection($"Download Process for {gameName}");
    DebugConsole.WriteKeyValue("Process started at", overallStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
    DebugConsole.WriteKeyValue("Local download path", localDownloadPath);
    DebugConsole.WriteKeyValue("Overwrite existing", overwriteExisting);

    if (!File.Exists(RcloneExePath) || !File.Exists(ConfigPath))
    {
        string error = "Rclone is not installed or configured.";
        DebugConsole.WriteError(error);
        PlayniteApi.Notifications.Add(new NotificationMessage("RCLONE_MISSING", error, NotificationType.Error));
        return new DownloadResult { Success = false, Error = error };
    }

    string remoteBasePath = $"gdrive:PlayniteCloudSave/{SanitizeGameName(gameName)}";
    DebugConsole.WriteKeyValue("Remote base path", remoteBasePath);

    var downloadResult = new DownloadResult { StartTime = overallStartTime };

    try
    {
        // Ensure local download directory exists
        if (!Directory.Exists(localDownloadPath))
        {
            Directory.CreateDirectory(localDownloadPath);
            DebugConsole.WriteInfo($"Created local download directory: {localDownloadPath}");
        }

        // Get list of remote files
        var remoteFiles = await GetRemoteFileList(remoteBasePath);
        if (!remoteFiles.Any())
        {
            string message = $"No save files found for {gameName} in cloud storage.";
            DebugConsole.WriteWarning(message);
            downloadResult.Success = false;
            downloadResult.Error = message;
            return downloadResult;
        }

        DebugConsole.WriteInfo($"Found {remoteFiles.Count} files in cloud storage:");
        DebugConsole.WriteList("Remote files", remoteFiles.Select(f => f.Name));

         PlayniteApi.Dialogs.ActivateGlobalProgress(async (prog) =>
        {
            prog.ProgressMaxValue = remoteFiles.Count;
            prog.CurrentProgressValue = 0;
            prog.Text = $"Downloading save files for {gameName}...";

            foreach (var remoteFile in remoteFiles)
            {
                await ProcessDownloadFile(remoteFile, localDownloadPath, remoteBasePath, prog, downloadResult, overwriteExisting);
                prog.CurrentProgressValue++;
            }

            var overallEndTime = DateTime.Now;
            downloadResult.TotalTime = overallEndTime - overallStartTime;
            DebugConsole.WriteKeyValue("Process completed at", overallEndTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));

            ShowDownloadResults(PlayniteApi, gameName, downloadResult);

        }, new GlobalProgressOptions($"Downloading {gameName} saves", true));

        downloadResult.Success = downloadResult.FailedCount == 0;
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "Download process failed");
        downloadResult.Success = false;
        downloadResult.Error = ex.Message;
        PlayniteApi.Notifications.Add(new NotificationMessage("RCLONE_DOWNLOAD_ERROR", $"Error downloading {gameName}: {ex.Message}", NotificationType.Error));
    }

    return downloadResult;
}

private async Task<List<RemoteFileInfo>> GetRemoteFileList(string remotePath)
{
    DebugConsole.WriteInfo($"Getting remote file list from: {remotePath}");
    
    try
    {
        var result = await ExecuteRcloneCommand($"lsjson \"{remotePath}\" --config \"{ConfigPath}\"", TimeSpan.FromSeconds(60));
        
        if (!result.Success)
        {
            DebugConsole.WriteWarning($"Failed to list remote files: {result.Error}");
            return new List<RemoteFileInfo>();
        }

        if (string.IsNullOrWhiteSpace(result.Output))
        {
            DebugConsole.WriteInfo("No files found in remote directory");
            return new List<RemoteFileInfo>();
        }

        var files = JsonConvert.DeserializeObject<List<RcloneFileInfo>>(result.Output);
        var remoteFiles = files?.Where(f => !f.IsDir).Select(f => new RemoteFileInfo
        {
            Name = f.Name,
            Size = f.Size,
            ModTime = f.ModTime
        }).ToList() ?? new List<RemoteFileInfo>();

        DebugConsole.WriteSuccess($"Found {remoteFiles.Count} files in remote directory");
        return remoteFiles;
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "Failed to parse remote file list");
        return new List<RemoteFileInfo>();
    }
}

private async Task ProcessDownloadFile(RemoteFileInfo remoteFile, string localDownloadPath, string remoteBasePath, GlobalProgressActionArgs prog, DownloadResult downloadResult, bool overwriteExisting)
{
    string localFilePath = Path.Combine(localDownloadPath, remoteFile.Name);
    string remoteFilePath = $"{remoteBasePath}/{remoteFile.Name}";
    
    DebugConsole.WriteSeparator('-', 40);
    DebugConsole.WriteInfo($"Processing: {remoteFile.Name}");
    
    try
    {
        prog.Text = $"Checking {remoteFile.Name}...";
        
        DebugConsole.WriteKeyValue("Remote file size", $"{remoteFile.Size:N0} bytes");
        DebugConsole.WriteKeyValue("Remote modified", remoteFile.ModTime.ToString("yyyy-MM-dd HH:mm:ss"));

        bool shouldDownload = await ShouldDownloadFile(localFilePath, remoteFilePath, overwriteExisting);

        if (!shouldDownload)
        {
            DebugConsole.WriteSuccess($"SKIPPED: {remoteFile.Name} - Local file is up to date");
            downloadResult.SkippedCount++;
            downloadResult.SkippedSize += remoteFile.Size;
            return;
        }

        prog.Text = $"Downloading {remoteFile.Name}...";
        DebugConsole.WriteInfo($"DOWNLOADING: {remoteFile.Name}");

        bool downloadSuccess = await DownloadFileWithRetry(remoteFilePath, localFilePath, remoteFile.Name);
        
        if (downloadSuccess)
        {
            DebugConsole.WriteSuccess($"Download completed: {remoteFile.Name}");
            downloadResult.DownloadedCount++;
            downloadResult.DownloadedSize += remoteFile.Size;
        }
        else
        {
            DebugConsole.WriteError($"Download failed after retries: {remoteFile.Name}");
            downloadResult.FailedCount++;
            downloadResult.FailedFiles.Add(remoteFile.Name);
        }
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, $"Error processing {remoteFile.Name}");
        downloadResult.FailedCount++;
        downloadResult.FailedFiles.Add(remoteFile.Name);
    }
}

private async Task<bool> DownloadFileWithRetry(string remotePath, string localPath, string fileName)
{
    for (int attempt = 1; attempt <= MaxRetries; attempt++)
    {
        try
        {
            DebugConsole.WriteDebug($"Download attempt {attempt}/{MaxRetries} for {fileName}");
            
            var result = await ExecuteRcloneCommand($"copyto \"{remotePath}\" \"{localPath}\" --config \"{ConfigPath}\" --progress", ProcessTimeout);
            
            if (result.Success)
            {
                DebugConsole.WriteSuccess($"Download successful on attempt {attempt}");
                return true;
            }
            else
            {
                DebugConsole.WriteWarning($"Attempt {attempt} failed: {result.Error}");
                
                if (attempt < MaxRetries)
                {
                    DebugConsole.WriteInfo($"Waiting {RetryDelay.TotalSeconds} seconds before retry...");
                    await Task.Delay(RetryDelay);
                }
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteException(ex, $"Download attempt {attempt} exception");
            
            if (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelay);
            }
        }
    }
    
    return false;
}

private async Task<bool> ShouldDownloadFile(string localFilePath, string remotePath, bool overwriteExisting)
{
    try
    {
        if (!File.Exists(localFilePath))
        {
            DebugConsole.WriteInfo("Local file doesn't exist - download needed");
            return true;
        }

        if (overwriteExisting)
        {
            DebugConsole.WriteInfo("Overwrite existing enabled - download needed");
            return true;
        }

        string localChecksum = await GetLocalFileChecksum(localFilePath);
        DebugConsole.WriteDebug($"Local MD5: {localChecksum}");

        string remoteChecksum = await GetRemoteFileChecksum(remotePath);
        
        if (string.IsNullOrEmpty(remoteChecksum))
        {
            DebugConsole.WriteWarning("Could not get remote checksum - downloading to be safe");
            return true;
        }

        DebugConsole.WriteDebug($"Remote MD5: {remoteChecksum}");
        
        bool different = !localChecksum.Equals(remoteChecksum, StringComparison.OrdinalIgnoreCase);
        DebugConsole.WriteDebug($"Files {(different ? "DIFFERENT" : "IDENTICAL")}");
        
        return different;
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "Checksum comparison failed - downloading to be safe");
        return true;
    }
}

private static void ShowDownloadResults(IPlayniteAPI api, string gameName, DownloadResult result)
{
    DebugConsole.WriteSection("Download Results");
    DebugConsole.WriteKeyValue("Downloaded", $"{result.DownloadedCount} files ({result.DownloadedSize:N0} bytes)");
    DebugConsole.WriteKeyValue("Skipped", $"{result.SkippedCount} files ({result.SkippedSize:N0} bytes)");
    DebugConsole.WriteKeyValue("Failed", $"{result.FailedCount} files");
    DebugConsole.WriteKeyValue("Total time", $"{result.TotalTime.TotalSeconds:F1} seconds");
    
    if (result.FailedFiles.Any())
    {
        DebugConsole.WriteList("Failed files", result.FailedFiles);
    }
    
    string message = $"Download complete for {gameName}: " +
                   $"{result.DownloadedCount} downloaded, {result.SkippedCount} skipped, {result.FailedCount} failed.";
    
    var notificationType = result.FailedCount > 0 ? NotificationType.Error : NotificationType.Info;
    
    api.Notifications.Add(new NotificationMessage("RCLONE_DOWNLOAD_COMPLETE", message, notificationType));
}

// Add these classes to your existing RcloneUploader class:
public class DownloadResult
{
    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public int DownloadedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public long DownloadedSize { get; set; }
    public long SkippedSize { get; set; }
    public TimeSpan TotalTime { get; set; }
    public DateTime StartTime { get; set; }
    public List<string> FailedFiles { get; set; } = new List<string>();
}

private class RemoteFileInfo
{
    public string Name { get; set; }
    public long Size { get; set; }
    public DateTime ModTime { get; set; }
}

private class RcloneFileInfo
{
    [JsonProperty("Name")]
    public string Name { get; set; }
    
    [JsonProperty("Size")]
    public long Size { get; set; }
    
    [JsonProperty("ModTime")]
    public DateTime ModTime { get; set; }
    
    [JsonProperty("IsDir")]
    public bool IsDir { get; set; }
}
        private class RcloneResult
        {
            public bool Success { get; set; }
            public string Output { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
            public int ExitCode { get; set; }
        }

        private class UploadStats
        {
            public int UploadedCount { get; set; }
            public int SkippedCount { get; set; }
            public int FailedCount { get; set; }
            public long UploadedSize { get; set; }
            public long SkippedSize { get; set; }
            public TimeSpan CheckingTime { get; set; }
            public TimeSpan UploadingTime { get; set; }
            public TimeSpan TotalTime { get; set; }
            public DateTime StartTime { get; set; }
        }
    }
}