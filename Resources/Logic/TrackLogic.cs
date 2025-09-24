using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveTracker.Resources.Helpers;
using static SaveTracker.Resources.Logic.DebugConsole;

namespace SaveTracker.Resources.Logic
{
    public class TrackLogic
    {
        private static TraceEventSession _session;
        private static bool _isTracking;
        public SaveTrackerSettingsViewModel TrackerSettings { get; set; }
        private static List<string> _saveFilesList = new List<string>();
        private static readonly string UserProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile
        );

        public static List<string> GetSaveList()
        {
            lock (_saveFilesList)
            {
                return _saveFilesList;
            }
        }
        // Optimized file filtering for save game detection
        private static readonly HashSet<string> IgnoredDirectoriesSet = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        ) {
            // System and program directories
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData",
            @"C:\System Volume Information",
            @"C:\$Recycle.Bin",
            @"C:\Recovery",
            // Temp & user-local system folders
            Path.Combine(UserProfile, @"AppData\Local\Temp"),
            Path.Combine(UserProfile, @"AppData\Local\Packages"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\WindowsApps"),
            Path.Combine(UserProfile, @"AppData\LocalLow\Microsoft\CryptnetUrlCache"),
            @"C:\Temp",
            @"C:\Tmp",
            @"C:\Windows\Temp",
            // Graphics card caches and drivers
            Path.Combine(UserProfile, @"AppData\Local\AMD\VkCache"),
            Path.Combine(UserProfile, @"AppData\Local\AMD\DxCache"),
            Path.Combine(UserProfile, @"AppData\Local\AMD"),
            Path.Combine(UserProfile, @"AppData\Local\AMD\GLCache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA Corporation\NV_Cache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA Corporation\GLCache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA Corporation\DXCache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA"),
            Path.Combine(UserProfile, @"AppData\Local\Intel\ShaderCache"),
            Path.Combine(UserProfile, @"AppData\Local\Intel"),
            @"C:\ProgramData\NVIDIA Corporation\Drs",
            @"C:\ProgramData\NVIDIA Corporation\NV_Cache",
            @"C:\ProgramData\NVIDIA Corporation\GLCache",
            @"C:\ProgramData\NVIDIA Corporation\Downloader",
            @"C:\ProgramData\AMD\PPC",
            // DirectX and graphics caches
            Path.Combine(UserProfile, @"AppData\Local\D3DSCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\DirectX"),
            Path.Combine(UserProfile, @"AppData\Local\VirtualStore\ProgramData\Microsoft\DirectX"),
            // Game platform caches and logs
            Path.Combine(UserProfile, @"AppData\Local\Steam\htmlcache"),
            Path.Combine(UserProfile, @"AppData\Local\Steam\logs"),
            Path.Combine(UserProfile, @"AppData\Local\Steam\crashdumps"),
            Path.Combine(UserProfile, @"AppData\Local\Steam\shader_cache_temp_dir_d3d11"),
            Path.Combine(UserProfile, @"AppData\Local\Steam\shader_cache_temp_dir_vulkan"),
            Path.Combine(UserProfile, @"AppData\Local\EpicGamesLauncher\Intermediate"),
            Path.Combine(UserProfile, @"AppData\Local\EpicGamesLauncher\Saved\Logs"),
            Path.Combine(UserProfile, @"AppData\Local\EpicGamesLauncher\Saved\webcache"),
            Path.Combine(UserProfile, @"AppData\Local\Origin\Logs"),
            Path.Combine(UserProfile, @"AppData\Local\Ubisoft Game Launcher\logs"),
            Path.Combine(UserProfile, @"AppData\Local\Battle.net\Logs"),
            Path.Combine(UserProfile, @"AppData\Local\GOG.com\Galaxy\logs"),
            // Windows and system caches
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\WebCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\Caches"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\History"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\INetCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\INetCookies"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\IECompatCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\IECompatUaCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\IEDownloadHistory"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\WER"),
            // Browser caches
            Path.Combine(UserProfile, @"AppData\Local\Google\Chrome\User Data\Default\Cache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Edge\User Data\Default\Cache"),
            Path.Combine(UserProfile, @"AppData\Local\Mozilla\Firefox\Profiles"),
            // Additional common directories
            Path.Combine(UserProfile, @"AppData\Local\CrashDumps"),
            Path.Combine(UserProfile, @"AppData\Local\Logs"),
            Path.Combine(UserProfile, @"AppData\Local\VirtualStore"),
        };

        // Fast file extension and name filters
        private static readonly HashSet<string> IgnoredExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        ) {
            ".tmp",
            ".log",
            ".dmp",
            ".crash",
            ".bak",
            ".old",
            ".lock",
            ".pid",
            ".swp",
            ".swo",
            ".temp",
            ".cache",
            ".etl",
            ".evtx",
            ".pdb",
            ".map",
            ".symbols",
            ".debug",
            ".parc"
        };

        private static readonly HashSet<string> IgnoredFileNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        ) {
            "thumbs.db",
            "desktop.ini",
            ".ds_store",
            "hiberfil.sys",
            "pagefile.sys",
            "swapfile.sys",
            "bootmgfw.efi",
            "ntuser.dat",
            "ntuser.pol"
        };

        // Simple keyword-based filters for obvious non-saves
        private static readonly string[] IgnoredKeywords =
        {
            "cache",
            "temp",
            "log",
            "crash",
            "dump",
            "shader",
            "debug",
            "thumbnail",
            "preview",
            "backup",
            "unity",
            "analytics",
            "windows",
            "config",
            "sentry",
            "sentrynative",
            "event",
            "autosave.bak"
        };
        private static bool IsObviousSystemFile(string fileName)
        {
            // Files starting with ~ or . are usually temp/hidden
            if (fileName.StartsWith("~") || fileName.StartsWith("."))
                return true;
            return false;
        }

        // Add this static field to track already logged files
        private static readonly HashSet<string> LoggedFiles = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

