using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace SaveTracker
{
    public class TrackLogic
    {

        private static TraceEventSession session;
        private static bool isTracking = false;
        public  SaveTrackerSettingsViewModel  TrackerSettings { get; set; }
        public static List<string> SaveFilesList = new List<string>();
        static string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public List<string> GetSaveList()
        {
            return SaveFilesList;
        }
        
        private static readonly List<string> IgnoredDirectories = new List<string>()
        {
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData",
            Path.Combine(userProfile, @"AppData\Local\Temp"),
            Path.Combine(userProfile, @"AppData\Local\Packages"),
            Path.Combine(userProfile, @"AppData\Local\Microsoft\Windows"),
            @"C:\Temp",
            @"C:\Tmp",
            // Add any other system temp or hidden folders you want to ignore
        };

        public async Task Track(string processName, IPlayniteAPI playniteApi, OnGameStartedEventArgs GameArg, bool showDebugConsole = false)
        {
            SaveFilesList = new List<string>();

            
            
                DebugConsole.WriteLine("== SaveTracker DebugConsole Started ==");
            

            if (isTracking)
            {
                DebugConsole.WriteLine("Already tracking.");
                playniteApi.Dialogs.ShowMessage("Already tracking a process. Stop current tracking first.");
                return;
            }

            string cleanName = processName.ToLower().Replace(".exe", "");
            Process[] procs = Process.GetProcessesByName(cleanName);

            if (procs.Length == 0)
            {
                 DebugConsole.WriteLine($"No process with name {cleanName} found.");
                playniteApi.Dialogs.ShowMessage($"No process with name {cleanName} found.");
                return;
            }

            int targetPid = procs[0].Id;
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string logFile = Path.Combine(desktopPath, "SaveTracker.log");

            File.WriteAllText(logFile, $"SaveTracker Log Started - {DateTime.Now}\n");
             DebugConsole.WriteLine($"Tracking Process: {procs[0].ProcessName} (PID: {targetPid})");

            try
            {
                session = new TraceEventSession("SaveTrackerSession");
                isTracking = true;

                session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.FileIO |
                    KernelTraceEventParser.Keywords.FileIOInit
                );

                void HandleFileWrite(string filePath, string proctype = "undetermined")
                {
                    if (string.IsNullOrEmpty(filePath))
                        return;
                    foreach (var ignoredDir in IgnoredDirectories)
                    {
                        if (filePath.StartsWith(ignoredDir, StringComparison.OrdinalIgnoreCase))
                            return;
                    }

                    lock (SaveFilesList)
                    {
                        if (!SaveFilesList.Contains(filePath))
                        {
                            SaveFilesList.Add(filePath);
                             DebugConsole.WriteLine($"Detected write: {filePath}");
                        }
                    }
                }

                if (TrackerSettings.Settings.TrackWrites)
                {
                    session.Source.Kernel.FileIOWrite += data =>
                    {
                        if (data.ProcessID == targetPid)
                        {
                            HandleFileWrite(data.FileName);
                        }
                    };
                }
                DebugConsole.WriteWarning("can track writes?: " +TrackerSettings.Settings.TrackWrites.ToString());
                DebugConsole.WriteWarning("can track reads?: " +TrackerSettings.Settings.TrackReads.ToString());
                
                if (TrackerSettings.Settings.TrackReads)
                {
                    session.Source.Kernel.FileIORead += data =>
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
                             DebugConsole.WriteLine("ETW session processing started.");
                            session.Source.Process();
                        }
                        catch (Exception ex)
                        {
                             DebugConsole.WriteLine($"[ERROR] ETW Session: {ex.Message}");
                        }
                    });

                    gameProc.WaitForExit();
                     DebugConsole.WriteLine("Game exited, finalizing tracking...");
                    Thread.Sleep(1000);

                    // Stop the ETW session first
                    StopTracking();

                    // Create and save the JSON file with the tracked files
                    lock (SaveFilesList)
                    {
                        // Create a copy of the current list for JSON export
                        var trackedFiles = new List<string>(SaveFilesList);
                        trackedFiles.Sort();

                        // Create the JSON file path
                        var jsonPath = Path.Combine(GameArg.Game.InstallDirectory, "GameFiles.json");
                        
                        try
                        {
                            // Save the tracked files to JSON
                            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(trackedFiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                            
                            // Add the JSON file itself to the list
                            SaveFilesList.Add(jsonPath);
                            
                             
                            {
                                DebugConsole.WriteLine($"Created JSON file: {jsonPath}");
                                DebugConsole.WriteLine($"Total tracked files: {trackedFiles.Count}");
                                DebugConsole.WriteLine("Final file list (including JSON):");
                                foreach (var path in SaveFilesList)
                                {
                                    DebugConsole.WriteLine($"  {path}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                             DebugConsole.WriteLine($"[ERROR] Failed to create JSON file: {ex.Message}");
                        }
                    }

                    isTracking = false;
                     DebugConsole.WriteLine("Tracking session complete.");
                });
            }
            catch (UnauthorizedAccessException)
            {
                isTracking = false;
                 DebugConsole.WriteLine("Access denied. Run as Admin.");
                playniteApi.Dialogs.ShowMessage("Access denied. Please run as Administrator to track file I/O.");
            }
            catch (Exception e)
            {
                isTracking = false;
                 DebugConsole.WriteLine($"[ERROR] Setup failed: {e.Message}");
               // playniteApi.Dialogs.ShowMessage($"Error setting up tracking: {e.Message}");
            }
        }

        public void StopTracking()
        {
            if (session != null && isTracking)
            {
                DebugConsole.WriteLine("Stopping ETW session...");
                session.Stop();
                session.Dispose();
                session = null;
                isTracking = false;
                DebugConsole.WriteLine("Session stopped.");
            }
        }
    }
}