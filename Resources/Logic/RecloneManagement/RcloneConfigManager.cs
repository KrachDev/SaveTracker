using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playnite.SDK;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class RcloneConfigManager
    {
        private readonly CloudProviderHelper _cloudProviderHelper = new CloudProviderHelper();
        private readonly RcloneExecutor _rcloneExecutor = new RcloneExecutor();

        public static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");

        private static readonly string ToolsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools"
        );

        private readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");
        // Modified IsValidConfig method
        public Task<bool> IsValidConfig(string path, CloudProvider provider)
        {
            string providerName = _cloudProviderHelper.GetProviderConfigName(provider);
            string providerType = _cloudProviderHelper.GetProviderConfigType(provider);
            string displayName = _cloudProviderHelper.GetProviderDisplayName(provider);

            DebugConsole.WriteInfo($"Validating {displayName} config file: {path}");

            try
            {
                if (!File.Exists(path))
                {
                    DebugConsole.WriteWarning("Config file does not exist");
                    return Task.FromResult(false);
                }

                string content = File.ReadAllText(path);
                DebugConsole.WriteDebug($"Config file size: {content.Length} characters");

                // Check for provider-specific section
                string sectionName = $"[{providerName}]";
                if (!content.Contains(sectionName))
                {
                    DebugConsole.WriteWarning($"Config missing {sectionName} section");
                    return Task.FromResult(false);
                }

                // Check for provider-specific type
                string typeString = $"type = {providerType}";
                if (!content.Contains(typeString))
                {
                    DebugConsole.WriteWarning($"Config missing '{typeString}' setting");
                    return Task.FromResult(false);
                }

                // Token validation (if required by provider)
                if (_cloudProviderHelper.RequiresTokenValidation(provider))
                {
                    var tokenMatch = Regex.Match(content, @"token\s*=\s*(.+)");
                    if (!tokenMatch.Success)
                    {
                        DebugConsole.WriteWarning("Config missing or invalid token");
                        return Task.FromResult(false);
                    }

                    try
                    {
                        JsonConvert.DeserializeObject(tokenMatch.Groups[1].Value.Trim());
                        DebugConsole.WriteSuccess($"{displayName} config validation passed");
                        return Task.FromResult(true);
                    }
                    catch (JsonException)
                    {
                        DebugConsole.WriteWarning("Config token is not valid JSON");
                        return Task.FromResult(false);
                    }
                }
                else
                {
                    DebugConsole.WriteSuccess($"{displayName} config validation passed");
                    return Task.FromResult(true);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Config validation failed");
                return Task.FromResult(false);
            }
        }

        // Modified CreateConfig method
        public async Task CreateConfig(IPlayniteAPI api, CloudProvider provider)
        {
            string providerName = _cloudProviderHelper.GetProviderConfigName(provider);
            string providerType = _cloudProviderHelper.GetProviderType(provider);
            string displayName = _cloudProviderHelper.GetProviderDisplayName(provider);

            DebugConsole.WriteSection($"Creating {displayName} Config");

            try
            {
                if (File.Exists(_configPath))
                {
                    string backupPath = $"{_configPath}.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Move(_configPath, backupPath);
                    DebugConsole.WriteInfo($"Backed up existing config to: {backupPath}");
                }

                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"config create {providerName} {providerType} --config \"{_configPath}\"",
                    TimeSpan.FromMinutes(5),
                    false
                );

                if (result.Success && await IsValidConfig(_configPath, provider))
                {
                    DebugConsole.WriteSuccess(
                        $"{displayName} configuration completed successfully"
                    );
                    api.Notifications.Add(
                        new NotificationMessage(
                            "RCLONE_CONFIG_OK",
                            $"{displayName} is configured.",
                            NotificationType.Info
                        )
                    );
                }
                else
                {
                    string errorMsg =
                        $"Rclone config failed. Exit code: {result.ExitCode}, Error: {result.Error}";
                    DebugConsole.WriteError(errorMsg);
                    throw new Exception(errorMsg);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Config creation failed");
                throw;
            }
        }

        // Modified TestConnection method
        public async Task TestConnection(IPlayniteAPI api, CloudProvider provider)
        {
            string providerName = _cloudProviderHelper.GetProviderConfigName(provider);
            string displayName = _cloudProviderHelper.GetProviderDisplayName(provider);

            DebugConsole.WriteInfo($"Testing {displayName} connection...");

            try
            {
                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"lsd {providerName}: --max-depth 1 --config \"{_configPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (result.Success)
                {
                    DebugConsole.WriteSuccess($"{displayName} connection test passed");
                }
                else
                {
                    DebugConsole.WriteWarning($"Connection test failed: {result.Error}");
                    api.Notifications.Add(
                        new NotificationMessage(
                            "RCLONE_CONNECTION_WARNING",
                            $"Rclone configured but {displayName} connection test failed. Upload may not work properly.",
                            NotificationType.Error
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Connection test error");
            }
        }
    }
}
