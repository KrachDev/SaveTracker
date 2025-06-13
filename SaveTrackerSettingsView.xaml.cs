using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using Path = System.Windows.Shapes.Path;
using System.IO;

namespace SaveTracker
{
    public partial class SaveTrackerSettingsView : UserControl
    {
        private IPlayniteAPI api;
        RcloneUploader rclone = new RcloneUploader();
        private SaveTrackerSettingsViewModel settings;

        public SaveTrackerSettingsView(IPlayniteAPI MainApi, SaveTrackerSettingsViewModel MainSettingsViewModel)
        {
            api = MainApi;
            settings = MainSettingsViewModel;
            InitializeComponent();
            DataContext = settings;
            rclone = new RcloneUploader(){UploaderSettings = settings};

        }

       private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
 
            var game = GetSelectedGame();

    var DownloadPath = System.IO.Path.Combine(api.Paths.ApplicationPath, game.InstallDirectory, "SavesDownloaded");
    await rclone.Download(game.Name, DownloadPath, api,(CloudProvider)settings.Settings.SelectedProviderIndex, true);
    var SaveListJson = System.IO.Path.Combine(DownloadPath, "GameFiles.json");
    
    // Read the JSON file content
    var jsonContent = File.ReadAllText(SaveListJson);

    // Deserialize to get the list of file paths
    var saveFilePaths = JsonConvert.DeserializeObject<List<string>>(jsonContent);
    
    // Get all downloaded files except JSON files
    var downloadedFiles = Directory.GetFiles(DownloadPath)
        .Where(file => !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        .ToList();
    
    // Get current username for path replacement
    var currentUser = Environment.UserName;
    
    // Copy each downloaded file to its corresponding location
    foreach (var downloadedFile in downloadedFiles)
    {
        try
        {
            var fileName = System.IO.Path.GetFileName(downloadedFile);
            
            // Find matching path in JSON by filename
            var matchingJsonPath = saveFilePaths.FirstOrDefault(path => 
                System.IO. Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase));
            
            if (matchingJsonPath != null)
            {
                // Replace username in path if different
                var destinationPath = ReplaceUserInPath(matchingJsonPath, currentUser);
                
                // Create the destination directory if it doesn't exist
                var destinationDirectory = System.IO.Path.GetDirectoryName(destinationPath);
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }
                
                // Copy the file
                File.Copy(downloadedFile, destinationPath, overwrite: true);
                DebugConsole.WriteLine($"Copied: {fileName} -> {destinationPath}");
            }
            else
            {
                DebugConsole.WriteLine($"No matching path found in JSON for file: {fileName}");
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"Error copying file {System.IO.Path.GetFileName(downloadedFile)}: {ex.Message}");
        }
    }
            }
            catch (Exception exception)
            {
                DebugConsole.WriteError(exception.Message);
                throw;
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

        private void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var game = GetSelectedGame();
    
            if (game == null)
            {
                DebugConsole.WriteWarning("Please select a game first.", "No Game Selected");
                return;
            }
    
            if (string.IsNullOrEmpty(game.InstallDirectory))
            {
                DebugConsole.WriteWarning($"Game '{game.Name}' has no install directory.", "Invalid Game");
                return;
            }
    
            var jsonPath = System.IO.Path.Combine(game.InstallDirectory, "GameFiles.json");

            if (!File.Exists(jsonPath))
            {
                api.Dialogs.ShowMessage(
                    $"No Saves Found For {game.Name}, Open the game first and try to save then try again");
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
                    DebugConsole.WriteWarning($"No save files found in GameFiles.json for '{game.Name}'");
                    return;
                }
        
                DebugConsole.WriteInfo($"Found {saveFilePaths.Count} save files for {game.Name}");
        
                // Pass the actual file paths to RcloneUploader
                rclone.Upload(saveFilePaths, api, game.Name, (CloudProvider)settings.Settings.SelectedProviderIndex);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error processing saves for {game.Name}: {ex.Message}");
                api.Dialogs.ShowErrorMessage($"Error processing saves: {ex.Message}", "Error");
            }
        }
        
        private Game GetSelectedGame()
        {
            // Get currently selected game from main view
            var selectedGames = api.MainView.SelectedGames;
    
            if (selectedGames != null && selectedGames.Any())
            {
                return selectedGames.First();
            }
    
            return null;
        }

        private void SaveBTN_OnClick(object sender, RoutedEventArgs e)
        {
            // Assuming 'settings' refers to your SaveTrackerSettingsViewModel instance
            // Option 1: If you want to validate and save settings
            if (settings.VerifySettings(out List<string> errors))
            {
                settings.EndEdit(); // This calls plugin.SavePluginSettings(Settings) internally
        
                // Optional: Show success message or close dialog
                // MessageBox.Show("Settings saved successfully!");
                // this.Close(); // If this is a dialog window
            }
            else
            {
                // Handle validation errors
                string errorMessage = string.Join("\n", errors);
                MessageBox.Show($"Please fix the following errors:\n{errorMessage}", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (settings.Settings.ShowCosnoleOption)
            {
                DebugConsole.ShowConsole();
            }
            else
            {
                DebugConsole.HideConsole();

            }
            
            DebugConsole.WriteInfo("Cloud Provider: " + (CloudProvider)settings.Settings.SelectedProviderIndex);
        }
    }
}