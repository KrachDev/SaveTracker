using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Logic.RecloneManagment
{
    public class RcloneExecutor
    {
        public static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");
        public static readonly string ToolsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools");
        public readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");
        public async Task<RcloneResult> ExecuteRcloneCommand(string arguments, TimeSpan timeout,
            bool hideWindow = true) {
            DebugConsole.WriteDebug($"Executing: rclone {arguments}");

            var startInfo =
                new ProcessStartInfo { FileName = RcloneExePath,     Arguments = arguments,
                    UseShellExecute = false,      RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = hideWindow };

            var result = new RcloneResult();

            try {
                using var process = Process.Start(startInfo);
                using var cts = new System.Threading.CancellationTokenSource(timeout);

                if (process != null) {
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    bool finished = process.WaitForExit((int)timeout.TotalMilliseconds);

                    if (!finished) {
                        DebugConsole.WriteWarning($"Process timed out after {timeout.TotalSeconds} seconds");
                        try {
                            process.Kill();
                        } catch (Exception ex) {
                            DebugConsole.WriteError(ex.Message);
                        }

                        result.Success = false;
                        result.Error = "Process timed out";
                        result.ExitCode = -1;
                        return result;
                    }

                    result.Output = await outputTask;
                    result.Error = await errorTask;
                }

                if (process != null) {
                    result.ExitCode = process.ExitCode;
                    result.Success = process.ExitCode == 0;
                }

                if (!result.Success) {
                    DebugConsole.WriteWarning($"Process failed with exit code {result.ExitCode}");
                    if (!string.IsNullOrEmpty(result.Error)) {
                        DebugConsole.WriteError($"Error output: {result.Error}");
                    }
                }

                return result;
            } catch (Exception ex) {
                DebugConsole.WriteException(ex, "Process execution failed");
                result.Success = false;
                result.Error = ex.Message;
                result.ExitCode = -1;
                return result;
            }
        }

    }
}
public class RcloneResult {
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
}