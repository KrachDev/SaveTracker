using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveTracker.Resources.Helpers;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;

namespace SaveTracker
{
    public partial class SaveTrackerSettingsView
    {
        private IPlayniteAPI _api;
        RcloneManager _rclone;
        private SaveTrackerSettingsViewModel _settings;

        public SaveTrackerSettingsView(
            IPlayniteAPI mainApi,
            SaveTrackerSettingsViewModel mainSettingsViewModel
        )
        {
            _api = mainApi;
            _settings = mainSettingsViewModel;
            InitializeComponent();
            DataContext = _settings;
            _rclone = new RcloneManager(_settings);
            SetData();
        }

        private async void SetData()
        {
            try
            {
                // Initialize UI to default state
                InitializeUiToDefaults();
                // Validate dependencies
                var validationResult = ValidateDependencies();
                if (!validationResult.IsValid)
                {
                    DebugConsole.WriteWarning(validationResult.ErrorMessage);
                    return;
                }

                var game = validationResult.Game;
                GameNameTxt.Text = game.Name;

                var localJsonPath = Path.Combine(
                    game.InstallDirectory,
                    ".savetracker_checksums.json"
                );
                if (!File.Exists(localJsonPath))
                {
                    SetDefaultState();
                    if (_settings.Settings.CheckRemoteSave)
                    {
                        await CheckIfRemote(game);
                    }
                    return;
                }
                // Load and validate checksum data
                var checksumData = await LoadChecksumData(localJsonPath);
                if (checksumData == null)
                {
                    SetErrorState();
                    return;
                }

                // Update UI with data
                UpdateSyncInfo(checksumData);
                UpdateFileStats(checksumData);
                UpdateDateRange(checksumData);
                UpdateTrackingSettings(checksumData);
                UpdateFileLists(checksumData, localJsonPath);
                UpdateProvider(checksumData, localJsonPath);
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
        }

        private async Task CheckIfRemote(Game game)
        {
            try
            {
                var cloudProvider = (CloudProvider)_settings.Settings.SelectedProviderIndex;

                // Show a "checking..." dialog or progress indicator
                // api.Dialogs.ShowMessage("Checking for remote save data...", "Checking", MessageBoxButton.OK);

                // First, do a quick check if the file exists (much faster than downloading content)
                bool remoteExists = await _rclone.RemoteFileExists(game, cloudProvider);

                if (!remoteExists)
                {
                    return;
                }
                else
                {
                    // Remote data exists, get the metadata for more info
                    var remoteData = await _rclone.GetRemoteGameData(game, cloudProvider);

                    string message =
                        remoteData != null
                            ? $"Save game data found for {game.Name}!\n\nDo you want to download it?"
                            : $"Remote folder exists for {game.Name}, but couldn't read metadata.\n\nDo you want to download anyway?";

                    var result = _api.Dialogs.ShowMessage(
                        message,
                        "Remote Save Found",
                        MessageBoxButton.YesNo
                    );

                    if (result == MessageBoxResult.No)
                        return;
                }

                // User wants to proceed with download
                var downloadResult = await _rclone.Download(game, _api, cloudProvider, true);

                // Show detailed feedback based on results
                if (downloadResult.FailedCount == 0 && downloadResult.DownloadedCount > 0)
                {
                    _api.Dialogs.ShowMessage(
                        $"✅ Save game for {game.Name} downloaded successfully!\n\n"
                            + $"📁 {downloadResult.DownloadedCount} files downloaded\n"
                            + $"📦 {FormatBytes(downloadResult.DownloadedSize)} downloaded\n"
                            + $"⏱️ Completed in {downloadResult.TotalTime.TotalSeconds:F1} seconds",
                        "Download Complete",
                        MessageBoxButton.OK
                    );
                }
                else if (downloadResult.FailedCount == 0 && downloadResult.SkippedCount > 0)
                {
                    _api.Dialogs.ShowMessage(
                        $"✅ Save game for {game.Name} is already up to date!\n\n"
                            + $"📁 {downloadResult.SkippedCount} files skipped (already current)\n"
                            + $"📦 {FormatBytes(downloadResult.SkippedSize)} skipped",
                        "Already Up to Date",
                        MessageBoxButton.OK
                    );
                }
                else if (downloadResult.FailedCount > 0)
                {
                    string failedFilesList =
                        downloadResult.FailedFiles.Count > 0
                            ? $"\n\nFailed files:\n• {string.Join("\n• ", downloadResult.FailedFiles.Take(5))}"
                              + (
                                  downloadResult.FailedFiles.Count > 5
                                      ? $"\n... and {downloadResult.FailedFiles.Count - 5} more"
                                      : ""
                              )
                            : "";

                    _api.Dialogs.ShowMessage(
                        $"⚠️ Download completed with some issues for {game.Name}:\n\n"
                            + $"✅ {downloadResult.DownloadedCount} files downloaded successfully\n"
                            + $"❌ {downloadResult.FailedCount} files failed\n"
                            + $"📦 {FormatBytes(downloadResult.DownloadedSize)} downloaded"
                            + failedFilesList,
                        "Download Issues",
                        MessageBoxButton.OK
                    );
                }
                else
                {
                    _api.Dialogs.ShowMessage(
                        $"ℹ️ No save game data was downloaded for {game.Name}.\n\n"
                            + "This could mean:\n"
                            + "• The remote folder is empty\n"
                            + "• All files are already up to date\n"
                            + "• There was a connection issue",
                        "No Data Downloaded",
                        MessageBoxButton.OK
                    );
                }
            }
            catch (Exception ex)
            {
                _api.Dialogs.ShowErrorMessage(
                    $"❌ Error checking remote save for {game.Name}:\n\n{ex.Message}",
                    "Error"
                );
            }
        }

        // Helper method to format file sizes nicely
        private async void UpdateProvider(GameUploadData checksumData, string localJsonPath)
        {
            foreach (ComboBoxItem item in PerGameProvider.Items)
            {
                if (item.Tag is CloudProvider provider && provider == checksumData.GameProvider)
                {
                    PerGameProvider.SelectedItem = item;
                    break;
                }
            }

            await SaveChecksumDataAsync(checksumData, localJsonPath);
        }

#region Validation Methods

        private (bool IsValid, string ErrorMessage, Game Game) ValidateDependencies()
        {
            var game = Misc.GetSelectedGame(_api);
            if (game == null)
                return (false, "Game is Null", null);

            if (_rclone == null)
                return (false, "rclone is Null", null);

            if (_api == null)
                return (false, "Api is Null", null);

            if (_settings?.Settings?.SelectedProvider == null)
                return (false, "SelectedProvider is Null", null);

            return (true, string.Empty, game);
        }

#endregion

#region Data Loading Methods

        private Task<GameUploadData> LoadChecksumData(string localJsonPath)
        {
            if (!File.Exists(localJsonPath))
            {
                return Task.FromResult<GameUploadData>(null);
            }

            try
            {
                var jsonContent = File.ReadAllText(localJsonPath);

                var serializerSettings = new JsonSerializerSettings
                {
                    Error = (sender, args) =>
                    {
                        DebugConsole.WriteWarning(
                            $"JSON Parse Error: {args.ErrorContext.Error.Message}"
                        );
                        args.ErrorContext.Handled = true;
                    }
                };

                var checksumData = JsonConvert.DeserializeObject<GameUploadData>(
                    jsonContent,
                    serializerSettings
                );

                // Ensure collections are not null
                checksumData.Files ??= new Dictionary<string, FileChecksumRecord>();
                checksumData.Blacklist ??= new Dictionary<string, FileChecksumRecord>();

                return Task.FromResult(checksumData);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError(
                    $"Error loading checksum data: {ex.Message}",
                    ex.ToString()
                );
                return Task.FromResult<GameUploadData>(null);
            }
        }

#endregion

#region UI Update Methods

        private void InitializeUiToDefaults()
        {
            LastSyncTxt.Text = "Never";
            FilesCountTxt.Text = "0";
            TotalSizeTxt.Text = "0 KB";
            NewestSaveTxt.Text = "None";
            OldestSaveTxt.Text = "None";
            CanTrackBox.IsChecked = true;
            CanUploadBox.IsChecked = true;
            MainBoarder.Visibility = Visibility.Collapsed;
            FilesList.Items.Clear();
            BlackList.Items.Clear();
        }

        private void UpdateSyncInfo(GameUploadData checksumData)
        {
            LastSyncTxt.Text = checksumData.LastUpdated.ToString("yyyy-MM-dd HH:mm");
        }

        private void UpdateFileStats(GameUploadData checksumData)
        {
            var fileCount = checksumData.Files
                .Where(
                    f =>
                        !string.Equals(
                            f.Key,
                            ".savetracker_checksums.json",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .Count();

            FilesCountTxt.Text = fileCount.ToString();

            var totalBytes = checksumData.Files.Values.Sum(f => f.FileSize);
            TotalSizeTxt.Text = FormatBytes(totalBytes);
        }

        private void UpdateDateRange(GameUploadData checksumData)
        {
            var validFiles = checksumData.Files
                .Where(
                    f =>
                        !string.Equals(
                            f.Key,
                            ".savetracker_checksums.json",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .Select(f => f.Value)
                .ToList();

            if (validFiles.Any())
            {
                var newest = validFiles.Max(f => f.LastUpload);
                var oldest = validFiles.Min(f => f.LastUpload);

                NewestSaveTxt.Text = newest.ToString("yyyy-MM-dd HH:mm");
                OldestSaveTxt.Text = oldest.ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                NewestSaveTxt.Text = "None";
                OldestSaveTxt.Text = "None";
            }
        }

        private void UpdateTrackingSettings(GameUploadData checksumData)
        {
            CanTrackBox.IsChecked = checksumData.CanTrack;
            CanUploadBox.IsChecked = checksumData.CanUploads;
        }

        private void UpdateFileLists(GameUploadData checksumData, string localJsonPath)
        {
            FilesList.Items.Clear();
            BlackList.Items.Clear();

            // Only show lists if there are items to display
            var hasFiles =
                checksumData.Files.Count > 1
                || (
                    checksumData.Files.Count == 1
                    && !checksumData.Files.ContainsKey(".savetracker_checksums.json")
                );
            var hasBlacklistItems = checksumData.Blacklist?.Count > 0;

            if (hasFiles || hasBlacklistItems)
            {
                MainBoarder.Visibility = Visibility.Visible;
                PopulateFilesList(checksumData, localJsonPath);
                PopulateBlackList(checksumData, localJsonPath);
                TrackedCountTxt.Text = $"({FilesList.Items.Count})";
            }
            else
            {
                MainBoarder.Visibility = Visibility.Collapsed;
            }
        }

        private void PopulateFilesList(GameUploadData checksumData, string localJsonPath)
        {
            foreach (
                var file in checksumData.Files
                    .Where(
                        f =>
                            !string.Equals(
                                f.Key,
                                ".savetracker_checksums.json",
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                    .OrderBy(f => f.Key)
            )
            {
                var item = CreateFileListItem(
                    file,
                    localJsonPath,
                    checksumData,
                    isBlacklisted: false
                );
                item.ToolTip = file.Value.Path;
                FilesList.Items.Add(item);
            }
        }

        private void PopulateBlackList(GameUploadData checksumData, string localJsonPath)
        {
            if (checksumData.Blacklist?.Any() == true)
            {
                foreach (var file in checksumData.Blacklist.OrderBy(f => f.Key))
                {
                    var item = CreateFileListItem(
                        file,
                        localJsonPath,
                        checksumData,
                        isBlacklisted: true
                    );
                    item.ToolTip = file.Value.Path;
                    BlackList.Items.Add(item);
                }
                BlackList.Visibility = Visibility.Visible;
                BlacklistedCountTxt.Text = $"({BlackList.Items.Count})";
            }
            else
            {
                BlackList.Visibility = Visibility.Collapsed;
            }
        }

        private ListBoxItem CreateFileListItem(
            KeyValuePair<string, FileChecksumRecord> file,
            string localJsonPath,
            GameUploadData checksumData,
            bool isBlacklisted
        )
        {
            var item = new ListBoxItem
            {
                Content =
                    $"{file.Key} | {FormatBytes(file.Value.FileSize)} | {file.Value.LastUpload:yyyy-MM-dd HH:mm} | {file.Value.Checksum}",
                Tag = file.Key
            };

            item.MouseDoubleClick += async (s, e) =>
            {
                try
                {
                    var key = (string)item.Tag;

                    if (isBlacklisted)
                    {
                        // Move from blacklist to files
                        if (checksumData.Blacklist.TryGetValue(key, out var fileData))
                        {
                            checksumData.Files[key] = fileData;
                            checksumData.Blacklist.Remove(key);
                        }
                    }
                    else
                    {
                        // Move from files to blacklist
                        if (checksumData.Files.TryGetValue(key, out var fileData))
                        {
                            checksumData.Blacklist[key] = fileData;
                            checksumData.Files.Remove(key);
                        }
                    }

                    await SaveChecksumDataAsync(checksumData, localJsonPath);
                    SetData(); // Make this async call consistent
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteError(
                        $"Error moving file between lists: {ex.Message}",
                        ex.ToString()
                    );
                }
            };

            return item;
        }

#endregion

#region Error Handling Methods

        private void SetErrorState()
        {
            LastSyncTxt.Text = "Error";
            FilesCountTxt.Text = "-";
            TotalSizeTxt.Text = "-";
            NewestSaveTxt.Text = "None";
            OldestSaveTxt.Text = "None";
        }
        private void SetDefaultState()
        {
            LastSyncTxt.Text = "None";
            FilesCountTxt.Text = "None";
            TotalSizeTxt.Text = "None";
            NewestSaveTxt.Text = "None";
            OldestSaveTxt.Text = "None";
            FilesList.Visibility = Visibility.Collapsed;
            BlackList.Visibility = Visibility.Collapsed;
            CanTrackBox.IsChecked = true;
            CanUploadBox.IsChecked = true;

            DebugConsole.WriteInfo($"Game Has No Data.");
        }
        private void HandleError(Exception ex)
        {
            LastSyncTxt.Text = "None";
            FilesCountTxt.Text = "None";
            TotalSizeTxt.Text = "None";
            NewestSaveTxt.Text = "None";
            OldestSaveTxt.Text = "None";
            FilesList.Visibility = Visibility.Collapsed;
            BlackList.Visibility = Visibility.Collapsed;
            CanTrackBox.IsChecked = true;
            CanUploadBox.IsChecked = true;

            DebugConsole.WriteError($"Error in SetData: {ex.Message}", ex.ToString());
        }

#endregion

#region Helper Methods

        private Task SaveChecksumDataAsync(GameUploadData checksumData, string localJsonPath)
        {
            try
            {
                var updatedJson = JsonConvert.SerializeObject(checksumData, Formatting.Indented);
                File.WriteAllText(localJsonPath, updatedJson);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error saving checksum data: {ex.Message}", ex.ToString());
                throw;
            }

            return Task.CompletedTask;
        }

#endregion
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        // First, add these classes to handle the new JSON structure

        // Updated method
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var game = Misc.GetSelectedGame(_api);
                await _rclone.Download(
                    game,
                    _api,
                    (CloudProvider)_settings.Settings.SelectedProviderIndex,
                    true
                );
            }
            catch (Exception exception)
            {
                DebugConsole.WriteError(exception.Message);
                throw;
            }
        }

        private void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var game = Misc.GetSelectedGame(_api);

            if (game == null)
            {
                DebugConsole.WriteWarning("Please select a game first.", "No Game Selected");
                return;
            }

            if (string.IsNullOrEmpty(game.InstallDirectory))
            {
                DebugConsole.WriteWarning(
                    $"Game '{game.Name}' has no install directory.",
                    "Invalid Game"
                );
                return;
            }

            var jsonPath = Path.Combine(
                game.InstallDirectory,
                ".savetracker_checksums.json"
            );

            if (!File.Exists(jsonPath))
            {
                _api.Dialogs.ShowMessage(
                    $"No Saves Found For {game.Name}, Open the game first and try to save then try again"
                );
                DebugConsole.WriteWarning($"No Saves Found For {game.Name}");
                return;
            }

            try
            {
                // Read the JSON file content
                var jsonContent = File.ReadAllText(jsonPath);

                // Deserialize to get the list of file paths
                var saveFilePaths = JsonConvert.DeserializeObject<List<string>>(jsonContent);

                if (saveFilePaths == null || !saveFilePaths.Any())
                {
                    DebugConsole.WriteWarning(
                        $"No save files found in .savetracker_checksums.json for '{game.Name}'"
                    );
                    return;
                }

                DebugConsole.WriteInfo($"Found {saveFilePaths.Count} save files for {game.Name}");

                // Pass the actual file paths to RcloneUploader
                _ = _rclone.Upload(
                    saveFilePaths,
                    _api,
                    game,
                    (CloudProvider)_settings.Settings.SelectedProviderIndex
                );
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error processing saves for {game.Name}: {ex.Message}");
                _api.Dialogs.ShowErrorMessage($"Error processing saves: {ex.Message}", "Error");
            }
        }
        
        private void SaveBTN_OnClick(object sender, RoutedEventArgs e)
        {
            // Assuming 'settings' refers to your SaveTrackerSettingsViewModel instance
            // Option 1: If you want to validate and save settings
            if (_settings.VerifySettings(out List<string> errors))
            {
                _settings.EndEdit(); // This calls plugin.SavePluginSettings(Settings) internally
                // Optional: Show success message or close dialog
                // MessageBox.Show("Settings saved successfully!");
                // this.Close(); // If this is a dialog window
            }
            else
            {
                // Handle validation errors
                string errorMessage = string.Join("\n", errors);
                MessageBox.Show(
                    $"Please fix the following errors:\n{errorMessage}",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }

            if (_settings.Settings.ShowConsoleOption)
            {
                DebugConsole.ShowConsole();
            }
            else
            {
                DebugConsole.HideConsole();
            }

            DebugConsole.WriteInfo(
                "Cloud Provider: " + (CloudProvider)_settings.Settings.SelectedProviderIndex
            );
        }

        private void CanTrackBOX_OnClick(object sender, RoutedEventArgs e)
        {
            var game = Misc.GetSelectedGame(_api);
            var jsonPath = Path.Combine(game.InstallDirectory, ".savetracker_checksums.json");

            if (!File.Exists(jsonPath))
                return;

            var jsonContent = File.ReadAllText(jsonPath);
            var checksumData = JsonConvert.DeserializeObject<GameUploadData>(jsonContent);

            if (checksumData == null)
                return;

            // update value
            checksumData.CanTrack = CanTrackBox.IsChecked ?? true;

            // save back to file
            var updatedJson = JsonConvert.SerializeObject(checksumData, Formatting.Indented);
            File.WriteAllText(jsonPath, updatedJson);
        }

        private void CanUploadBOX_OnClick(object sender, RoutedEventArgs e)
        {
            var game = Misc.GetSelectedGame(_api);
            var jsonPath = Path.Combine(game.InstallDirectory, ".savetracker_checksums.json");

            if (!File.Exists(jsonPath))
                return;

            var jsonContent = File.ReadAllText(jsonPath);
            var checksumData = JsonConvert.DeserializeObject<GameUploadData>(jsonContent);

            if (checksumData == null)
                return;

            // update value
            checksumData.CanUploads = CanUploadBox.IsChecked ?? true;

            // save back to file
            var updatedJson = JsonConvert.SerializeObject(checksumData, Formatting.Indented);
            File.WriteAllText(jsonPath, updatedJson);
        }

        private async void TESTBTN_OnClick(object sender, RoutedEventArgs e)
        {
            var proList = await ProcessMonitor.GetProcessFromDir("C:\\Games\\XOutput");
            DebugConsole.WriteSuccess(proList, "GOT THE PATH");
        }

        private void PerGameProvider_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var game = Misc.GetSelectedGame(_api);
            var jsonPath = Path.Combine(game.InstallDirectory, ".savetracker_checksums.json");

            if (!File.Exists(jsonPath))
                return;

            var jsonContent = File.ReadAllText(jsonPath);
            var checksumData = JsonConvert.DeserializeObject<GameUploadData>(jsonContent);

            if (checksumData == null)
                return;

            if (
                PerGameProvider.SelectedItem is ComboBoxItem selectedItem
                && selectedItem.Tag is CloudProvider provider
            )
            {
                checksumData.GameProvider = provider;
            }
            // save back to file
            var updatedJson = JsonConvert.SerializeObject(checksumData, Formatting.Indented);
            File.WriteAllText(jsonPath, updatedJson);
        }
    }
}
