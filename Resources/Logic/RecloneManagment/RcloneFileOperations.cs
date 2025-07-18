using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playnite.SDK;
using System.Security.Cryptography;
using Playnite.SDK.Models;

namespace SaveTracker.Resources.Logic.RecloneManagment
{
    public class RcloneFileOperations
    {
        RcloneExecutor _executor = new RcloneExecutor();
        private readonly IPlayniteAPI _playniteAPI;

        public static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");
        public static readonly string ToolsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools");
        public readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");
        public readonly int _maxRetries = 3;
        public readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
        public readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(10);

        public RcloneFileOperations(IPlayniteAPI playniteAPI)
        {
            _playniteAPI = playniteAPI;
        }
        
        private Game GetSelectedGame()
        {
            // Get currently selected game from main view
            var selectedGames = _playniteAPI.MainView.SelectedGames;
    
            if (selectedGames != null && selectedGames.Any())
            {
                return selectedGames.First();
            }
    
            return null;
        }

        /// <summary>
        /// Gets the checksum file path for a specific game directory
        /// </summary>
        private string GetChecksumFilePath(string gameDirectory)
        {
            return Path.Combine(gameDirectory, ".savetracker_checksums.json");
        }

        /// <summary>
        /// Ensures the game directory exists and is accessible
        /// </summary>
        private bool EnsureGameDirectoryExists(string gameDirectory)
        {
            try
            {
                if (!Directory.Exists(gameDirectory))
                {
                    Directory.CreateDirectory(gameDirectory);
                    DebugConsole.WriteInfo($"Created game directory: {gameDirectory}");
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to create/access game directory: {gameDirectory}");
                return false;
            }
        }

        public async Task ProcessFile(string filePath, string remoteBasePath,
                                     GlobalProgressActionArgs prog, RcloneManager.UploadStats stats)
        {
            string fileName = Path.GetFileName(filePath);
            string remotePath = $"{remoteBasePath}/{fileName}";
            var game = GetSelectedGame();
            
            if (game == null)
            {
                DebugConsole.WriteError("No game selected - cannot determine game directory");
                stats.FailedCount++;
                return;
            }

            string gameDirectory = game.InstallDirectory;
            if (string.IsNullOrEmpty(gameDirectory))
            {
                DebugConsole.WriteError("Game install directory is not set");
                stats.FailedCount++;
                return;
            }

            if (!EnsureGameDirectoryExists(gameDirectory))
            {
                DebugConsole.WriteError($"Cannot access game directory: {gameDirectory}");
                stats.FailedCount++;
                return;
            }

            DebugConsole.WriteSeparator('-', 40);
            DebugConsole.WriteInfo($"Processing: {fileName}");
            DebugConsole.WriteKeyValue("Game directory", gameDirectory);

            try
            {
                prog.Text = $"Checking {fileName}...";

                var fileInfo = new FileInfo(filePath);
                DebugConsole.WriteKeyValue("File size", $"{fileInfo.Length:N0} bytes");
                DebugConsole.WriteKeyValue("Last modified",
                                         fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));

                bool needsUpload = await ShouldUploadFileWithChecksum(filePath, remotePath, gameDirectory);

                if (!needsUpload)
                {
                    DebugConsole.WriteSuccess($"SKIPPED: {fileName} - Identical to last uploaded version");
                    stats.SkippedCount++;
                    stats.SkippedSize += fileInfo.Length;
                    return;
                }

                prog.Text = $"Uploading {fileName}...";
                DebugConsole.WriteInfo($"UPLOADING: {fileName}");

                bool uploadSuccess = await UploadFileWithRetry(filePath, remotePath, fileName);

                if (uploadSuccess)
                {
                    // Update checksum tracking after successful upload
                    await UpdateFileChecksumRecord(filePath, gameDirectory);
                    
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

        private async Task<bool> ShouldUploadFileWithChecksum(string localFilePath, string remotePath, string gameDirectory)
        {
            try
            {
                // Get current file checksum
                string currentChecksum = await GetFileChecksum(localFilePath);
                if (string.IsNullOrEmpty(currentChecksum))
                {
                    DebugConsole.WriteWarning("Could not compute local file checksum - uploading to be safe");
                    return true;
                }

                DebugConsole.WriteDebug($"Current file MD5: {currentChecksum}");

                // Get stored checksum from JSON in game directory
                string storedChecksum = await GetStoredFileChecksum(localFilePath, gameDirectory);
                
                if (string.IsNullOrEmpty(storedChecksum))
                {
                    DebugConsole.WriteInfo("No stored checksum found - upload needed");
                    return true;
                }

                DebugConsole.WriteDebug($"Stored MD5: {storedChecksum}");

                // Compare checksums
                bool different = !currentChecksum.Equals(storedChecksum, StringComparison.OrdinalIgnoreCase);
                
                if (different)
                {
                    DebugConsole.WriteInfo("File has changed since last upload - upload needed");
                }
                else
                {
                    DebugConsole.WriteInfo("File unchanged since last upload - skipping");
                }

                return different;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Checksum comparison failed - uploading to be safe");
                return true;
            }
        }

        private async Task<string> GetFileChecksum(string filePath)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                var hashBytes = await Task.Run(() => md5.ComputeHash(stream));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to compute file checksum");
                return null;
            }
        }

        private async Task<string> GetStoredFileChecksum(string filePath, string gameDirectory)
        {
            try
            {
                var checksumData = await LoadChecksumData(gameDirectory);
                string fileName = Path.GetFileName(filePath);
                
                if (checksumData.Files.TryGetValue(fileName, out var fileRecord))
                {
                    DebugConsole.WriteDebug($"Found stored checksum for {fileName} from {fileRecord.LastUpload:yyyy-MM-dd HH:mm:ss}");
                    return fileRecord.Checksum;
                }

                DebugConsole.WriteDebug($"No stored checksum found for {fileName}");
                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to get stored checksum");
                return null;
            }
        }

        private async Task UpdateFileChecksumRecord(string filePath, string gameDirectory)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                string checksum = await GetFileChecksum(filePath);
                
                if (string.IsNullOrEmpty(checksum))
                {
                    DebugConsole.WriteWarning($"Could not compute checksum for {fileName} - skipping record update");
                    return;
                }

                var checksumData = await LoadChecksumData(gameDirectory);
                
                checksumData.Files[fileName] = new FileChecksumRecord
                {
                    Checksum = checksum,
                    LastUpload = DateTime.UtcNow,
                    FileSize = new FileInfo(filePath).Length,
                    Path = filePath
                };


                await SaveChecksumData(checksumData, gameDirectory);
                
                DebugConsole.WriteDebug($"Updated checksum record for {fileName} in {gameDirectory}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to update checksum record");
            }
        }

