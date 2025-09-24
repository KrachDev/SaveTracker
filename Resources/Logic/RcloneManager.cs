using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveTracker.Resources.Helpers;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
namespace SaveTracker
{
    public class RcloneManager
    {
        private static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");
        private static readonly string ToolsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools"
        );
        private readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");
        public SaveTrackerSettingsViewModel UploaderSettings { get; set; }
        public IPlayniteAPI PlayniteApi { get; set; }

        private readonly RcloneInstaller _rcloneInstaller;

        public RcloneManager(SaveTrackerSettingsViewModel managerSettings)
        {
            _rcloneInstaller = new RcloneInstaller(managerSettings);
        }

        private readonly CloudProviderHelper _recloneHelper = new CloudProviderHelper();

        private readonly RcloneFileOperations _rcloneFileOperations = new RcloneFileOperations(
            SaveTracker.PlayniteApi
        );
        // Modified Upload method
        public async Task Upload(
            List<string> saveFiles,
            IPlayniteAPI playniteApi,
            Game gameArg,
            CloudProvider provider
        )
        { 
            DebugConsole.WriteList("Staging Files To Be Uploaded: ", saveFiles);

            var saveFilesList = saveFiles.ToList();
            
            var gameData = Misc.GetGameData(gameArg);
            if (!gameData.CanUploads)
            {
                DebugConsole.WriteInfo("Upload Bypassed");
                return;
            }

            if (gameData.GameProvider != CloudProvider.Global)
            {
                provider = gameData.GameProvider;
            }

            // Initialize and validate
            await _rcloneInstaller.RcloneCheckAsync(playniteApi, provider);
            var overallStartTime = DateTime.Now;
            string providerName = _recloneHelper.GetProviderConfigName(provider);
            string displayName = _recloneHelper.GetProviderDisplayName(provider);

            DebugConsole.WriteSection($"Upload Process for {gameArg.Name} to {displayName}");
            DebugConsole.WriteKeyValue(
                "Process started at",
                overallStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff")
            );
            DebugConsole.WriteKeyValue("Files to process", saveFilesList.Count);
            DebugConsole.WriteKeyValue("Cloud provider", displayName);
            DebugConsole.WriteKeyValue(
                "Save files Path",
                string.Join(", ", saveFilesList.Select(Path.GetFullPath))
            );
            DebugConsole.WriteList("Save files", saveFilesList.Select(Path.GetFileName));

            // Validate rclone installation
            if (!File.Exists(RcloneExePath) || !File.Exists(_configPath))
            {
                string error = "Rclone is not installed or configured.";
                DebugConsole.WriteError(error);
                playniteApi.Notifications.Add(
                    new NotificationMessage("RCLONE_MISSING", error, NotificationType.Error)
                );
                return;
            }

            string remoteBasePath =
                $"{providerName}:PlayniteCloudSave/{SanitizeGameName(gameArg.Name)}";
            DebugConsole.WriteKeyValue("Remote base path", remoteBasePath);

            var stats = new UploadStats();
            var validFiles = saveFilesList.Where(File.Exists).ToList();
            var invalidFiles = saveFilesList.Except(validFiles).ToList();

            if (invalidFiles.Any())
            {
                DebugConsole.WriteWarning($"Skipping {invalidFiles.Count} missing files:");
                DebugConsole.WriteList("Missing files", invalidFiles.Select(Path.GetFileName));
            }

            // Handle checksum file - create if needed
            string checksumFile = null;
            bool hasChecksumFile = false;
            bool createdChecksumFile = false;

            try
            {
                if (
                    !string.IsNullOrEmpty(gameArg.InstallDirectory)
                    && Directory.Exists(gameArg.InstallDirectory)
                )
                {
                    checksumFile = Path.Combine(
                        gameArg.InstallDirectory,
                        ".savetracker_checksums.json"
                    );
                    hasChecksumFile = File.Exists(checksumFile);

                    // Create checksum file if it doesn't exist and we have valid files
                    if (!hasChecksumFile && validFiles.Any())
                    {
                        DebugConsole.WriteInfo("Creating checksum file for save files...");

                        var checksums = new Dictionary<string, object>
                        {
                            ["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                            ["game"] = gameArg.Name,
                            ["files"] = new Dictionary<string, string>()
                        };

                        var fileChecksums = (Dictionary<string, string>)checksums["files"];

                        foreach (var file in validFiles)
                        {
                            try
                            {
                                string checksum = CalculateFileChecksum(file);
                                fileChecksums[Path.GetFileName(file)] = checksum;
                                DebugConsole.WriteKeyValue(
                                    $"Checksum for {Path.GetFileName(file)}",
                                    checksum
                                );
                            }
                            catch (Exception ex)
                            {
                                DebugConsole.WriteWarning(
                                    $"Failed to calculate checksum for {Path.GetFileName(file)}: {ex.Message}"
                                );
                                fileChecksums[Path.GetFileName(file)] = "ERROR";
                            }
                        }

                        try
                        {
                            string json = System.Text.Json.JsonSerializer.Serialize(
                                checksums,
                                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                            );
                            File.WriteAllText(checksumFile, json);
                            hasChecksumFile = true;
                            createdChecksumFile = true;
                            DebugConsole.WriteSuccess($"Created checksum file: {checksumFile}");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteError(
                                $"Failed to create checksum file: {ex.Message}"
                            );
                            checksumFile = null;
                            hasChecksumFile = false;
                        }
                    }
                }
                else
                {
                    DebugConsole.WriteWarning(
                        "Game install directory not available - skipping checksum file"
                    );
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error handling checksum file: {ex.Message}");
                checksumFile = null;
                hasChecksumFile = false;
            }

            // Calculate total progress steps
            int totalSteps = validFiles.Count + (hasChecksumFile ? 1 : 0);

            // Create and show progress window
            var progressWindow = new UploadProgressWindow(gameArg.Name, displayName);

            // Initialize progress
            var progress = new UploadProgressWindow.UploadProgress
            {
                TotalFiles = totalSteps,
                ValidFiles = validFiles.Count,
                MissingFiles = invalidFiles.Count,
                Status = "Initializing upload...",
                LogMessages = new List<string>
                {
                    // Add initial log messages
                    $"[{DateTime.Now:HH:mm:ss}] Starting upload process for {gameArg.Name}",
                    $"[{DateTime.Now:HH:mm:ss}] Provider: {displayName}",
                    $"[{DateTime.Now:HH:mm:ss}] Total files to process: {totalSteps}"
                }
            };

            if (invalidFiles.Any())
            {
                progress.LogMessages.Add(
                    $"[{DateTime.Now:HH:mm:ss}] Warning: {invalidFiles.Count} files missing"
                );
            }

            progressWindow.UpdateProgress(progress);

            if (UploaderSettings.Settings.ShowUpload)
            {
                progressWindow.Show();
            }

            try
            {
                // Run upload process on background thread
                await Task.Run(
                    async () =>
                    {
                        try
                        {
                            // Phase 1: Upload all save files first
                            progress.Status = "Processing save files...";
                            progress.LogMessages.Add(
                                $"[{DateTime.Now:HH:mm:ss}] Phase 1: Processing save files"
                            );
                            progressWindow.UpdateProgress(progress);

                            foreach (var file in validFiles)
                            {
                                // Check for cancellation
                                if (progressWindow.CancellationToken.IsCancellationRequested)
                                {
                                    progress.Status = "Upload cancelled by user";
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] Upload cancelled by user"
                                    );
                                    progressWindow.UpdateProgress(progress);
                                    return;
                                }

                                string fileName = Path.GetFileName(file);
                                progress.Status = $"Uploading {fileName}...";
                                progress.LogMessages.Add(
                                    $"[{DateTime.Now:HH:mm:ss}] Uploading: {fileName}"
                                );
                                progressWindow.UpdateProgress(progress);

                                try
                                {
                                    // Process the file - ProcessFile is async
                                    await _rcloneFileOperations.ProcessFile(
                                        file,
                                        remoteBasePath,
                                        stats
                                    );

                                    // If we reach here without exception, consider it successful
                                    progress.UploadedFiles++;
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] ✓ {fileName} uploaded successfully"
                                    );
                                }
                                catch (Exception ex)
                                {
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] ✗ Failed to upload {fileName}: {ex.Message}"
                                    );
                                    DebugConsole.WriteError(
                                        $"Failed to upload {fileName}: {ex.Message}"
                                    );
                                }

                                progress.ProcessedFiles++;
                                progressWindow.UpdateProgress(progress);
                            }

                            // Phase 2: Upload checksum file AFTER all saves are processed
                            if (hasChecksumFile && !string.IsNullOrEmpty(checksumFile))
                            {
                                // Check for cancellation
                                if (progressWindow.CancellationToken.IsCancellationRequested)
                                {
                                    progress.Status = "Upload cancelled by user";
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] Upload cancelled by user"
                                    );
                                    progressWindow.UpdateProgress(progress);
                                    return;
                                }

