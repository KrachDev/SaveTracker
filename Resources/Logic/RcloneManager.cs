using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagment;
using JsonException = Newtonsoft.Json.JsonException;
namespace SaveTracker {
public class RcloneManager {
  private static string RcloneExePath =>
      Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");
  private static readonly string ToolsPath =
      Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools");
  private readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");
  private readonly int _maxRetries = 3;
  private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
  private readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(10);
  public SaveTrackerSettingsViewModel UploaderSettings { get; set; }
  public IPlayniteAPI  PlayniteApi { get; set; }

  RcloneInstaller rcloneInstaller ;

  public RcloneManager(SaveTrackerSettingsViewModel ManagerSettings )
  {
    rcloneInstaller = new RcloneInstaller(ManagerSettings);
  }
  CloudProviderHelper recloneHelper = new CloudProviderHelper();
  RcloneFileOperations  rcloneFileOperations = new RcloneFileOperations(SaveTracker.PlayniteApi);
  // Modified Upload method
public async Task Upload(List<string> saveFilesList, IPlayniteAPI playniteApi, Game gameArg,
                         CloudProvider provider) {
    
    // Initialize and validate
    await rcloneInstaller.RcloneCheckAsync(playniteApi);
    var overallStartTime = DateTime.Now;
    string providerName = recloneHelper.GetProviderConfigName(provider);
    string displayName = recloneHelper.GetProviderDisplayName(provider);

    DebugConsole.WriteSection($"Upload Process for {gameArg.Name} to {displayName}");
    DebugConsole.WriteKeyValue("Process started at", overallStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
    DebugConsole.WriteKeyValue("Files to process", saveFilesList.Count);
    DebugConsole.WriteKeyValue("Cloud provider", displayName);
    DebugConsole.WriteList("Save files", saveFilesList.Select(Path.GetFileName));

    // Validate rclone installation
    if (!File.Exists(RcloneExePath) || !File.Exists(_configPath)) {
        string error = "Rclone is not installed or configured.";
        DebugConsole.WriteError(error);
        playniteApi.Notifications.Add(
            new NotificationMessage("RCLONE_MISSING", error, NotificationType.Error));
        return;
    }

    string remoteBasePath = $"{providerName}:PlayniteCloudSave/{SanitizeGameName(gameArg.Name)}";
    DebugConsole.WriteKeyValue("Remote base path", remoteBasePath);

    var stats = new UploadStats();
    var validFiles = saveFilesList.Where(File.Exists).ToList();
    var invalidFiles = saveFilesList.Except(validFiles).ToList();

    if (invalidFiles.Any()) {
        DebugConsole.WriteWarning($"Skipping {invalidFiles.Count} missing files:");
        DebugConsole.WriteList("Missing files", invalidFiles.Select(Path.GetFileName));
    }

    // Get checksum file path (but don't add to main list)
    string checksumFile = Path.Combine(gameArg.InstallDirectory, ".savetracker_checksums.json");
    bool hasChecksumFile = File.Exists(checksumFile);
    
    // Calculate total progress steps
    int totalSteps = validFiles.Count + (hasChecksumFile ? 1 : 0);

    playniteApi.Dialogs.ActivateGlobalProgress((prog) => {
        try {
            prog.ProgressMaxValue = totalSteps;
            prog.CurrentProgressValue = 0;
            prog.Text = $"Processing save files for {gameArg.Name} ({displayName})...";

            // Phase 1: Upload all save files first
            DebugConsole.WriteInfo("Phase 1: Processing save files");
            foreach (var file in validFiles) {
                prog.Text = $"Uploading {Path.GetFileName(file)}...";
                _ = rcloneFileOperations.ProcessFile(file, remoteBasePath, prog, stats);
                prog.CurrentProgressValue++;
            }

            // Phase 2: Upload checksum file AFTER all saves are processed
            if (hasChecksumFile) {
                DebugConsole.WriteInfo("Phase 2: Processing checksum file");
                prog.Text = "Uploading checksum file...";
                _ = rcloneFileOperations.ProcessFile(checksumFile, remoteBasePath, prog, stats);
                prog.CurrentProgressValue++;
            }

            var overallEndTime = DateTime.Now;
            var totalTime = overallEndTime - overallStartTime;
            
            DebugConsole.WriteKeyValue("Process completed at", overallEndTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            DebugConsole.WriteKeyValue("Total processing time", $"{totalTime.TotalSeconds:F2} seconds");

            ShowUploadResults(playniteApi, stats, displayName);
            
        } catch (Exception e) {
            DebugConsole.WriteError($"Upload failed: {e.Message}");
            DebugConsole.WriteException(e, "Upload Exception");
            playniteApi.Notifications.Add(
                new NotificationMessage("UPLOAD_ERROR", 
                    $"Failed to upload saves for {gameArg.Name}: {e.Message}", 
                    NotificationType.Error));
        }
    }, new GlobalProgressOptions($"Uploading {gameArg.Name} saves to {displayName}", true));
}
  // Modified Download method
public async Task<DownloadResult> Download(Game gameArg,
                                           IPlayniteAPI playniteApi,
                                           CloudProvider provider,
                                           bool overwriteExisting = false)
{
    DebugConsole.WriteLine("[DownloadStep] Starting download process...");

    await rcloneInstaller.RcloneCheckAsync(playniteApi);
    DebugConsole.WriteLine("[DownloadStep] Verified Rclone installation");
    
    // Use the game's install directory for staging, not a hardcoded path
    string stagingFolder = Path.Combine(playniteApi.Paths.ApplicationPath, gameArg.InstallDirectory, "SavesDownloaded");
    Directory.CreateDirectory(stagingFolder);
    DebugConsole.WriteLine($"[DownloadStep] Created/verified staging folder: {stagingFolder}");

    string tempChecksumFile = Path.Combine(stagingFolder, ".savetracker_checksums.json");

    var overallStartTime = DateTime.Now;
    string providerName = recloneHelper.GetProviderConfigName(provider);
    string displayName = recloneHelper.GetProviderDisplayName(provider);

    DebugConsole.WriteSection($"[DownloadStep] Download Process for {gameArg.Name} from {displayName}");
    DebugConsole.WriteKeyValue("[DownloadStep] Process started at", overallStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
    DebugConsole.WriteKeyValue("[DownloadStep] Cloud provider", displayName);
    DebugConsole.WriteKeyValue("[DownloadStep] Overwrite existing", overwriteExisting);

    if (!File.Exists(RcloneExePath) || !File.Exists(_configPath))
    {
        string error = "[DownloadStep] Rclone is not installed or configured.";
        DebugConsole.WriteError(error);
        playniteApi.Notifications.Add(new NotificationMessage("RCLONE_MISSING", error, NotificationType.Error));
        return new DownloadResult();
    }

    string remoteBasePath = $"{providerName}:PlayniteCloudSave/{SanitizeGameName(gameArg.Name)}";
    DebugConsole.WriteKeyValue("[DownloadStep] Remote base path", remoteBasePath);

    var downloadResult = new DownloadResult();

    try
    {
        var checksumRemotePath = $"{remoteBasePath}/.savetracker_checksums.json";

        DebugConsole.WriteLine("[DownloadStep] Downloading remote checksum file...");
        var downloaded = await rcloneFileOperations.DownloadFileWithRetry(checksumRemotePath, tempChecksumFile, ".savetracker_checksums.json");

        if (!downloaded || !File.Exists(tempChecksumFile))
        {
            DebugConsole.WriteError("[DownloadStep] Failed to retrieve checksum file from remote storage.");
            return downloadResult;
        }

        DebugConsole.WriteLine("[DownloadStep] Reading checksum file content...");
        var checksumContent = File.ReadAllText(tempChecksumFile);
        var checksumData = JsonConvert.DeserializeObject<ChecksumData>(checksumContent);

        if (checksumData == null || checksumData.Files.Count == 0)
        {
            DebugConsole.WriteWarning("[DownloadStep] No valid file entries found in checksum JSON.");
            return downloadResult;
        }

        DebugConsole.WriteInfo($"[DownloadStep] Loaded {checksumData.Files.Count} tracked files from remote checksum file.");
        DebugConsole.WriteList("[DownloadStep] Tracked paths", checksumData.Files.Values.Select(f => f.Path));

        playniteApi.Dialogs.ActivateGlobalProgress((prog) =>
        {
            prog.ProgressMaxValue = checksumData.Files.Count;
            prog.CurrentProgressValue = 0;
            prog.Text = $"Downloading save files for {gameArg.Name} from {displayName}...";

            DebugConsole.WriteLine("[DownloadStep] Starting file download loop...");

foreach (var entry in checksumData.Files)
{
    string fileName = entry.Key;
    var record = entry.Value;
    string originalPath = record.Path; // This should be the original path from when uploaded

    // Determine the final destination path
    string finalPath;
    if (string.IsNullOrEmpty(originalPath))
    {
        // If no original path stored, default to install directory
        finalPath = Path.Combine(gameArg.InstallDirectory, fileName);
    }
    else
    {
        // Use the original path, but replace the username if needed
        var currentUser = Environment.UserName;
        finalPath = ReplaceUserInPath(originalPath, currentUser);
    }

    // Create staging path - preserve directory structure
    string relativeDir = Path.GetDirectoryName(fileName) ?? "";
    string stagingDir = Path.Combine(stagingFolder, relativeDir);
    string stagedPath = Path.Combine(stagingDir, Path.GetFileName(fileName));

    DebugConsole.WriteLine($"[DownloadStep] Processing download for: {fileName}");
    DebugConsole.WriteKeyValue("[DownloadStep] Original path", originalPath);
    DebugConsole.WriteKeyValue("[DownloadStep] Remote path", $"{remoteBasePath}/{fileName}");
    DebugConsole.WriteKeyValue("[DownloadStep] Staging path", stagedPath);
    DebugConsole.WriteKeyValue("[DownloadStep] Final path", finalPath);

    // Create staging directory if it doesn't exist
    if (!Directory.Exists(stagingDir))
    {
        Directory.CreateDirectory(stagingDir);
        DebugConsole.WriteLine($"[DownloadStep] Created staging directory: {stagingDir}");
    }

    try
    {
        // Download file to staging area
        var downloadTask = rcloneFileOperations.ProcessDownloadFile(
            new Resources.Logic.RecloneManagment.RemoteFileInfo
            {
                Name = fileName,
                Size = record.FileSize,
                ModTime = record.LastUpload
            },
            stagingDir, // Use the staging directory (not the full path)
            remoteBasePath,
            prog,
            downloadResult,
            overwriteExisting
        );

        // Wait for download to complete (if it's a task)
        var task = downloadTask;
        if (task != null && !task.IsCompleted)
        {
        }
        
        // Just check if the file exists in staging - ignore task status
        if (File.Exists(stagedPath))
        {
            try
            {
                DebugConsole.WriteInfo($"Moving from staging: {stagedPath} to final: {finalPath}");
                
                // Create the destination directory if it doesn't exist
                var destinationDirectory = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                    DebugConsole.WriteLine($"[DownloadStep] Created destination directory: {destinationDirectory}");
                }
                
                // Handle existing file
                if (File.Exists(finalPath))
                {
                    if (overwriteExisting)
                    {
                        File.Delete(finalPath);
                        DebugConsole.WriteLine($"[DownloadStep] Deleted existing file: {finalPath}");
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"[DownloadStep] File already exists, skipping: {finalPath}");
                        prog.CurrentProgressValue++;
                        continue;
                    }
                }
                
                // Move the file to its final location
                File.Move(stagedPath, finalPath);
                DebugConsole.WriteLine($"[DownloadStep] Successfully moved {fileName} to {finalPath}");
                
                // Verify the file was moved correctly
                if (File.Exists(finalPath))
                {
                    var fileInfo = new FileInfo(finalPath);
                    DebugConsole.WriteLine($"[DownloadStep] File verified at destination. Size: {fileInfo.Length} bytes");
                    downloadResult.DownloadedCount++;
                }
                else
                {
                    DebugConsole.WriteError($"[DownloadStep] File move appeared to succeed but file not found at destination: {finalPath}");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"[DownloadStep] Failed to move {fileName} to final location: {finalPath}");
                downloadResult.FailedCount++;
            }
        }
        else
        {
            DebugConsole.WriteError($"[DownloadStep] Staged file not found: {stagedPath}");
            downloadResult.FailedCount++;
        }
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, $"[DownloadStep] Exception during download process for {fileName}");
        downloadResult.FailedCount++;
    }

    prog.CurrentProgressValue++;
}
            var overallEndTime = DateTime.Now;
            downloadResult.TotalTime = overallEndTime - overallStartTime;

            DebugConsole.WriteKeyValue("[DownloadStep] Process completed at", overallEndTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            DebugConsole.WriteKeyValue("[DownloadStep] Total time taken", downloadResult.TotalTime.ToString());

            ShowDownloadResults(playniteApi, gameArg.Name, downloadResult);
        },
        new GlobalProgressOptions($"Downloading {gameArg.Name} saves from {displayName}", true));
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "[DownloadStep] Download process failed");
        playniteApi.Notifications.Add(new NotificationMessage(
            "RCLONE_DOWNLOAD_ERROR",
            $"Error downloading {gameArg.Name} from {displayName}: {ex.Message}",
            NotificationType.Error));
    }

    return downloadResult;
    
}
public async Task<ChecksumData> GetRemoteChecksumData(Game gameArg, CloudProvider provider)
{
    DebugConsole.WriteLine("[ChecksumHelper] Starting remote checksum retrieval...");
    
    string providerName = recloneHelper.GetProviderConfigName(provider);
    string displayName = recloneHelper.GetProviderDisplayName(provider);
    
    DebugConsole.WriteKeyValue("[ChecksumHelper] Provider", displayName);
    DebugConsole.WriteKeyValue("[ChecksumHelper] Game", gameArg.Name);
    
    // Create temp directory for checksum file
    string tempFolder = Path.Combine(Path.GetTempPath(), "PlayniteCloudSave", "checksums");
    Directory.CreateDirectory(tempFolder);
    
    string tempChecksumFile = Path.Combine(tempFolder, $"{SanitizeGameName(gameArg.Name)}_checksums.json");
    
    try
    {
        string remoteBasePath = $"{providerName}:PlayniteCloudSave/{SanitizeGameName(gameArg.Name)}";
        string checksumRemotePath = $"{remoteBasePath}/.savetracker_checksums.json";
        
        DebugConsole.WriteKeyValue("[ChecksumHelper] Remote checksum path", checksumRemotePath);
        DebugConsole.WriteKeyValue("[ChecksumHelper] Temp file path", tempChecksumFile);
        
        // Download the checksum file
        var downloaded = await rcloneFileOperations.DownloadFileWithRetry(
            checksumRemotePath, 
            tempChecksumFile, 
            ".savetracker_checksums.json"
        );
        
        if (!downloaded || !File.Exists(tempChecksumFile))
        {
            DebugConsole.WriteError("[ChecksumHelper] Failed to retrieve checksum file from remote storage.");
            return null;
        }
        
        // Read and deserialize the checksum data
        var checksumContent = File.ReadAllText(tempChecksumFile);
        var checksumData = JsonConvert.DeserializeObject<ChecksumData>(checksumContent);
        
        if (checksumData == null || checksumData.Files.Count == 0)
        {
            DebugConsole.WriteWarning("[ChecksumHelper] No valid file entries found in checksum JSON.");
            return null;
        }
        
        DebugConsole.WriteInfo($"[ChecksumHelper] Successfully loaded {checksumData.Files.Count} tracked files from remote.");
        
        // Clean up temp file
        if (File.Exists(tempChecksumFile))
        {
            File.Delete(tempChecksumFile);
        }
        
        return checksumData;
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "[ChecksumHelper] Failed to retrieve remote checksum data");
        
        // Clean up temp file on error
        if (File.Exists(tempChecksumFile))
        {
            File.Delete(tempChecksumFile);
        }
        
        return null;
    }
}
private string ReplaceUserInPath(string originalPath, string currentUser)
{
    // Check if path contains Users directory
    if (originalPath.Contains("\\Users\\"))
    {
        // Extract the old username from the path
        var userIndex = originalPath.IndexOf("\\Users\\") + 7; // 7 = length of "\\Users\\"
        var nextSlashIndex = originalPath.IndexOf("\\", userIndex);
        
        if (nextSlashIndex > userIndex)
        {
            var oldUser = originalPath.Substring(userIndex, nextSlashIndex - userIndex);
            
            // Replace old username with current username
            if (!oldUser.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
            {
                return originalPath.Replace($"\\Users\\{oldUser}\\", $"\\Users\\{currentUser}\\");
            }
        }
    }
    
    return originalPath; // Return original if no replacement needed
}
  private string SanitizeGameName(string gameName) {
    if (string.IsNullOrWhiteSpace(gameName))
      return "UnknownGame";

    var invalidChars = Path.GetInvalidFileNameChars().Concat(
      new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
    string sanitized = invalidChars.Aggregate(gameName, (current, c) => current.Replace(c, '_'));
    return sanitized.Trim();
  }

  private void ShowUploadResults(IPlayniteAPI api, UploadStats stats,
    string displayName) {
    DebugConsole.WriteSection("Upload Results");
    DebugConsole.WriteKeyValue("Uploaded",
      $"{stats.UploadedCount} files ({stats.UploadedSize:N0} bytes)");
    DebugConsole.WriteKeyValue("Skipped",
      $"{stats.SkippedCount} files ({stats.SkippedSize:N0} bytes)");
    DebugConsole.WriteKeyValue("Failed", $"{stats.FailedCount} files");

    // Use displayName instead of gameName for user-facing messages
    string message =
      $"Upload complete for {displayName}: " +
      $"{stats.UploadedCount} uploaded, {stats.SkippedCount} skipped, {stats.FailedCount} failed.";

    var notificationType = stats.FailedCount > 0 ? NotificationType.Error : NotificationType.Info;

    api.Notifications.Add(
      new NotificationMessage("RCLONE_UPLOAD_COMPLETE", message, notificationType));
  }

 

  private void ShowDownloadResults(IPlayniteAPI api, string gameName, DownloadResult result) {
    DebugConsole.WriteSection("Download Results");
    DebugConsole.WriteKeyValue(
      "Downloaded", $"{result.DownloadedCount} files ({result.DownloadedSize:N0} bytes)");
    DebugConsole.WriteKeyValue("Skipped",
      $"{result.SkippedCount} files ({result.SkippedSize:N0} bytes)");
    DebugConsole.WriteKeyValue("Failed", $"{result.FailedCount} files");
    DebugConsole.WriteKeyValue("Total time", $"{result.TotalTime.TotalSeconds:F1} seconds");

    if (result.FailedFiles.Any()) {
      DebugConsole.WriteList("Failed files", result.FailedFiles);
    }

    string message =
      $"Download complete for {gameName}: " +
      $"{result.DownloadedCount} downloaded, {result.SkippedCount} skipped, {result.FailedCount} failed.";

    var notificationType = result.FailedCount > 0 ? NotificationType.Error : NotificationType.Info;

    api.Notifications.Add(
      new NotificationMessage("RCLONE_DOWNLOAD_COMPLETE", message, notificationType));
  }

  // Add these classes to your existing RcloneUploader class:
  public class DownloadResult {
    public int DownloadedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public long DownloadedSize { get; set; }
    public long SkippedSize { get; set; }
    public TimeSpan TotalTime { get; set; }
    public List<string> FailedFiles { get; } = new List<string>();
  }

  private class RemoteFileInfo {
    public string Name { get; set; }
    public long Size { get; set; }
    public DateTime ModTime { get; set; }
  }

  public class RcloneFileInfo {
    [JsonProperty("Name")]
    public string Name { get; set; }

    [JsonProperty("Size")]
    public long Size { get; set; }

    [JsonProperty("ModTime")]
    public DateTime ModTime { get; set; }

    [JsonProperty("IsDir")]
    public bool IsDir { get; set; }
  }
  

  public class UploadStats {
    public int UploadedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public long UploadedSize { get; set; }
    public long SkippedSize { get; set; }
  }
}
}

public class RemoteFileInfo {
  public string Name { get; set; }
  public long Size { get; set; }
  public DateTime ModTime { get; set; }
}