        private async Task<ChecksumData> LoadChecksumData(string gameDirectory)
        {
            try
            {
                string checksumFilePath = GetChecksumFilePath(gameDirectory);
                
                if (!File.Exists(checksumFilePath))
                {
                    DebugConsole.WriteDebug($"Checksum file doesn't exist at {checksumFilePath} - creating new one");
                    return new ChecksumData();
                }

                string jsonContent =  File.ReadAllText(checksumFilePath);
                var data = JsonConvert.DeserializeObject<ChecksumData>(jsonContent);
                
                DebugConsole.WriteDebug($"Loaded checksum data from {checksumFilePath} with {data?.Files?.Count ?? 0} file records");
                return data ?? new ChecksumData();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to load checksum data from {gameDirectory} - creating new");
                return new ChecksumData();
            }
        }

        private async Task SaveChecksumData(ChecksumData data, string gameDirectory)
        {
            try
            {
                string checksumFilePath = GetChecksumFilePath(gameDirectory);
                
                // Ensure directory exists
                if (!EnsureGameDirectoryExists(gameDirectory))
                {
                    throw new DirectoryNotFoundException($"Cannot access game directory: {gameDirectory}");
                }
                
                string jsonContent = JsonConvert.SerializeObject(data, Formatting.Indented);
                 File.WriteAllText(checksumFilePath, jsonContent);
                
                DebugConsole.WriteDebug($"Saved checksum data to {checksumFilePath}");
                DebugConsole.WriteInfo($"Checksum file updated with {data.Files.Count} file records");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to save checksum data to {gameDirectory}");
            }
        }

