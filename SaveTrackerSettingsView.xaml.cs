using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveTracker;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagment;

namespace SaveTracker
{
    public partial class SaveTrackerSettingsView 
    {
        private IPlayniteAPI api;
        RcloneManager rclone;
        private SaveTrackerSettingsViewModel settings;

        public SaveTrackerSettingsView(IPlayniteAPI MainApi, SaveTrackerSettingsViewModel MainSettingsViewModel)
        {
            api = MainApi;
            settings = MainSettingsViewModel;
            InitializeComponent();
            DataContext = settings;
            rclone = new RcloneManager(settings)
            {
            };

        }

// First, add these classes to handle the new JSON structure

// Updated method
private async void DownloadButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var game = GetSelectedGame();

        var DownloadPath = System.IO.Path.Combine(api.Paths.ApplicationPath, game.InstallDirectory, "SavesDownloaded");
        await rclone.Download(game, api,(CloudProvider)settings.Settings.SelectedProviderIndex, true);

      
               
            
    }
    catch (Exception exception)
    {
        DebugConsole.WriteError(exception.Message);
        throw;
    }
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
    
            var jsonPath = System.IO.Path.Combine(game.InstallDirectory, ".savetracker_checksums.json");

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
                    DebugConsole.WriteWarning($"No save files found in .savetracker_checksums.json for '{game.Name}'");
                    return;
                }
        
                DebugConsole.WriteInfo($"Found {saveFilePaths.Count} save files for {game.Name}");
        
                // Pass the actual file paths to RcloneUploader
                rclone.Upload(saveFilePaths, api, game, (CloudProvider)settings.Settings.SelectedProviderIndex);
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
public class SaveTrackerData
{
    public Dictionary<string, FileChecksumRecord> Files { get; set; }
    public DateTime LastUpdated { get; set; }
}
