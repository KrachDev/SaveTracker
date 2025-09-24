using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SaveTracker.Resources.Helpers;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;

namespace SaveTracker
{
    public class SaveTracker : GenericPlugin
    {
        private static SaveTrackerSettingsViewModel Settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("ad1eece9-6f59-460a-a7db-0d5fe5255ebd");
        private static System.Windows.Shapes.Path _cloudIcon;

        private readonly TrackLogic _trackLogic = new TrackLogic() { TrackerSettings = Settings, };

        private RcloneManager _rcloneManager;

        public new static IPlayniteAPI PlayniteApi { get; set; }
        public SaveTracker(IPlayniteAPI api) : base(api)
        {
            Settings = new SaveTrackerSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };
            PlayniteApi = api;
            _rcloneManager = new RcloneManager(Settings);

            var selectedProvider = (CloudProvider)Settings.Settings.SelectedProviderIndex;
            _rcloneManager.UploaderSettings = Settings;
            // Enable debug console based on settings
            DebugConsole.Enable(Settings.Settings.ShowConsoleOption);
            DebugConsole.WriteInfo("SaveTracker plugin initialized");
            DebugConsole.WriteInfo($"Cloud Provider: {selectedProvider}");
        }
        // Add sidebar element
        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            DebugConsole.WriteInfo("GetSidebarItems called");
            // SVG path from IcoMoon
            var svgPathData =
                "M512 328.771c0-41.045-28.339-75.45-66.498-84.74-1.621-64.35-54.229-116.031-118.931-116.031-37.896 0-71.633 17.747-93.427 45.366-12.221-15.799-31.345-25.98-52.854-25.98-36.905 0-66.821 29.937-66.821 66.861 0 3.218 0.24 6.38 0.682 9.477-5.611-1.012-11.383-1.569-17.285-1.569-53.499-0.001-96.866 43.393-96.866 96.921 0 53.531 43.367 96.924 96.865 96.924l328.131-0.006c48.069-0.092 87.004-39.106 87.004-87.223z";

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
                    Opened = () =>
                    {
                        DebugConsole.WriteInfo("Save Tracker sidebar opened");
                        cloudIcon.Fill = Brushes.White;

                        // Return your existing settings view instead of custom control
                        return new SaveTrackerSettingsView(PlayniteApi, Settings);
                    },
                    Closed = () =>
                    {
                        DebugConsole.WriteInfo("Save Tracker sidebar closed");
                        cloudIcon.Fill = Brushes.Gray;
                    }
                }
            };
        }

        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            DebugConsole.WriteLine("Generating TopPanelItems for current view...");

            // SVG path from IcoMoon
            var pathIDle =
                "M512 328.771c0-41.045-28.339-75.45-66.498-84.74-1.621-64.35-54.229-116.031-118.931-116.031-37.896 0-71.633 17.747-93.427 45.366-12.221-15.799-31.345-25.98-52.854-25.98-36.905 0-66.821 29.937-66.821 66.861 0 3.218 0.24 6.38 0.682 9.477-5.611-1.012-11.383-1.569-17.285-1.569-53.499-0.001-96.866 43.393-96.866 96.921 0 53.531 43.367 96.924 96.865 96.924l328.131-0.006c48.069-0.092 87.004-39.106 87.004-87.223z";

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

            yield return new TopPanelItem { Icon = _cloudIcon, Title = "Cloud Dev Extra2", };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            // Added into "Extensions" menu
            yield return new MainMenuItem { MenuSection = "@TEST1" };

            // Added into "Extensions -> submenu" menu
            yield return new MainMenuItem
            {
                MenuSection = "@Save Tracker|@Upload",
                Description = "Manual Upload Of Save Files",
            };
            yield return new MainMenuItem { MenuSection = "@Save Tracker| Download" };
        }

        public async override void OnGameStarting(OnGameStartingEventArgs args)
        {
            try
            {
                base.OnGameStarting(args);
                var isAdmin = await _trackLogic.IsAdministrator();

                if (!isAdmin)
                {
                    PlayniteApi.Dialogs.ShowMessage(
                        "Access denied. Please run as Administrator to track file I/O."
                    );
                    var result = PlayniteApi.Dialogs.ShowMessage(
                        "Do you want to restart as Admin?",
                        "Run As",
                        MessageBoxButton.YesNo
                    );
                    if (result == MessageBoxResult.Yes)
                    {
                        await _trackLogic.RestartAsAdmin();
                    }
                    else
                    {
                        // User clicked No or closed the dialog
                    }
                }
            }
            catch (Exception e)
            {
               DebugConsole.WriteException(e);
            }
        }

        public override async void OnGameStarted(OnGameStartedEventArgs args)
        {
            string cleanName = "";
            try
            {
                base.OnGameStarted(args);

                DebugConsole.Enable(Settings.Settings.ShowConsoleOption);
                DebugConsole.WriteSection("Game Started");
                DebugConsole.WriteKeyValue("Game Name", args.Game.Name);
                DebugConsole.WriteKeyValue("Start Time", DateTime.Now);

                string source = args.Game.Source?.Name ?? "Unknown";
                string pluginId = args.Game.PluginId.ToString();
                bool isStandalone = string.IsNullOrEmpty(pluginId);

                DebugConsole.WriteKeyValue("Game Source", source);
                DebugConsole.WriteKeyValue("Plugin ID", pluginId);
                DebugConsole.WriteKeyValue("Is Standalone", isStandalone);
                DebugConsole.WriteInfo(
                    isStandalone
                      ? "Game is running as a standalone executable (no launcher)"
                      : $"Game launched via launcher: {source}"
                );

                string gamePath = args.SourceAction?.Path;

                // Detect game exe using WMI
                if (source != "Unknown" && !string.IsNullOrEmpty(args.Game.InstallDirectory))
                {
                    string detectedExe = await ProcessMonitor.GetProcessFromDir(
                        args.Game.InstallDirectory
                    );
                    if (!string.IsNullOrEmpty(detectedExe))
                    {
                        // Take first path if multiple lines returned
                        detectedExe = detectedExe.Split(
                            new[] { '\n', '\r' },
                            StringSplitOptions.RemoveEmptyEntries
                        )[0];

                        // Remove illegal path characters
                        detectedExe = new string(
                            detectedExe
                                .Where(c => !Path.GetInvalidPathChars().Contains(c))
                                .ToArray()
                        );

                        gamePath = detectedExe;
                        DebugConsole.WriteInfo("Game detected at: " + gamePath);
                    }
                    else
                    {
                        DebugConsole.WriteInfo("No game detected within the timeout.");
                    }
                }

                DebugConsole.WriteKeyValue(
                    "Game Path",
                    gamePath ?? "Couldn't find game path, please specify it in the game Action"
                );

                // Ensure checksum file exists
                string checksumFilePath = Path.Combine(
                    args.Game.InstallDirectory,
                    ".savetracker_checksums.json"
                );
                if (!File.Exists(checksumFilePath))
                {
                    var initialData = new GameUploadData
                    {
                        Files = new Dictionary<string, FileChecksumRecord>(),
                        LastUpdated = DateTime.UtcNow,
                        CanTrack = true,
                        CanUploads = true,
                        Blacklist = new Dictionary<string, FileChecksumRecord>(),
                        LastSyncStatus = "Initialized"
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(
                        initialData,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                    );

                    File.WriteAllText(checksumFilePath, json);
                    DebugConsole.WriteSuccess(
                        $".savetracker_checksums.json Created: {checksumFilePath}"
                    );
                }

                if (!string.IsNullOrEmpty(gamePath))
                {
                    var trackLogic = new TrackLogic { TrackerSettings = Settings };

                    // Extract exe name safely
                    string exeName = Path.GetFileName(gamePath);
                    cleanName = !string.IsNullOrEmpty(exeName)
                        ? new string(
                              exeName
                                  .ToLower()
                                  .Replace(".exe", "")
                                  .Where(c => !Path.GetInvalidPathChars().Contains(c))
                                  .ToArray()
                          )
                        : null;
                    if (!string.IsNullOrEmpty(cleanName))
                    {
                        if (Settings.Settings.TrackFiles)
                        {
                            if (source != "Unknown" && !Settings.Settings.Track3RdParty)
                            {
                                DebugConsole.WriteInfo(
                                    $"Skipping tracking: Game has a client ({source}) and 3rd-party tracking is disabled."
                                );
                                return;
                            }

                            DebugConsole.WriteInfo($"Starting to track executable: {cleanName}");

                            try
                            {
                                var gameData = Misc.GetGameData(args.Game);
                                if (gameData.CanTrack)
                                {
                                    //bool isThirdParty = source != "Unknown";
                                    await trackLogic.Track(
                                        PlayniteApi,
                                        args.Game
                                    );
                                    DebugConsole.WriteSuccess("Tracked successfully");
                                }
                                else
                                {
                                    DebugConsole.WriteInfo("Tracking Bypassed");
                                }
                            }
                            catch (Exception ex)
                            {
                                DebugConsole.WriteError($"Failed to start tracking: {ex.Message}");
                            }
                        }
                        else
                        {
                            DebugConsole.WriteInfo(
                                $"Tracking disabled in settings. Game client: {source}"
                            );
                        }
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
                PlayniteApi.Dialogs.ShowMessage(
                    $"Error starting game tracking: {ex.Message}\n{cleanName}"
                );
            }
        }

        public override async void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                string source = args.Game.Source?.Name ?? "Unknown";
                var gameData = Misc.GetGameData(args.Game);

                // Only continue if tracking is enabled
                if (Settings.Settings.TrackFiles && gameData.CanTrack)
                {
                    // Determine if we should handle this based on launcher source
                    bool isPlayniteLaunch = source == "Unknown";
                    bool allow3RdParty = Settings.Settings.Track3RdParty;
                    if (!isPlayniteLaunch && !allow3RdParty)
                    {
                        DebugConsole.WriteInfo(
                            $"Tracking skipped: Game stopped and was launched via 3rd-party client ({source}), and Track3rdParty is disabled."
                        );
                        return;
                    }

                    DebugConsole.WriteSection("Game Stopped");
                    DebugConsole.WriteKeyValue("Game Name", args.Game.Name);
                    DebugConsole.WriteKeyValue("Stop Time", DateTime.Now);

                    DebugConsole.WriteInfo("Stopping file tracking...");
                    _trackLogic.StopTracking();

                    var saveList = TrackLogic.GetSaveList();
                    DebugConsole.WriteList("test List", saveList);
                    DebugConsole.WriteKeyValue("Files Found", saveList.Count);
                    if (saveList.Count == 0 && gameData.Files.Count == 0)
                    {
                        string msg = "No save files were tracked locally or remotely.";
                        DebugConsole.WriteWarning(msg);
                        PlayniteApi.Dialogs.ShowMessage(msg, "No Save Data");
                        return;
                    }
                    else if (saveList.Count == 0 && gameData.Files.Count > 0)
                    {
                        DebugConsole.WriteInfo(
                            $"No local saves found, using {gameData.Files.Count} remote save files"
                        );
                        saveList = Misc.ConvertFilesToStringList(gameData.Files);
                    }
                    DebugConsole.WriteList("Tracked Save Files", saveList);

                    var message = "Saves:\n" + string.Join("\n", saveList);
                    var notification = new NotificationMessage(
                        "Save Files",
                        message,
                        NotificationType.Info
                    );
                    PlayniteApi.Notifications.Add(notification);

                    if (Settings.Settings.AutoSyncOption && gameData.CanUploads)
                    {
                        DebugConsole.WriteInfo("Starting cloud upload...");

                        try
                        {
                            _rcloneManager = new RcloneManager(Settings)
                            {
                                UploaderSettings = Settings
                            };

                            await _rcloneManager.Upload(
                                saveList,
                                PlayniteApi,
                                args.Game,
                                (CloudProvider)Settings.Settings.SelectedProviderIndex
                            );
                            DebugConsole.WriteSuccess("Cloud upload completed.");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteError($"Cloud upload failed: {ex.Message}");
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
            DebugConsole.Enable(Settings.Settings.ShowConsoleOption);
            if (Settings.Settings.ShowConsoleOption)
            {
                DebugConsole.WriteInfo("Debug console enabled via settings");
            }
        }
    }
}
