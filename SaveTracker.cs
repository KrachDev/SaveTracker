using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using SaveTracker.Resources.Logic;

namespace SaveTracker
{
    public class SaveTracker : GenericPlugin
    {
        private static SaveTrackerSettingsViewModel Settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("ad1eece9-6f59-460a-a7db-0d5fe5255ebd");
        private static System.Windows.Shapes.Path _cloudIcon;

        private readonly TrackLogic _trackLogic = new TrackLogic()
        {
            TrackerSettings = Settings,
        };

        private RcloneUploader _rcloneUploader = new RcloneUploader();

        public SaveTracker(IPlayniteAPI api) : base(api)
        {
            Settings = new SaveTrackerSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
            var selectedProvider = (CloudProvider)Settings.Settings.SelectedProviderIndex;
            _rcloneUploader.UploaderSettings = Settings;
            // Enable debug console based on settings
            DebugConsole.Enable(Settings.Settings.ShowCosnoleOption);
            DebugConsole.WriteInfo("SaveTracker plugin initialized");
            DebugConsole.WriteInfo($"Cloud Provider: {selectedProvider}");

        }
        // Add sidebar element
        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            DebugConsole.WriteInfo("GetSidebarItems called");
            // SVG path from IcoMoon
            var svgPathData = "M512 328.771c0-41.045-28.339-75.45-66.498-84.74-1.621-64.35-54.229-116.031-118.931-116.031-37.896 0-71.633 17.747-93.427 45.366-12.221-15.799-31.345-25.98-52.854-25.98-36.905 0-66.821 29.937-66.821 66.861 0 3.218 0.24 6.38 0.682 9.477-5.611-1.012-11.383-1.569-17.285-1.569-53.499-0.001-96.866 43.393-96.866 96.921 0 53.531 43.367 96.924 96.865 96.924l328.131-0.006c48.069-0.092 87.004-39.106 87.004-87.223z";

            var pathGeometry = Geometry.Parse(svgPathData);

            var cloudIcon = new System.Windows.Shapes.Path
            {
                Data = pathGeometry,
                Fill = Brushes.Gray, // You can customize the color
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform
            };
            return new List<SidebarItem>
            {
                new SidebarItem
                {
                    Title = "Save Tracker",
                    Type = SiderbarItemType.View,
                    Icon = cloudIcon,     
                    Opened = () => {
                        DebugConsole.WriteInfo("Save Tracker sidebar opened");
                        cloudIcon.Fill = Brushes.White;

                        // Return your existing settings view instead of custom control
                        return new SaveTrackerSettingsView(PlayniteApi, Settings);
                    },
                    
                    Closed = () => {
                        DebugConsole.WriteInfo("Save Tracker sidebar closed");
                        cloudIcon.Fill = Brushes.Gray;

                    }
                }
            };
        }

        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {DebugConsole.WriteLine("Generating TopPanelItems for current view...");

            // SVG path from IcoMoon
            var pathIDle = "M512 328.771c0-41.045-28.339-75.45-66.498-84.74-1.621-64.35-54.229-116.031-118.931-116.031-37.896 0-71.633 17.747-93.427 45.366-12.221-15.799-31.345-25.98-52.854-25.98-36.905 0-66.821 29.937-66.821 66.861 0 3.218 0.24 6.38 0.682 9.477-5.611-1.012-11.383-1.569-17.285-1.569-53.499-0.001-96.866 43.393-96.866 96.921 0 53.531 43.367 96.924 96.865 96.924l328.131-0.006c48.069-0.092 87.004-39.106 87.004-87.223z";

            var pathGeometry = Geometry.Parse(pathIDle);

             _cloudIcon = new System.Windows.Shapes.Path
            {
                Data = pathGeometry,
                Fill = Brushes.White, // You can customize the color
                Stroke = Brushes.Gray,
                StrokeThickness = 2,
                Width = 30,
                Height = 30,
                Stretch = Stretch.Uniform
            };

            yield return new TopPanelItem
            {
                Icon = _cloudIcon,
                Title = "Cloud Dev",
               
            };
        }




        public override async void OnGameStarted(OnGameStartedEventArgs args)
        {
            try
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
                    string pluginId = args.Game.PluginId.ToString();

                    bool isStandalone = string.IsNullOrEmpty(pluginId);

                    DebugConsole.WriteKeyValue("Game Source", source);
                    DebugConsole.WriteKeyValue("Plugin ID", pluginId);
                    DebugConsole.WriteKeyValue("Is Standalone", isStandalone);

                    DebugConsole.WriteInfo(isStandalone
                        ? "Game is running as a standalone executable (no launcher)"
                        : $"Game launched via launcher: {source}");

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
            catch (Exception e)
            {
                DebugConsole.WriteException(e, "OnGameStarted");
            }
        }

        public override async void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                string source = args.Game.Source?.Name ?? "Unknown";

                if (Settings.Settings.TrackFiles &&  source == "Unknown")
                {
                    DebugConsole.WriteSection("Game Stopped");
                DebugConsole.WriteKeyValue("Game Name", args.Game.Name);
                DebugConsole.WriteKeyValue("Stop Time", DateTime.Now);

                DebugConsole.WriteInfo("Stopping file tracking...");
                _trackLogic.StopTracking();
                
                var saveList = TrackLogic.GetSaveList();
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
                    await SafeWriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(saveList, Formatting.Indented));


                    // Add the JSON file itself to the list
                    saveList.Add(jsonPath);
                    DebugConsole.WriteInfo("Added JSON file to upload list");

                    if (Settings.Settings.AutoSyncOption && Settings.Settings.TrackFiles)
                    {
                        // Upload the files to cloud
                        DebugConsole.WriteInfo("Starting cloud upload...");
                        _rcloneUploader = new RcloneUploader(){            UploaderSettings = Settings
                        };
                        

                        await _rcloneUploader.Upload(saveList, PlayniteApi, args.Game.Name, (CloudProvider)Settings.Settings.SelectedProviderIndex);
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

        private static async Task SafeWriteAllTextAsync(string path, string content, int retries = 5, int delayMs = 200)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    File.WriteAllText(path, content);
                    return;
                }
                catch (IOException)
                {
                    if (i == retries - 1) throw;
                    await Task.Delay(delayMs);
                }
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