        public async Task<bool> UploadFileWithRetry(string localPath, string remotePath, string fileName)
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    DebugConsole.WriteDebug($"Upload attempt {attempt}/{_maxRetries} for {fileName}");

                    var result = await _executor.ExecuteRcloneCommand(
                        $"copyto \"{localPath}\" \"{remotePath}\" --config \"{_configPath}\" --progress",
                        _processTimeout);

                    if (result.Success)
                    {
                        DebugConsole.WriteSuccess($"Upload successful on attempt {attempt}");
                        return true;
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Attempt {attempt} failed: {result.Error}");

                        if (attempt < _maxRetries)
                        {
                            DebugConsole.WriteInfo($"Waiting {_retryDelay.TotalSeconds} seconds before retry...");
                            await Task.Delay(_retryDelay);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Upload attempt {attempt} exception");

                    if (attempt < _maxRetries)
                    {
                        await Task.Delay(_retryDelay);
                    }
                }
            }

            return false;
        }

        // Legacy method for backward compatibility - now simplified
        public async Task<bool> ShouldUploadFile(string localFilePath, string remotePath)
        {
            try
            {
                // Just check if remote file exists for basic compatibility
                bool remoteExists = await RemoteFileExists(remotePath);
                if (!remoteExists)
                {
                    DebugConsole.WriteInfo("Remote file doesn't exist - upload needed");
                    return true;
                }

                DebugConsole.WriteWarning("Using legacy upload check - consider using checksum-based method");
                return false; // Assume no upload needed for legacy calls
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Legacy file check failed - uploading to be safe");
                return true;
            }
        }

        public async Task<bool> RemoteFileExists(string remotePath)
        {
            try
            {
                var result = await _executor.ExecuteRcloneCommand($"lsl \"{remotePath}\" --config \"{_configPath}\"",
                    TimeSpan.FromSeconds(15));
                return result.Success && !string.IsNullOrWhiteSpace(result.Output);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check remote file existence");
                return false;
            }
        }

        public async Task<List<RemoteFileInfo>> GetRemoteFileList(string remotePath)
        {
            DebugConsole.WriteInfo($"Getting remote file list from: {remotePath}");

            try
            {
                var result = await _executor.ExecuteRcloneCommand($"lsjson \"{remotePath}\" --config \"{_configPath}\"",
                    TimeSpan.FromSeconds(60));

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

                var files = JsonConvert.DeserializeObject<List<RcloneManager.RcloneFileInfo>>(result.Output);
                var remoteFiles =
                    files?.Where(f => !f.IsDir)
                        .Select(f => new RemoteFileInfo { Name = f.Name, Size = f.Size, ModTime = f.ModTime })
                        .ToList() ??
                    new List<RemoteFileInfo>();

                DebugConsole.WriteSuccess($"Found {remoteFiles.Count} files in remote directory");
                return remoteFiles;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to parse remote file list");
                return new List<RemoteFileInfo>();
            }
        }

        public async Task ProcessDownloadFile(RemoteFileInfo remoteFile, string localDownloadPath,
            string remoteBasePath, GlobalProgressActionArgs prog,
            RcloneManager.DownloadResult downloadResult, bool overwriteExisting)
        {
            string localFilePath = Path.Combine(localDownloadPath, remoteFile.Name);
            string remoteFilePath = $"{remoteBasePath}/{remoteFile.Name}";

            // Get the game directory for checksum tracking
            var game = GetSelectedGame();
            string gameDirectory = game?.InstallDirectory;
            
            if (string.IsNullOrEmpty(gameDirectory))
            {
                DebugConsole.WriteWarning("Game directory not available - checksum tracking disabled for download");
                gameDirectory = localDownloadPath; // Fallback to download path
            }

            DebugConsole.WriteSeparator('-', 40);
            DebugConsole.WriteInfo($"Processing: {remoteFile.Name}");
            DebugConsole.WriteKeyValue("Game directory", gameDirectory);

            try
            {
                prog.Text = $"Checking {remoteFile.Name}...";

                DebugConsole.WriteKeyValue("Remote file size", $"{remoteFile.Size:N0} bytes");
                DebugConsole.WriteKeyValue("Remote modified",
                    remoteFile.ModTime.ToString("yyyy-MM-dd HH:mm:ss"));

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
                    // Update checksum record after successful download using game directory
                    await UpdateFileChecksumRecord(localFilePath, gameDirectory);
                    
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

                // For downloads, we can still do basic file existence check
                // The checksum will be updated after successful download
                DebugConsole.WriteInfo("Local file exists and overwrite disabled - skipping download");
                return false;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Download check failed - downloading to be safe");
                return true;
            }
        }

        public async Task<bool> DownloadFileWithRetry(string remotePath, string localPath, string fileName)
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    DebugConsole.WriteDebug($"Download attempt {attempt}/{_maxRetries} for {fileName}");

                    var result = await _executor.ExecuteRcloneCommand(
                        $"copyto \"{remotePath}\" \"{localPath}\" --config \"{_configPath}\" --progress",
                        _processTimeout);

                    if (result.Success)
                    {
                        DebugConsole.WriteSuccess($"Download successful on attempt {attempt}");
                        return true;
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Attempt {attempt} failed: {result.Error}");

                        if (attempt < _maxRetries)
                        {
                            DebugConsole.WriteInfo($"Waiting {_retryDelay.TotalSeconds} seconds before retry...");
                            await Task.Delay(_retryDelay);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Download attempt {attempt} exception");

                    if (attempt < _maxRetries)
                    {
                        await Task.Delay(_retryDelay);
                    }
                }
            }

            return false;
        }

        // Utility method to clean up old checksum records
        public async Task CleanupChecksumRecords(string gameDirectory, TimeSpan maxAge)
        {
            try
            {
                var checksumData = await LoadChecksumData(gameDirectory);
                var cutoffDate = DateTime.UtcNow - maxAge;
                
                var filesToRemove = checksumData.Files
                    .Where(kvp => kvp.Value.LastUpload < cutoffDate)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var fileName in filesToRemove)
                {
                    checksumData.Files.Remove(fileName);
                }

                if (filesToRemove.Any())
                {
                    await SaveChecksumData(checksumData, gameDirectory);
                    DebugConsole.WriteInfo($"Cleaned up {filesToRemove.Count} old checksum records from {gameDirectory}");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to cleanup checksum records");
            }
        }

        // Utility method to get checksum file info
        public string GetChecksumFileInfo(string gameDirectory)
        {
            try
            {
                string checksumFilePath = GetChecksumFilePath(gameDirectory);
                if (File.Exists(checksumFilePath))
                {
                    var fileInfo = new FileInfo(checksumFilePath);
                    return $"Checksum file: {checksumFilePath} (Size: {fileInfo.Length} bytes, Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss})";
                }
                else
                {
                    return $"Checksum file not found at: {checksumFilePath}";
                }
            }
            catch (Exception ex)
            {
                return $"Error accessing checksum file: {ex.Message}";
            }
        }
    }

    // Data classes for checksum tracking
    public class ChecksumData
    {
        public Dictionary<string, FileChecksumRecord> Files { get; set; } = new Dictionary<string, FileChecksumRecord>();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public class FileChecksumRecord
    {
        public string Checksum { get; set; }
        public DateTime LastUpload { get; set; }
        public string Path { get; set; }
        public long FileSize { get; set; }
    }

    public class RemoteFileInfo
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime ModTime { get; set; }
    }
}