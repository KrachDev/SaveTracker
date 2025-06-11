using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SaveTracker
{
    public class SaveTracker : GenericPlugin
    {
        private static SaveTrackerSettingsViewModel Settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("ad1eece9-6f59-460a-a7db-0d5fe5255ebd");

        public TrackLogic trackLogic = new TrackLogic()
        {
            TrackerSettings = Settings,
        };
        public RcloneUploader rcloneUploader = new RcloneUploader();

        public SaveTracker(IPlayniteAPI api) : base(api)
        {
            Settings = new SaveTrackerSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            // Enable debug console based on settings
            DebugConsole.Enable(Settings.Settings.ShowCosnoleOption);
            DebugConsole.WriteInfo("SaveTracker plugin initialized");
        }
        // Add sidebar element
        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            DebugConsole.WriteInfo("GetSidebarItems called");
    
            return new List<SidebarItem>
            {
                new SidebarItem
                {
                    Title = "Save Tracker",
                    Type = SiderbarItemType.View,
                    Icon = new BitmapImage(new Uri("pack://application:,,,/SaveTracker;component/icon.png")),
                    Opened = () => {
                        DebugConsole.WriteInfo("Save Tracker sidebar opened");
                
                        // Return your existing settings view instead of custom control
                        return new SaveTrackerSettingsView(PlayniteApi, Settings);
                    },
                    Closed = () => {
                        DebugConsole.WriteInfo("Save Tracker sidebar closed");
                    }
                }
            };
        }       

        public override async void OnGameStarted(OnGameStartedEventArgs args)
        {
            base.OnGameStarted(args);

            try
            {
                // Update console state based on current settings
                DebugConsole.Enable(Settings.Settings.ShowCosnoleOption);
                
                DebugConsole.WriteSection("Game Started");
                DebugConsole.WriteKeyValue("Game Name", args.Game.Name);
                DebugConsole.WriteKeyValue("Start Time", DateTime.Now);

                // Detect game source (Steam, Epic, etc.)
                string source = args.Game.Source?.Name ?? "Unknown";
                string pluginId = args.Game.PluginId.ToString() ?? "";

                bool isStandalone = string.IsNullOrEmpty(pluginId);

                DebugConsole.WriteKeyValue("Game Source", source);
                DebugConsole.WriteKeyValue("Plugin ID", pluginId);
                DebugConsole.WriteKeyValue("Is Standalone", isStandalone);

                if (isStandalone)
                {
                    DebugConsole.WriteInfo("Game is running as a standalone executable (no launcher)");
                }
                else
                {
                    DebugConsole.WriteInfo($"Game launched via launcher: {source}");
                }

                // Get the executable path
                string gamePath = args.SourceAction?.Path;
                DebugConsole.WriteKeyValue("Game Path", gamePath ?? "NOT FOUND");

                if (!string.IsNullOrEmpty(gamePath))
                {
                    var trackLogic = new TrackLogic(){
                        TrackerSettings = Settings,
                    };
                    string exeName = Path.GetFileName(gamePath);

                    if (Settings.Settings.TrackFiles &&  source == "Unknown")
                    {
                        DebugConsole.WriteInfo($"Starting to track executable: {exeName}");
                        await trackLogic.Track(exeName, PlayniteApi, args, Settings.Settings.ShowCosnoleOption);
                        DebugConsole.WriteSuccess("Tracking started successfully");
                    }
                    else
                    {
                        DebugConsole.WriteInfo($"Game Has a Client " + source);

                    }
                }
                else
                {
                    string errorMsg = "No executable path found";
                    DebugConsole.WriteError(errorMsg);
                    PlayniteApi.Dialogs.ShowMessage(errorMsg);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "OnGameStarted");
                PlayniteApi.Dialogs.ShowMessage($"Error starting game tracking: {ex.Message}");
            }
        }

        public async override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                if (Settings.Settings.TrackFiles)
                {
                    DebugConsole.WriteSection("Game Stopped");
                DebugConsole.WriteKeyValue("Game Name", args.Game.Name);
                DebugConsole.WriteKeyValue("Stop Time", DateTime.Now);

                DebugConsole.WriteInfo("Stopping file tracking...");
                trackLogic.StopTracking();
                
                var saveList = trackLogic.GetSaveList();
                DebugConsole.WriteKeyValue("Files Found", saveList.Count);

                if (saveList.Count == 0)
                {
                    string msg = "No save files were tracked";
                    DebugConsole.WriteWarning(msg);
                    PlayniteApi.Dialogs.ShowMessage(msg);
                }
                else
                {
                    DebugConsole.WriteList("Tracked Save Files", saveList);

                    var message = "Saves:\n" + string.Join("\n", saveList);
                    NotificationMessage msg = new NotificationMessage("Save Files", message, NotificationType.Info);
                    PlayniteApi.Notifications.Add(msg);

                    var jsonPath = Path.Combine(args.Game.InstallDirectory, "GameFiles.json");
                    DebugConsole.WriteInfo($"Saving JSON file to: {jsonPath}");

                    // Save the tracked files to JSON
                    File.WriteAllText(jsonPath,
                        JsonSerializer.Serialize(saveList,
                            new JsonSerializerOptions { WriteIndented = true }));

                    // Add the JSON file itself to the list
                    saveList.Add(jsonPath);
                    DebugConsole.WriteInfo("Added JSON file to upload list");

                    if (Settings.Settings.AutoSyncOption && Settings.Settings.TrackFiles)
                    {
                        // Upload the files to cloud
                        DebugConsole.WriteInfo("Starting cloud upload...");
                        rcloneUploader = new RcloneUploader();
                        await rcloneUploader.Upload(saveList, PlayniteApi, args.Game.Name);
                        DebugConsole.WriteSuccess("Cloud upload completed");
                    }
                   
                }

                }
                DebugConsole.WriteSection("Game Session Complete");
            }
            catch (Exception e)
            {
                DebugConsole.WriteException(e, "OnGameStopped");
                PlayniteApi.Dialogs.ShowMessage($"Error during game stop: {e.Message}");
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SaveTrackerSettingsView(PlayniteApi, Settings);
        }

        // Optional: Method to update console state when settings change
        public void UpdateConsoleState()
        {
            DebugConsole.Enable(Settings.Settings.ShowCosnoleOption);
            if (Settings.Settings.ShowCosnoleOption)
            {
                DebugConsole.WriteInfo("Debug console enabled via settings");
            }
        }
    }

   
}