private static bool ShouldIgnoreFile(string filePath, Game game = null)
{
    if (string.IsNullOrEmpty(filePath))
        return true;

    // Normalize the path for consistent tracking
    string normalizedPath = filePath.Replace('/', '\\');
    bool shouldLog = LoggedFiles.Add(normalizedPath); // Returns false if already exists

    // 1. Game-specific blacklist check (highest priority)
    if (game != null)
    {
        var data = Misc.GetGameData(game);
        if (data?.Blacklist != null)
        {
            foreach (var blacklistItem in data.Blacklist)
            {
                // Normalize blacklist path once per iteration
                string normalizedBlacklist = blacklistItem.Value.Path.Replace('/', '\\');

                // Combined exact path match (covers both original and normalized)
                if (string.Equals(normalizedPath, normalizedBlacklist, StringComparison.OrdinalIgnoreCase))
                {
                    if (shouldLog)
                        DebugConsole.WriteWarning($"Skipped (Game Blacklist - Exact): {filePath}");
                    return true;
                }

                // Check if file is within blacklisted directory
                if (normalizedPath.StartsWith(normalizedBlacklist + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    if (shouldLog)
                        DebugConsole.WriteWarning($"Skipped (Game Blacklist - Directory): {filePath}");
                    return true;
                }
            }
        }
        else
        {
            if (shouldLog)
                DebugConsole.WriteWarning("BlackList is Null");
        }
    }

    try
    {
        // 2. Quick directory check - most performant filter
        foreach (var ignoredDir in IgnoredDirectoriesSet)
        {
            if (normalizedPath.StartsWith(ignoredDir + "\\", StringComparison.OrdinalIgnoreCase) || 
                normalizedPath.Equals(ignoredDir, StringComparison.OrdinalIgnoreCase))
            {
                if (shouldLog)
                    DebugConsole.WriteWarning($"Skipped (System Directory): {filePath}");
                return true;
            }
        }

        // 3. File name and extension checks
        string fileName = Path.GetFileName(normalizedPath);
        string fileExtension = Path.GetExtension(normalizedPath);

        if (IgnoredFileNames.Contains(fileName))
        {
            if (shouldLog)
                DebugConsole.WriteWarning($"Skipped (Ignored Filename): {filePath}");
            return true;
        }

        if (IgnoredExtensions.Contains(fileExtension))
        {
            if (shouldLog)
                DebugConsole.WriteWarning($"Skipped (Ignored Extension): {filePath}");
            return true;
        }

        // 4. Simple keyword filtering (case-insensitive) - compute lowercase once
        string lowerPath = normalizedPath.ToLower();
        string lowerFileName = fileName.ToLower();

        foreach (var keyword in IgnoredKeywords)
        {
            if (lowerFileName.Contains(keyword))
            {
                if (shouldLog)
                    DebugConsole.WriteWarning($"Skipped (Keyword in Filename '{keyword}'): {filePath}");
                return true;
            }

            if (lowerPath.Contains($"\\{keyword}\\"))
            {
                if (shouldLog)
                    DebugConsole.WriteWarning($"Skipped (Keyword in Path '{keyword}'): {filePath}");
                return true;
            }
        }

        // 5. Additional heuristics for common patterns
        if (IsObviousSystemFile(fileName))
        {
            if (shouldLog)
                DebugConsole.WriteWarning($"Skipped (System File Heuristic): {filePath}");
            return true;
        }

        // File passed all filters - remove from logged set since it's not being ignored
        LoggedFiles.Remove(normalizedPath);
        return false;
    }
    catch (Exception ex)
    {
        if (shouldLog)
            DebugConsole.WriteWarning($"Skipped (Path Processing Error): {filePath} - {ex.Message}");
        return false;
    }
}
public async Task Track(
    IPlayniteAPI playniteApi,
    Game gamearg
)
{
    lock (_saveFilesList)
    {
        _saveFilesList = new List<string>();
    }

    WriteLine("== SaveTracker DebugConsole Started ==");

    if (_isTracking)
    {
        WriteLine("Already tracking.");
        playniteApi.Dialogs.ShowMessage(
            "Already tracking a process. Stop current tracking first."
        );
        return;
    }

    var detectedExe = await ProcessMonitor.GetProcessFromDir(gamearg.InstallDirectory);
    string processName = Path.GetFileName(detectedExe);
    var cleanName = processName.ToLower().Replace(".exe", "");
    var procs = Process.GetProcessesByName(cleanName);

    if (procs.Length == 0)
    {
        WriteLine($"No process with name {cleanName} found.");
        playniteApi.Dialogs.ShowMessage($"No process with name {cleanName} found.");
        return;
    }

    int initialPid = procs[0].Id;
    WriteLine($"Initial Process: {procs[0].ProcessName} (PID: {initialPid})");

    // Get all processes from the same directory
    var directoryProcesses = GetProcessesFromDirectory(gamearg.InstallDirectory);
    var trackedPids = new ConcurrentHashSet<int>();
    
    foreach (var pid in directoryProcesses)
    {
        trackedPids.Add(pid);
        WriteLine($"Tracking directory process: PID {pid}");
    }

    try
    {
        _session = new TraceEventSession("SaveTrackerSession");
        _isTracking = true;

        // Enable FileIO and Process events
        _session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.FileIO
                | KernelTraceEventParser.Keywords.FileIOInit
                | KernelTraceEventParser.Keywords.Process
        );

        // Monitor new processes to see if they're from our directory
        _session.Source.Kernel.ProcessStart += data =>
        {
            try
            {
                var process = Process.GetProcessById(data.ProcessID);
                string exePath = GetProcessExecutablePath(data.ProcessID);
                
                if (!string.IsNullOrEmpty(exePath) && 
                    exePath.StartsWith(gamearg.InstallDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    if (trackedPids.Add(data.ProcessID))
                    {
                        WriteLine($"New directory process started: {Path.GetFileName(exePath)} (PID: {data.ProcessID})");
                    }
                }
            }
            catch (Exception ex)
            {
                // Process might have already exited, ignore
            }
        };

        // Clean up when processes exit
        _session.Source.Kernel.ProcessStop += data =>
        {
            if (trackedPids.Contains(data.ProcessID))
            {
                WriteLine($"Directory process exited: PID {data.ProcessID}");
            }
        };

        void HandleFileWrite(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            // Normalize path
            string normalizedPath = filePath.Replace('/', '\\');

            // Check blacklist BEFORE converting to portable (using full path)
            if (ShouldIgnoreFile(normalizedPath, gamearg))
                return;

            lock (_saveFilesList)
            {
                // Convert to portable path first
                string portablePath = normalizedPath;

                if (normalizedPath.IndexOf(gamearg.InstallDirectory, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    portablePath = Misc.NormalizePathToPortable(normalizedPath, gamearg);
                }

                // Check if the portable path already exists (consistent format check)
                if (!_saveFilesList.Contains(portablePath))
                {
                    _saveFilesList.Add(portablePath);
                    WriteLine($"Detected Portable: {portablePath}");
                }
            }
        }

        if (TrackerSettings.Settings.TrackWrites)
        {
            _session.Source.Kernel.FileIOWrite += data =>
            {
                if (trackedPids.Contains(data.ProcessID))
                {
                    HandleFileWrite(data.FileName);
                }
            };
        }

        WriteWarning("can track writes?: " + TrackerSettings.Settings.TrackWrites.ToString());
        WriteWarning("can track reads?: " + TrackerSettings.Settings.TrackReads.ToString());

        if (TrackerSettings.Settings.TrackReads)
        {
            _session.Source.Kernel.FileIORead += data =>
            {
                if (trackedPids.Contains(data.ProcessID))
                {
                    HandleFileWrite(data.FileName);
                }
            };
        }

        await Task.Run(() =>
        {
            var gameProc = Process.GetProcessById(initialPid);

            // Start ETW processing in background
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

            // Periodically rescan directory for new processes
            Task.Run(async () =>
            {
                while (!gameProc.HasExited && _isTracking)
                {
                    try
                    {
                        var currentDirectoryProcesses = GetProcessesFromDirectory(gamearg.InstallDirectory);
                        foreach (var pid in currentDirectoryProcesses)
                        {
                            if (trackedPids.Add(pid))
                            {
                                WriteLine($"Found new directory process: PID {pid}");
                            }
                        }
                        
                        await Task.Delay(5 * 60 * 1000); // Check every 5 minutes
                    }
                    catch (Exception ex)
                    {
                        WriteLine($"[ERROR] Directory monitoring: {ex.Message}");
                    }
                }
            });

            // Wait for main process to exit
            gameProc.WaitForExit();
            WriteLine("Main game process exited, waiting for other directory processes...");
            
            // Wait for other directory processes to finish
            Thread.Sleep(3000);

            // Stop the ETW session
            StopTracking();

            // Create and save the JSON file with the tracked files
            lock (_saveFilesList)
            {
                var trackedFiles = new List<string>(_saveFilesList);
                trackedFiles.Sort();
                WriteList("List Of tracked Files:", trackedFiles);
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
    }
}

// Get all currently running processes from the specified directory
private List<int> GetProcessesFromDirectory(string directory)
{
    var processIds = new List<int>();
    
    try
    {
        string query = "SELECT ProcessId, ExecutablePath FROM Win32_Process";
        using (var searcher = new ManagementObjectSearcher(query))
        {
            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    string execPath = obj["ExecutablePath"]?.ToString();
                    if (!string.IsNullOrEmpty(execPath) && 
                        execPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                    {
                        int processId = Convert.ToInt32(obj["ProcessId"]);
                        processIds.Add(processId);
                        WriteLine($"Found directory process: {Path.GetFileName(execPath)} (PID: {processId})");
                    }
                }
                catch (Exception ex)
                {
                    // Skip processes we can't access
                    continue;
                }
            }
        }
    }
    catch (Exception ex)
    {
        WriteLine($"[ERROR] Getting directory processes: {ex.Message}");
    }
    
    return processIds;
}

// Get executable path for a specific process ID
private string GetProcessExecutablePath(int processId)
{
    try
    {
        string query = $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {processId}";
        using (var searcher = new ManagementObjectSearcher(query))
        {
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["ExecutablePath"]?.ToString() ?? "";
            }
        }
    }
    catch (Exception ex)
    {
        WriteLine($"[WARNING] Error getting executable path for PID {processId}: {ex.Message}");
    }
    
    return "";
}

// Thread-safe HashSet implementation
        // Method to check if the application is running with administrative privileges

        public Task<bool> IsAdministrator()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return Task.FromResult(principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
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
                var playniteProcessNames = new[]
                {
                    "Playnite",
                    "PlayniteUI",
                    "Playnite.Common",
                    "Playnite.SDK"
                };
                var processesToKill = new List<Process>();

                foreach (string processName in playniteProcessNames)
                {
                    processesToKill.AddRange(
                        Process
                            .GetProcessesByName(processName)
                            .Where(p => p.Id != currentProcess.Id)
                    );
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
                            if (
                                !string.IsNullOrEmpty(procPath)
                                && procPath.StartsWith(
                                    currentDir,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                && proc.Id != currentProcess.Id
                            )
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
                DebugConsole.WriteInfo(
                    $"Attempting to close {processesToKill.Count} related processes..."
                );

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
                        DebugConsole.WriteWarning(
                            $"Failed to gracefully close process {process.Id}: {ex.Message}"
                        );
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
                            DebugConsole.WriteInfo(
                                $"Force killing process {process.ProcessName} (ID: {process.Id})"
                            );
                            process.Kill();
                            process.WaitForExit(5000); // Wait up to 5 seconds for exit
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError(
                            $"Failed to kill process {process.Id}: {ex.Message}"
                        );
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
                        stillRunning.AddRange(
                            Process
                                .GetProcessesByName(processName)
                                .Where(p => p.Id != currentProcess.Id)
                        );
                    }

                    if (stillRunning.Count == 0)
                    {
                        allProcessesClosed = true;
                        break;
                    }

                    DebugConsole.WriteInfo(
                        $"Waiting for {stillRunning.Count} processes to exit... (Attempt {i + 1}/{maxWaitAttempts})"
                    );
                    await Task.Delay(500);
                }

                if (!allProcessesClosed)
                {
                    DebugConsole.WriteWarning(
                        "Some processes may still be running. Proceeding with restart..."
                    );
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
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                    Arguments = "--restart" // Optional: add restart flag if your app supports it
                };

                var newProcess = Process.Start(processInfo);

                if (newProcess == null)
                {
                    throw new InvalidOperationException(
                        "Failed to start elevated process - user may have cancelled UAC prompt"
                    );
                }

                DebugConsole.WriteInfo(
                    "Elevated process started successfully. Terminating current process..."
                );

                // Give the new process time to initialize before killing current
                await Task.Delay(1000);

                // Use Environment.Exit for cleaner shutdown
                Environment.Exit(0);
            }
            catch (System.ComponentModel.Win32Exception win32Ex)
                when (win32Ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt
                DebugConsole.WriteWarning("User cancelled elevation request.");
                MessageBox.Show(
                    "Administrator privileges are required to restart the application.",
                    "Elevation Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError(
                    $"Error restarting application with elevated permissions: {ex.Message}"
                );
                MessageBox.Show(
                    $"Restart failed: {ex.Message}\n\nPlease close all Playnite instances manually and restart as administrator.",
                    "Restart Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
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
public class ConcurrentHashSet<T> : IDisposable
{
    private readonly HashSet<T> _hashSet = new HashSet<T>();
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    public bool Add(T item)
    {
        _lock.EnterWriteLock();
        try
        {
            return _hashSet.Add(item);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool Contains(T item)
    {
        _lock.EnterReadLock();
        try
        {
            return _hashSet.Contains(item);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _hashSet.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }
}
