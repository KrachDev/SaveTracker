using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class RcloneExecutor
    {
        private static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");
        public static readonly string ToolsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools"
        );

        // Optimized method with better async handling and performance flags
        public async Task<RcloneResult> ExecuteRcloneCommand(
            string arguments,
            TimeSpan timeout,
            bool hideWindow = true
        )
        {
            DebugConsole.WriteDebug($"Executing: rclone {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = RcloneExePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = hideWindow,
                // Performance improvements
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = false
            };

            var result = new RcloneResult();

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    result.Success = false;
                    result.Error = "Failed to start process";
                    return result;
                }

                using var cts = new CancellationTokenSource(timeout);

                // Use async methods with cancellation token for better performance
                var outputTask = ReadStreamAsync(process.StandardOutput, cts.Token);
                var errorTask = ReadStreamAsync(process.StandardError, cts.Token);

                // Wait for process to exit with cancellation support
                var processTask = WaitForExitAsync(process, cts.Token);

                try
                {
                    await processTask;
                    result.Output = await outputTask;
                    result.Error = await errorTask;
                    result.ExitCode = process.ExitCode;
                    result.Success = process.ExitCode == 0;
                }
                catch (OperationCanceledException)
                {
                    DebugConsole.WriteWarning(
                        $"Process timed out after {timeout.TotalSeconds} seconds"
                    );

                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            await Task.Delay(100, cts.Token); 
                        }
                    }
                    catch (Exception killEx)
                    {
                        DebugConsole.WriteError($"Error killing process: {killEx.Message}");
                    }

                    result.Success = false;
                    result.Error = "Process timed out";
                    result.ExitCode = -1;
                }

                if (!result.Success && !string.IsNullOrEmpty(result.Error))
                {
                    DebugConsole.WriteWarning($"Process failed with exit code {result.ExitCode}");
                    DebugConsole.WriteError($"Error output: {result.Error}");
                }

                return result;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Process execution failed");
                result.Success = false;
                result.Error = ex.Message;
                result.ExitCode = -1;
                return result;
            }
        }

        private static async Task<string> ReadStreamAsync(
            StreamReader reader,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var readTask = reader.ReadToEndAsync();
                var completedTask = await Task.WhenAny(
                    readTask,
                    Task.Delay(Timeout.Infinite, cancellationToken)
                );

                if (completedTask != readTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return await readTask;
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }
        }
        private static async Task WaitForExitAsync(
            Process process,
            CancellationToken cancellationToken
        )
        {
            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(50, cancellationToken);
            }

            if (!process.HasExited)
                cancellationToken.ThrowIfCancellationRequested();
        }

        // Method to get common performance flags for rclone commands
        public static string GetPerformanceFlags()
        {
            return "--no-check-certificate --disable-http2 --timeout 10s --contimeout 5s --retries 1 --low-level-retries 1";
        }
    }
}
public class RcloneResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
}
