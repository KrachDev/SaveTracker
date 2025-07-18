using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
            // System and program directories
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData",

            // Temp & user-local system folders
            Path.Combine(UserProfile, @"AppData\Local\Temp"),
            Path.Combine(UserProfile, @"AppData\Local\Packages"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows"),
            @"C:\Temp",
            @"C:\Tmp",

            // AMD Vulkan cache
            Path.Combine(UserProfile, @"AppData\Local\AMD\VkCache"),

            // NVIDIA shader caches and other cache files
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA Corporation\NV_Cache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA Corporation\GLCache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA"),
            @"C:\ProgramData\NVIDIA Corporation\Drs",
            @"C:\ProgramData\NVIDIA Corporation\NV_Cache",
            @"C:\ProgramData\NVIDIA Corporation\GLCache",
            @"C:\ProgramData\NVIDIA Corporation\Downloader", // Used for driver install files

            // Other common cache folders
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\WebCache"),
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
                        WriteList("List Of tracked Files:", trackedFiles);
                        // Create the JSON file path
                        
                        try
                        {
                            // Save the tracked files to JSON
                            
                            // Add the JSON file itself to the list
                            
                       
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
                 await RestartAsAdmin();
            }
            catch (Exception e)
            {
                _isTracking = false;
                 WriteLine($"[ERROR] Setup failed: {e.Message}");
               // playniteApi.Dialogs.ShowMessage($"Error setting up tracking: {e.Message}");
            }
        }
        public static void LogIssue(string message)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string filePath = Path.Combine(desktopPath, "PlayniteIssues.txt");
        
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        
                File.AppendAllText(filePath, logEntry);
            }
            catch
            {
                // Silent fail if can't write to desktop
            }
        }
        // Method to check if the application is running with administrative privileges
        public async Task<bool> IsAdministrator()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        // Method to restart the application with administrative privileges
public async Task RestartAsAdmin()
{
    try
    {
        var currentProcess = Process.GetCurrentProcess();
        string executablePath = currentProcess.MainModule?.FileName;

        if (string.IsNullOrEmpty(executablePath))
        {
            DebugConsole.WriteError("Executable path not found.");
            MessageBox.Show("Executable path not found.");
            return;
        }

        // Get all Playnite-related processes (including different executables)
        var playniteProcessNames = new[] { "Playnite", "PlayniteUI", "Playnite.Common", "Playnite.SDK" };
        var processesToKill = new List<Process>();

        foreach (string processName in playniteProcessNames)
        {
            processesToKill.AddRange(Process.GetProcessesByName(processName)
                .Where(p => p.Id != currentProcess.Id));
        }

        // Also check for processes from the same directory
        var currentDir = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrEmpty(currentDir))
        {
            var allProcesses = Process.GetProcesses();
            foreach (var proc in allProcesses)
            {
                try
                {
                    var procPath = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(procPath) && 
                        procPath.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase) &&
                        proc.Id != currentProcess.Id)
                    {
                        processesToKill.Add(proc);
                    }
                }
                catch
                {
                    // Skip processes we can't access
                }
            }
        }

        // Gracefully close processes first
        DebugConsole.WriteInfo($"Attempting to close {processesToKill.Count} related processes...");
        
        foreach (var process in processesToKill.Distinct())
        {
            try
            {
                // Try graceful close first
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to gracefully close process {process.Id}: {ex.Message}");
            }
        }

        // Wait for graceful shutdown
        await Task.Delay(2000);

        // Force kill remaining processes
        foreach (var process in processesToKill)
        {
            try
            {
                if (!process.HasExited)
                {
                    DebugConsole.WriteInfo($"Force killing process {process.ProcessName} (ID: {process.Id})");
                    process.Kill();
                    process.WaitForExit(5000); // Wait up to 5 seconds for exit
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to kill process {process.Id}: {ex.Message}");
            }
        }

        // Enhanced wait with verification
        const int maxWaitAttempts = 20;
        bool allProcessesClosed = false;
        
        for (int i = 0; i < maxWaitAttempts; i++)
        {
            // Check all Playnite process names
            var stillRunning = new List<Process>();
            foreach (string processName in playniteProcessNames)
            {
                stillRunning.AddRange(Process.GetProcessesByName(processName)
                    .Where(p => p.Id != currentProcess.Id));
            }

            if (stillRunning.Count == 0)
            {
                allProcessesClosed = true;
                break;
            }

            DebugConsole.WriteInfo($"Waiting for {stillRunning.Count} processes to exit... (Attempt {i + 1}/{maxWaitAttempts})");
            await Task.Delay(500);
        }

        if (!allProcessesClosed)
        {
            DebugConsole.WriteWarning("Some processes may still be running. Proceeding with restart...");
        }

        // Additional wait for file system locks to release
        await Task.Delay(1500);

        // Force garbage collection to release any managed resources
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        DebugConsole.WriteInfo("Starting elevated process...");

        // Start elevated process with working directory
        var processInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            Arguments = "--restart" // Optional: add restart flag if your app supports it
        };

        var newProcess = Process.Start(processInfo);
        
        if (newProcess == null)
        {
            throw new InvalidOperationException("Failed to start elevated process - user may have cancelled UAC prompt");
        }

        DebugConsole.WriteInfo("Elevated process started successfully. Terminating current process...");
        
        // Give the new process time to initialize before killing current
        await Task.Delay(1000);
        
        // Use Environment.Exit for cleaner shutdown
        Environment.Exit(0);
    }
    catch (System.ComponentModel.Win32Exception win32Ex) when (win32Ex.NativeErrorCode == 1223)
    {
        // User cancelled UAC prompt
        DebugConsole.WriteWarning("User cancelled elevation request.");
        MessageBox.Show("Administrator privileges are required to restart the application.", 
                       "Elevation Required", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    catch (Exception ex)
    {
        DebugConsole.WriteError($"Error restarting application with elevated permissions: {ex.Message}");
        MessageBox.Show($"Restart failed: {ex.Message}\n\nPlease close all Playnite instances manually and restart as administrator.", 
                       "Restart Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}        public void StopTracking()
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