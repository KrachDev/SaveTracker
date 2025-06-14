using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Playnite.SDK;
using Playnite.SDK.Events;
using static SaveTracker.Resources.Logic.DebugConsole;

namespace SaveTracker.Resources.Logic
{
    public class TrackLogic
    {

        private static TraceEventSession _session;
        private static bool _isTracking;
        public  SaveTrackerSettingsViewModel  TrackerSettings { get; set; }
        private static List<string> _saveFilesList = new List<string>();
        private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static List<string> GetSaveList()
        {
            lock (_saveFilesList)
            {
                return _saveFilesList;
            }
        }
        
        private static readonly List<string> IgnoredDirectories = new List<string>()
        {
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData",
            Path.Combine(UserProfile, @"AppData\Local\Temp"),
            Path.Combine(UserProfile, @"AppData\Local\Packages"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows"),
            @"C:\Temp",
            @"C:\Tmp",
            // Add any other system temp or hidden folders you want to ignore
        };

        public async Task Track(string processName, IPlayniteAPI playniteApi, OnGameStartedEventArgs gameArg, bool showDebugConsole = false)
        {
            lock (_saveFilesList)
            {
                _saveFilesList = new List<string>();
            }

            
            
            WriteLine("== SaveTracker DebugConsole Started ==");
            

            if (_isTracking)
            {
                WriteLine("Already tracking.");
                playniteApi.Dialogs.ShowMessage("Already tracking a process. Stop current tracking first.");
                return;
            }

            var cleanName = processName.ToLower().Replace(".exe", "");
            var procs = Process.GetProcessesByName(cleanName);

            if (procs.Length == 0)
            {
                 WriteLine($"No process with name {cleanName} found.");
                playniteApi.Dialogs.ShowMessage($"No process with name {cleanName} found.");
                return;
            }

            int targetPid = procs[0].Id;
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string logFile = Path.Combine(desktopPath, "SaveTracker.log");

            File.WriteAllText(logFile, $"SaveTracker Log Started - {DateTime.Now}\n");
             WriteLine($"Tracking Process: {procs[0].ProcessName} (PID: {targetPid})");

            try
            {
                _session = new TraceEventSession("SaveTrackerSession");
                _isTracking = true;

                _session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.FileIO |
                    KernelTraceEventParser.Keywords.FileIOInit
                );

                void HandleFileWrite(string filePath)
                {
                    if (string.IsNullOrEmpty(filePath))
                        return;
                    foreach (var ignoredDir in IgnoredDirectories)
                    {
                        if (filePath.StartsWith(ignoredDir, StringComparison.OrdinalIgnoreCase))
                            return;
                    }

                    lock (_saveFilesList)
                    {
                        if (!_saveFilesList.Contains(filePath))
                        {
                            _saveFilesList.Add(filePath);
                             WriteLine($"Detected write: {filePath}");
                        }
                    }
                }

                if (TrackerSettings.Settings.TrackWrites)
                {
                    _session.Source.Kernel.FileIOWrite += data =>
                    {
                        if (data.ProcessID == targetPid)
                        {
                            HandleFileWrite(data.FileName);
                        }
                    };
                }
                WriteWarning("can track writes?: " +TrackerSettings.Settings.TrackWrites.ToString());
                WriteWarning("can track reads?: " +TrackerSettings.Settings.TrackReads.ToString());
                
                if (TrackerSettings.Settings.TrackReads)
                {
                    _session.Source.Kernel.FileIORead += data =>
                    {
                        if (data.ProcessID == targetPid)
                        {
                            HandleFileWrite(data.FileName);
                        }
                    };
                }
                
            
                await Task.Run(() =>
                {
                    var gameProc = Process.GetProcessById(targetPid);

                    Task.Run(() =>
                    {
                        try
                        {
                             WriteLine("ETW session processing started.");
                            _session.Source.Process();
                        }
                        catch (Exception ex)
                        {
                             WriteLine($"[ERROR] ETW Session: {ex.Message}");
                        }
                    });

                    gameProc.WaitForExit();
                     WriteLine("Game exited, finalizing tracking...");
                    Thread.Sleep(1000);

                    // Stop the ETW session first
                    StopTracking();

                    // Create and save the JSON file with the tracked files
                    lock (_saveFilesList)
                    {
                        // Create a copy of the current list for JSON export
                        var trackedFiles = new List<string>(_saveFilesList);
                        trackedFiles.Sort();

                        // Create the JSON file path
                        var jsonPath = Path.Combine(gameArg.Game.InstallDirectory, "GameFiles.json");
                        
                        try
                        {
                            // Save the tracked files to JSON
                            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(trackedFiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                            
                            // Add the JSON file itself to the list
                            _saveFilesList.Add(jsonPath);
                            
                             
                            {
                                WriteLine($"Created JSON file: {jsonPath}");
                                WriteLine($"Total tracked files: {trackedFiles.Count}");
                                WriteLine("Final file list (including JSON):");
                                foreach (var path in _saveFilesList)
                                {
                                    WriteLine($"  {path}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                             WriteLine($"[ERROR] Failed to create JSON file: {ex.Message}");
                        }
                    }

                    _isTracking = false;
                     WriteLine("Tracking session complete.");
                });
            }
            catch (UnauthorizedAccessException)
            {
                _isTracking = false;
                 WriteLine("Access denied. Run as Admin.");
                playniteApi.Dialogs.ShowMessage("Access denied. Please run as Administrator to track file I/O.");
            }
            catch (Exception e)
            {
                _isTracking = false;
                 WriteLine($"[ERROR] Setup failed: {e.Message}");
               // playniteApi.Dialogs.ShowMessage($"Error setting up tracking: {e.Message}");
            }
        }

        public void StopTracking()
        {
            if (_session != null && _isTracking)
            {
                WriteLine("Stopping ETW session...");
                _session.Stop();
                _session.Dispose();
                _session = null;
                _isTracking = false;
                WriteLine("Session stopped.");
            }
        }
    }
}