                                progress.Status = "Uploading checksum file...";
                                progress.LogMessages.Add(
                                    $"[{DateTime.Now:HH:mm:ss}] Phase 2: Processing checksum file"
                                );
                                progressWindow.UpdateProgress(progress);

                                try
                                {
                                    await _rcloneFileOperations.ProcessFile(
                                        checksumFile,
                                        remoteBasePath,
                                        stats
                                    );

                                    progress.UploadedFiles++;
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] ✓ Checksum file uploaded successfully"
                                    );
                                }
                                catch (Exception ex)
                                {
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] ✗ Failed to upload checksum file: {ex.Message}"
                                    );
                                    DebugConsole.WriteError(
                                        $"Failed to upload checksum file: {ex.Message}"
                                    );
                                }

                                progress.ProcessedFiles++;
                                progressWindow.UpdateProgress(progress);
                            }

                            var overallEndTime = DateTime.Now;
                            var totalTime = overallEndTime - overallStartTime;

                            DebugConsole.WriteKeyValue(
                                "Process completed at",
                                overallEndTime.ToString("yyyy-MM-dd HH:mm:ss.fff")
                            );
                            DebugConsole.WriteKeyValue(
                                "Total processing time",
                                $"{totalTime.TotalSeconds:F2} seconds"
                            );

                            // Mark as completed
                            progress.IsCompleted = true;
                            progress.Status =
                                $"Upload completed in {totalTime.TotalSeconds:F2} seconds";
                            progress.LogMessages.Add(
                                $"[{DateTime.Now:HH:mm:ss}] Upload completed successfully!"
                            );
                            progress.LogMessages.Add(
                                $"[{DateTime.Now:HH:mm:ss}] Total time: {totalTime.TotalSeconds:F2} seconds"
                            );
                            progress.LogMessages.Add(
                                $"[{DateTime.Now:HH:mm:ss}] Files uploaded: {progress.UploadedFiles}"
                            );
                            progressWindow.UpdateProgress(progress);

                            // Show results notification
                            await Application.Current.Dispatcher.BeginInvoke(
                                new Action(
                                    () =>
                                    {
                                        ShowUploadResults(playniteApi, stats, displayName);
                                    }
                                )
                            );
                        }
                        catch (Exception e)
                        {
                            progress.LogMessages.Add(
                                $"[{DateTime.Now:HH:mm:ss}] ✗ Upload failed: {e.Message}"
                            );
                            progress.Status = "Upload failed";
                            progressWindow.UpdateProgress(progress);

                            DebugConsole.WriteError($"Upload failed: {e.Message}");
                            DebugConsole.WriteException(e, "Upload Exception");

                            await Application.Current.Dispatcher.BeginInvoke(
                                new Action(
                                    () =>
                                    {
                                        playniteApi.Notifications.Add(
                                            new NotificationMessage(
                                                "UPLOAD_ERROR",
                                                $"Failed to upload saves for {gameArg.Name}: {e.Message}",
                                                NotificationType.Error
                                            )
                                        );
                                    }
                                )
                            );
                        }
                        finally
                        {
                            // Clean up temporary checksum file if we created it
                            if (createdChecksumFile && !string.IsNullOrEmpty(checksumFile))
                            {
                                try
                                {
                                    // Uncomment these lines if you want to delete the checksum file after upload
                                    // File.Delete(checksumFile);
                                    // DebugConsole.WriteInfo("Cleaned up temporary checksum file");
                                }
                                catch (Exception ex)
                                {
                                    DebugConsole.WriteWarning(
                                        $"Failed to clean up checksum file: {ex.Message}"
                                    );
                                }
                            }
                        }
                    },
                    progressWindow.CancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                DebugConsole.WriteInfo("Upload operation was cancelled by user");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Unexpected error during upload: {ex.Message}");
                playniteApi.Notifications.Add(
                    new NotificationMessage(
                        "UPLOAD_ERROR",
                        $"Unexpected error during upload: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
        }
        private string CalculateFileChecksum(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        } 
        // Modified Download method
public async Task<DownloadResult> Download(
    Game gameArg,
    IPlayniteAPI playniteApi,
    CloudProvider provider,
    bool overwriteExisting = false
)
{
    var data = Misc.GetGameData(gameArg);

    if (data.GameProvider != CloudProvider.Global)
    {
        provider = data.GameProvider;
    }
    DebugConsole.WriteLine("[DownloadStep] Starting download process...");

    await _rcloneInstaller.RcloneCheckAsync(playniteApi, provider);
    DebugConsole.WriteLine("[DownloadStep] Verified Rclone installation");

    // Use the game's install directory for staging, not a hardcoded path
    string stagingFolder = Path.Combine(
        playniteApi.Paths.ApplicationPath,
        gameArg.InstallDirectory,
        "SavesDownloaded"
    );
    Directory.CreateDirectory(stagingFolder);
    DebugConsole.WriteLine(
        $"[DownloadStep] Created/verified staging folder: {stagingFolder}"
    );

    string tempChecksumFile = Path.Combine(stagingFolder, ".savetracker_checksums.json");

    var overallStartTime = DateTime.Now;
    string providerName = _recloneHelper.GetProviderConfigName(provider);
    string displayName = _recloneHelper.GetProviderDisplayName(provider);

    DebugConsole.WriteSection(
        $"[DownloadStep] Download Process for {gameArg.Name} from {displayName}"
    );
    DebugConsole.WriteKeyValue(
        "[DownloadStep] Process started at",
        overallStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff")
    );
    DebugConsole.WriteKeyValue("[DownloadStep] Cloud provider", displayName);
    DebugConsole.WriteKeyValue("[DownloadStep] Overwrite existing", overwriteExisting);

    if (!File.Exists(RcloneExePath) || !File.Exists(_configPath))
    {
        string error = "[DownloadStep] Rclone is not installed or configured.";
        DebugConsole.WriteError(error);
        playniteApi.Notifications.Add(
            new NotificationMessage("RCLONE_MISSING", error, NotificationType.Error)
        );
        return new DownloadResult();
    }

    string remoteBasePath =
        $"{providerName}:PlayniteCloudSave/{SanitizeGameName(gameArg.Name)}";
    DebugConsole.WriteKeyValue("[DownloadStep] Remote base path", remoteBasePath);

    var downloadResult = new DownloadResult();

    try
    {
        var checksumRemotePath = $"{remoteBasePath}/.savetracker_checksums.json";

        DebugConsole.WriteLine("[DownloadStep] Downloading remote checksum file...");
        var downloaded = await _rcloneFileOperations.DownloadFileWithRetry(
            checksumRemotePath,
            tempChecksumFile,
            ".savetracker_checksums.json"
        );

        if (!downloaded || !File.Exists(tempChecksumFile))
        {
            DebugConsole.WriteError(
                "[DownloadStep] Failed to retrieve checksum file from remote storage."
            );
            return downloadResult;
        }

        DebugConsole.WriteLine("[DownloadStep] Reading checksum file content...");
        var checksumContent = File.ReadAllText(tempChecksumFile);
        var checksumData = JsonConvert.DeserializeObject<GameUploadData>(checksumContent);

        if (checksumData == null || checksumData.Files.Count == 0)
        {
            DebugConsole.WriteWarning(
                "[DownloadStep] No valid file entries found in checksum JSON."
            );
            return downloadResult;
        }

        DebugConsole.WriteInfo(
            $"[DownloadStep] Loaded {checksumData.Files.Count} tracked files from remote checksum file."
        );
        DebugConsole.WriteList(
            "[DownloadStep] Tracked paths",
            checksumData.Files.Values.Select(f => f.Path)
        );

        // Calculate total progress steps
        int totalSteps = checksumData.Files.Count;

        // Create and show progress window (assuming DownloadProgressWindow exists or create similar to UploadProgressWindow)
        var progressWindow = new UploadProgressWindow(gameArg.Name, displayName);

        // Initialize progress
        var progress = new UploadProgressWindow.UploadProgress
        {
            TotalFiles = totalSteps,
            Status = "Initializing download...",
            LogMessages = new List<string>
            {
                // Add initial log messages
                $"[{DateTime.Now:HH:mm:ss}] Starting download process for {gameArg.Name}",
                $"[{DateTime.Now:HH:mm:ss}] Provider: {displayName}",
                $"[{DateTime.Now:HH:mm:ss}] Total files to process: {totalSteps}",
                $"[{DateTime.Now:HH:mm:ss}] Overwrite existing files: {overwriteExisting}"
            }
        };

        progressWindow.UpdateProgress(progress);
        if (UploaderSettings.Settings.ShowDownload)
        {
            progressWindow.Show();
        }
        try
        {
            // Run download process on background thread
            await Task.Run(
                async () =>
                {
                    try
                    {
                        progress.Status = "Processing save files...";
                        progress.LogMessages.Add(
                            $"[{DateTime.Now:HH:mm:ss}] Processing {checksumData.Files.Count} tracked files"
                        );
                        progressWindow.UpdateProgress(progress);

                        DebugConsole.WriteLine("[DownloadStep] Starting file download loop...");

                        foreach (var entry in checksumData.Files)
                        {
                            // Check for cancellation
                            if (progressWindow.CancellationToken.IsCancellationRequested)
                            {
                                progress.Status = "Download cancelled by user";
                                progress.LogMessages.Add(
                                    $"[{DateTime.Now:HH:mm:ss}] Download cancelled by user"
                                );
                                progressWindow.UpdateProgress(progress);
                                return;
                            }

                            string fileName = entry.Key;
                            var record = entry.Value;
                            string originalPath = record.Path; // This should be the original path from when uploaded

                            progress.Status = $"Downloading {fileName}...";
                            progress.LogMessages.Add(
                                $"[{DateTime.Now:HH:mm:ss}] Processing: {fileName}"
                            );
                            progressWindow.UpdateProgress(progress);

                            // Determine the final destination path using ONLY remote data
                            string finalPath;
                            
                            // The remote checksum file contains the original path - use it directly
                            if (!string.IsNullOrEmpty(originalPath))
                            {
                                // Use the path from remote data - this is the authoritative source
                                string expandedPath = originalPath;
                                
                                // If it's a portable path, expand it for the current system
                                if (Misc.IsPortablePathStrict(originalPath))
                                {
                                    expandedPath = Misc.ExpandPortablePath(originalPath, gameArg);
                                    DebugConsole.WriteInfo(
                                        $"[DownloadStep] Expanded portable path: {originalPath} -> {expandedPath}"
                                    );
                                }

                                // Replace username if needed for cross-user compatibility
                                var currentUser = Environment.UserName;
                                finalPath = ReplaceUserInPath(expandedPath, currentUser);
                            }
                            else
                            {
                                // Fallback: if remote data doesn't have path info, use a default location
                                // This should rarely happen with properly uploaded files
                                string defaultSaveDir = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                    "CloudSave_Downloads",
                                    SanitizeGameName(gameArg.Name)
                                );
                                Directory.CreateDirectory(defaultSaveDir);
                                finalPath = Path.Combine(defaultSaveDir, fileName);
                                
                                DebugConsole.WriteWarning(
                                    $"[DownloadStep] No original path in remote data, using default: {finalPath}"
                                );
                                progress.LogMessages.Add(
                                    $"[{DateTime.Now:HH:mm:ss}] Warning: Using default path for {fileName}"
                                );
                            }

                            // Create staging path - preserve directory structure
                            string relativeDir = Path.GetDirectoryName(fileName) ?? "";
                            string stagingDir = Path.Combine(stagingFolder, relativeDir);
                            string stagedPath = Path.Combine(
                                stagingDir,
                                Path.GetFileName(fileName)
                            );

                            DebugConsole.WriteLine(
                                $"[DownloadStep] Processing download for: {fileName}"
                            );
                            DebugConsole.WriteKeyValue(
                                "[DownloadStep] Original path",
                                originalPath
                            );
                            DebugConsole.WriteKeyValue(
                                "[DownloadStep] Remote path",
                                $"{remoteBasePath}/{fileName}"
                            );
                            DebugConsole.WriteKeyValue("[DownloadStep] Staging path", stagedPath);
                            DebugConsole.WriteKeyValue("[DownloadStep] Final path", finalPath);

                            // Create staging directory if it doesn't exist
                            if (!Directory.Exists(stagingDir))
                            {
                                Directory.CreateDirectory(stagingDir);
                                DebugConsole.WriteLine(
                                    $"[DownloadStep] Created staging directory: {stagingDir}"
                                );
                            }

                            try
                            {
                                // Download file to staging area
                                var downloadTask = _rcloneFileOperations.ProcessDownloadFile(
                                    new Resources.Logic.RecloneManagement.RemoteFileInfo
                                    {
                                        Name = fileName,
                                        Size = record.FileSize,
                                        ModTime = record.LastUpload
                                    },
                                    stagingDir, // Use the staging directory (not the full path)
                                    remoteBasePath,
                                    null, // No longer using the old progress parameter
                                    downloadResult,
                                    overwriteExisting
                                );

                                // PROPERLY wait for download to complete
                                if (downloadTask != null)
                                {
                                    await downloadTask;
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] Download task completed for {fileName}"
                                    );
                                }
                                else
                                {
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] Warning: Download task was null for {fileName}"
                                    );
                                }

                                // Just check if the file exists in staging - ignore task status
                                if (stagedPath != null && File.Exists(stagedPath))
                                {
                                    try
                                    {
                                        DebugConsole.WriteInfo(
                                            $"Moving from staging: {stagedPath} to final: {finalPath}"
                                        );

                                        // Validate that the final path is absolute and accessible
                                        if (!Path.IsPathRooted(finalPath))
                                        {
                                            DebugConsole.WriteError(
                                                $"[DownloadStep] Final path is not absolute: {finalPath}"
                                            );
                                            downloadResult.FailedCount++;
                                            progress.LogMessages.Add(
                                                $"[{DateTime.Now:HH:mm:ss}] ✗ Final path is not absolute: {fileName}"
                                            );
                                            continue;
                                        }

                                        // Create the destination directory if it doesn't exist
                                        var destinationDirectory = Path.GetDirectoryName(finalPath);
                                        if (
                                            !string.IsNullOrEmpty(destinationDirectory)
                                            && !Directory.Exists(destinationDirectory)
                                        )
                                        {
                                            Directory.CreateDirectory(destinationDirectory);
                                            DebugConsole.WriteLine(
                                                $"[DownloadStep] Created destination directory: {destinationDirectory}"
                                            );
                                        }

                                        // Handle existing file
                                        if (File.Exists(finalPath))
                                        {
                                            if (overwriteExisting)
                                            {
                                                // Try to delete with retry logic
                                                if (
                                                    !TryDeleteFileWithRetry(
                                                        finalPath,
                                                        maxRetries: 3,
                                                        delayMs: 500
                                                    )
                                                )
                                                {
                                                    DebugConsole.WriteError(
                                                        $"[DownloadStep] Could not delete existing file after retries: {finalPath}"
                                                    );
                                                    downloadResult.FailedCount++;
                                                    progress.LogMessages.Add(
                                                        $"[{DateTime.Now:HH:mm:ss}] ✗ Could not delete existing file: {fileName}"
                                                    );
                                                    continue;
                                                }
                                                DebugConsole.WriteLine(
                                                    $"[DownloadStep] Deleted existing file: {finalPath}"
                                                );
                                                progress.LogMessages.Add(
                                                    $"[{DateTime.Now:HH:mm:ss}] Deleted existing file: {fileName}"
                                                );
                                            }
                                            else
                                            {
                                                DebugConsole.WriteWarning(
                                                    $"[DownloadStep] File already exists, skipping: {finalPath}"
                                                );
                                                progress.LogMessages.Add(
                                                    $"[{DateTime.Now:HH:mm:ss}] Skipped existing file: {fileName}"
                                                );
                                                progress.ProcessedFiles++;
                                                progressWindow.UpdateProgress(progress);
                                                continue;
                                            }
                                        }

                                        // Move the file with retry logic
                                        if (
                                            !TryMoveFileWithRetry(
                                                stagedPath,
                                                finalPath,
                                                maxRetries: 5,
                                                delayMs: 1000
                                            )
                                        )
                                        {
                                            DebugConsole.WriteError(
                                                $"[DownloadStep] Failed to move file after all retries: {fileName}"
                                            );
                                            downloadResult.FailedCount++;
                                            progress.LogMessages.Add(
                                                $"[{DateTime.Now:HH:mm:ss}] ✗ Failed to move file: {fileName}"
                                            );
                                            continue;
                                        }

                                        DebugConsole.WriteLine(
                                            $"[DownloadStep] Successfully moved {fileName} to {finalPath}"
                                        );

                                        // Verify the file was moved correctly
                                        if (File.Exists(finalPath))
                                        {
                                            var fileInfo = new FileInfo(finalPath);
                                            DebugConsole.WriteLine(
                                                $"[DownloadStep] File verified at destination. Size: {fileInfo.Length} bytes"
                                            );
                                            downloadResult.DownloadedCount++;
                                            progress.UploadedFiles++;
                                            progress.LogMessages.Add(
                                                $"[{DateTime.Now:HH:mm:ss}] ✓ {fileName} downloaded successfully"
                                            );
                                        }
                                        else
                                        {
                                            DebugConsole.WriteError(
                                                $"[DownloadStep] File move appeared to succeed but file not found at destination: {finalPath}"
                                            );
                                            downloadResult.FailedCount++;
                                            progress.LogMessages.Add(
                                                $"[{DateTime.Now:HH:mm:ss}] ✗ File not found after move: {fileName}"
                                            );
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugConsole.WriteException(
                                            ex,
                                            $"[DownloadStep] Failed to move {fileName} to final location: {finalPath}"
                                        );
                                        downloadResult.FailedCount++;
                                        progress.LogMessages.Add(
                                            $"[{DateTime.Now:HH:mm:ss}] ✗ Error moving {fileName}: {ex.Message}"
                                        );
                                    }
                                }
                                else
                                {
                                    DebugConsole.WriteError(
                                        $"[DownloadStep] Staged file not found: {stagedPath}"
                                    );
                                    downloadResult.FailedCount++;
                                    progress.LogMessages.Add(
                                        $"[{DateTime.Now:HH:mm:ss}] ✗ Staged file not found: {fileName}"
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                DebugConsole.WriteException(
                                    ex,
                                    $"[DownloadStep] Exception during download process for {fileName}"
                                );
                                downloadResult.FailedCount++;
                                progress.LogMessages.Add(
                                    $"[{DateTime.Now:HH:mm:ss}] ✗ Download failed for {fileName}: {ex.Message}"
                                );
                            }

                            progress.ProcessedFiles++;
                            progressWindow.UpdateProgress(progress);
                        }

                        var overallEndTime = DateTime.Now;
                        downloadResult.TotalTime = overallEndTime - overallStartTime;

                        DebugConsole.WriteKeyValue(
                            "[DownloadStep] Process completed at",
                            overallEndTime.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        );
                        DebugConsole.WriteKeyValue(
                            "[DownloadStep] Total time taken",
                            downloadResult.TotalTime.ToString()
                        );

                        // Mark as completed
                        progress.IsCompleted = true;
                        progress.Status =
                            $"Download completed in {downloadResult.TotalTime.TotalSeconds:F2} seconds";
                        progress.LogMessages.Add(
                            $"[{DateTime.Now:HH:mm:ss}] Download completed successfully!"
                        );
                        progress.LogMessages.Add(
                            $"[{DateTime.Now:HH:mm:ss}] Total time: {downloadResult.TotalTime.TotalSeconds:F2} seconds"
                        );
                        progress.LogMessages.Add(
                            $"[{DateTime.Now:HH:mm:ss}] Files downloaded: {progress.UploadedFiles}"
                        );
                        progressWindow.UpdateProgress(progress);

                        // Show results notification
                        await Application.Current.Dispatcher.BeginInvoke(
                            new Action(
                                () =>
                                {
                                    ShowDownloadResults(playniteApi, gameArg.Name, downloadResult);
                                }
                            )
                        );
                    }
                    catch (Exception e)
                    {
                        progress.LogMessages.Add(
                            $"[{DateTime.Now:HH:mm:ss}] ✗ Download failed: {e.Message}"
                        );
                        progress.Status = "Download failed";
                        progressWindow.UpdateProgress(progress);

                        DebugConsole.WriteError($"Download failed: {e.Message}");
                        DebugConsole.WriteException(e, "Download Exception");

                        await Application.Current.Dispatcher.BeginInvoke(
                            new Action(
                                () =>
                                {
                                    playniteApi.Notifications.Add(
                                        new NotificationMessage(
                                            "DOWNLOAD_ERROR",
                                            $"Failed to download saves for {gameArg.Name}: {e.Message}",
                                            NotificationType.Error
                                        )
                                    );
                                }
                            )
                        );
                    }
                },
                progressWindow.CancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            DebugConsole.WriteInfo("Download operation was cancelled by user");
        }
        catch (Exception ex)
        {
            DebugConsole.WriteError($"Unexpected error during download: {ex.Message}");
            playniteApi.Notifications.Add(
                new NotificationMessage(
                    "DOWNLOAD_ERROR",
                    $"Unexpected error during download: {ex.Message}",
                    NotificationType.Error
                )
            );
        }
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "[DownloadStep] Download process failed");
        playniteApi.Notifications.Add(
            new NotificationMessage(
                "RCLONE_DOWNLOAD_ERROR",
                $"Error downloading {gameArg.Name} from {displayName}: {ex.Message}",
                NotificationType.Error
            )
        );
    }

    return downloadResult;
}
        private static bool TryMoveFileWithRetry(
            string sourcePath,
            string destinationPath,
            int maxRetries = 3,
            int delayMs = 1000
        )
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Check if source file is locked before attempting move
                    if (IsFileLocked(sourcePath))
                    {
                        DebugConsole.WriteWarning(
                            $"[DownloadStep] Source file is locked, attempt {attempt}/{maxRetries}: {sourcePath}"
                        );
                        if (attempt < maxRetries)
                        {
                            Thread.Sleep(delayMs);
                            continue;
                        }
                        return false;
                    }

                    // Check if destination file is locked (if it exists)
                    if (File.Exists(destinationPath) && IsFileLocked(destinationPath))
                    {
                        DebugConsole.WriteWarning(
                            $"[DownloadStep] Destination file is locked, attempt {attempt}/{maxRetries}: {destinationPath}"
                        );
                        if (attempt < maxRetries)
                        {
                            Thread.Sleep(delayMs);
                            continue;
                        }
                        return false;
                    }

                    File.Move(sourcePath, destinationPath);
                    DebugConsole.WriteLine(
                        $"[DownloadStep] File moved successfully on attempt {attempt}"
                    );
                    return true;
                }
                catch (IOException ex) when (ex.HResult == -2147024864) // ERROR_SHARING_VIOLATION
                {
                    DebugConsole.WriteWarning(
                        $"[DownloadStep] File locked, attempt {attempt}/{maxRetries}: {ex.Message}"
                    );
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(delayMs * attempt); // Progressive delay
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    DebugConsole.WriteWarning(
                        $"[DownloadStep] Access denied, attempt {attempt}/{maxRetries}: {ex.Message}"
                    );
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(delayMs * attempt);
                    }
                }
                catch (Exception ex)
                {
                    // For other exceptions, don't retry
                    DebugConsole.WriteException(
                        ex,
                        $"[DownloadStep] Non-retryable error moving file"
                    );
                    return false;
                }
            }

            return false;
        }

        // Helper method for retrying file deletion
        private static bool TryDeleteFileWithRetry(
            string filePath,
            int maxRetries = 3,
            int delayMs = 500
        )
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (IsFileLocked(filePath))
                    {
                        DebugConsole.WriteWarning(
                            $"[DownloadStep] File is locked for deletion, attempt {attempt}/{maxRetries}: {filePath}"
                        );
                        if (attempt < maxRetries)
                        {
                            Thread.Sleep(delayMs * attempt);
                            continue;
                        }
                        return false;
                    }

                    File.Delete(filePath);
                    return true;
                }
                catch (IOException ex) when (ex.HResult == -2147024864) // ERROR_SHARING_VIOLATION
                {
                    DebugConsole.WriteWarning(
                        $"[DownloadStep] File locked for deletion, attempt {attempt}/{maxRetries}: {ex.Message}"
                    );
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(delayMs * attempt);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    DebugConsole.WriteWarning(
                        $"[DownloadStep] Access denied for deletion, attempt {attempt}/{maxRetries}: {ex.Message}"
                    );
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(delayMs * attempt);
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(
                        ex,
                        $"[DownloadStep] Non-retryable error deleting file"
                    );
                    return false;
                }
            }

            return false;
        }

        // Helper method to check if a file is locked
        private static bool IsFileLocked(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            try
            {
                using (
                    var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None)
                )
                {
                   DebugConsole.WriteLine(stream.Name);
                }
                return false;
            }
            catch (IOException)
            {
                // File is locked
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // Treat access denied as locked
                return true;
            }
        }
        public async Task<GameUploadData> DownloadChecksumData(Game gameArg, CloudProvider provider)
        {
            string tempFolder = Path.Combine(
                Path.GetTempPath(),
                "SaveTracker",
                Guid.NewGuid().ToString()
            );
            Directory.CreateDirectory(tempFolder);
            string tempChecksumFile = Path.Combine(tempFolder, ".savetracker_checksums.json");
            var data = Misc.GetGameData(gameArg);
            try
            {
                if (data.GameProvider != CloudProvider.Global)
                {
                    provider = data.GameProvider;
                }
                string providerName = _recloneHelper.GetProviderConfigName(provider);
                string remoteBasePath =
                    $"{providerName}:PlayniteCloudSave/{SanitizeGameName(gameArg.Name)}";
                string checksumRemotePath = $"{remoteBasePath}/.savetracker_checksums.json";

                var downloaded = await _rcloneFileOperations.DownloadFile(
                    checksumRemotePath,
                    tempChecksumFile
                );

                if (downloaded && File.Exists(tempChecksumFile))
                {
                    var checksumContent = File.ReadAllText(tempChecksumFile);
                    if (!string.IsNullOrWhiteSpace(checksumContent))
                    {
                        return JsonConvert.DeserializeObject<GameUploadData>(checksumContent);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempFolder))
                        Directory.Delete(tempFolder, true);
                }
                catch
                {
                    // ignored
                }
            }
        }
        private string ReplaceUserInPath(string originalPath, string currentUser)
        {
            // Find "\Users\" in a case-insensitive way
            int usersIndex = originalPath.IndexOf(@"\Users\", StringComparison.OrdinalIgnoreCase);
            if (usersIndex >= 0)
            {
                int userStart = usersIndex + @"\Users\".Length;

                // Find the next backslash after the username
                int nextSlashIndex = originalPath.IndexOf("\\", userStart, StringComparison.Ordinal);

                var oldUser = nextSlashIndex >= 0 ? originalPath.Substring(userStart, nextSlashIndex - userStart) : originalPath.Substring(userStart); // username is last part

                // Replace only if the username differs
                if (!oldUser.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                {
                    string oldSegment = nextSlashIndex >= 0 
                        ? $"\\Users\\{oldUser}\\"
                        : $"\\Users\\{oldUser}";

                    string newSegment = nextSlashIndex >= 0 
                        ? $"\\Users\\{currentUser}\\"
                        : $"\\Users\\{currentUser}";

                    return originalPath.Replace(oldSegment, newSegment);
                }
            }

            return originalPath; // No change needed
        }

        private string SanitizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return "UnknownGame";

            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' });
            string sanitized = invalidChars.Aggregate(
                gameName,
                (current, c) => current.Replace(c, '_')
            );
            return sanitized.Trim();
        }

        private void ShowUploadResults(IPlayniteAPI api, UploadStats stats, string displayName)
        {
            DebugConsole.WriteSection("Upload Results");
            DebugConsole.WriteKeyValue(
                "Uploaded",
                $"{stats.UploadedCount} files ({stats.UploadedSize:N0} bytes)"
            );
            DebugConsole.WriteKeyValue(
                "Skipped",
                $"{stats.SkippedCount} files ({stats.SkippedSize:N0} bytes)"
            );
            DebugConsole.WriteKeyValue("Failed", $"{stats.FailedCount} files");

            // Use displayName instead of gameName for user-facing messages
            string message =
                $"Upload complete for {displayName}: "
                + $"{stats.UploadedCount} uploaded, {stats.SkippedCount} skipped, {stats.FailedCount} failed.";

            var notificationType =
                stats.FailedCount > 0 ? NotificationType.Error : NotificationType.Info;

            api.Notifications.Add(
                new NotificationMessage("RCLONE_UPLOAD_COMPLETE", message, notificationType)
            );
        }

        private void ShowDownloadResults(IPlayniteAPI api, string gameName, DownloadResult result)
        {
            DebugConsole.WriteSection("Download Results");
            DebugConsole.WriteKeyValue(
                "Downloaded",
                $"{result.DownloadedCount} files ({result.DownloadedSize:N0} bytes)"
            );
            DebugConsole.WriteKeyValue(
                "Skipped",
                $"{result.SkippedCount} files ({result.SkippedSize:N0} bytes)"
            );
            DebugConsole.WriteKeyValue("Failed", $"{result.FailedCount} files");
            DebugConsole.WriteKeyValue("Total time", $"{result.TotalTime.TotalSeconds:F1} seconds");

            if (result.FailedFiles.Any())
            {
                DebugConsole.WriteList("Failed files", result.FailedFiles);
            }

            string message =
                $"Download complete for {gameName}: "
                + $"{result.DownloadedCount} downloaded, {result.SkippedCount} skipped, {result.FailedCount} failed.";

            var notificationType =
                result.FailedCount > 0 ? NotificationType.Error : NotificationType.Info;

            api.Notifications.Add(
                new NotificationMessage("RCLONE_DOWNLOAD_COMPLETE", message, notificationType)
            );
        }

        // Optimized GetRemoteGameData method
        public async Task<GameUploadData> GetRemoteGameData(Game game, CloudProvider provider)
        {
            try
            {
                RcloneExecutor executor = new RcloneExecutor();
                string providerName = _recloneHelper.GetProviderConfigName(provider);
                string remoteFilePath =
                    $"{providerName}:PlayniteCloudSave/{SanitizeGameName(game.Name)}/.savetracker_checksums.json";

                // Add performance flags to rclone command
                var result = await executor.ExecuteRcloneCommand(
                    $"cat \"{remoteFilePath}\" --config \"{_configPath}\" --no-check-certificate --disable-http2 --timeout 5s --contimeout 3s",
                    TimeSpan.FromSeconds(8)
                ); 
                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    var gameUploadData = JsonConvert.DeserializeObject<GameUploadData>(
                        result.Output
                    );
                    return gameUploadData;
                }

                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex);
                return null;
            }
        }

        // Optimized method to check if remote file exists (faster than downloading content)
        public async Task<bool> RemoteFileExists(Game game, CloudProvider provider)
        {
            try
            {
                RcloneExecutor executor = new RcloneExecutor();
                string providerName = _recloneHelper.GetProviderConfigName(provider);
                string remoteFilePath =
                    $"{providerName}:PlayniteCloudSave/{SanitizeGameName(game.Name)}/.savetracker_checksums.json";

                // Use lsjson just to check if file exists (much faster than cat)
                var result = await executor.ExecuteRcloneCommand(
                    $"lsjson \"{remoteFilePath}\" --config \"{_configPath}\" --no-check-certificate --disable-http2 --timeout 5s --contimeout 3s",
                    TimeSpan.FromSeconds(8)
                );

                return result.Success
                    && !string.IsNullOrEmpty(result.Output)
                    && result.Output.Trim() != "[]";
            }
            catch
            {
                return false;
            }
        }

        // Alternative: Use rclone's built-in JSON output for faster operations
        public async Task<GameUploadData> GetRemoteGameDataOptimized(
            Game game,
            CloudProvider provider
        )
        {
            try
            {
                // First quickly check if file exists
                if (!await RemoteFileExists(game, provider))
                    return null;

                // If exists, then download content
                return await GetRemoteGameData(game, provider);
            }
            catch
            {
                return null;
            }
        }
        // Add these classes to your existing RcloneUploader class:
        public class DownloadResult
        {
            public int DownloadedCount { get; set; }
            public int SkippedCount { get; set; }
            public int FailedCount { get; set; }
            public long DownloadedSize { get; set; }
            public long SkippedSize { get; set; }
            public TimeSpan TotalTime { get; set; }
            public List<string> FailedFiles { get; set; } = new List<string>();
        }
        public class RcloneFileInfo
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

        public class UploadStats
        {
            public int UploadedCount { get; set; }
            public int SkippedCount { get; set; }
            public int FailedCount { get; set; }
            public long UploadedSize { get; set; }
            public long SkippedSize { get; set; }
        }
    }
}
