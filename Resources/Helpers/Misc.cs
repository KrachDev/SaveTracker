using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;

namespace SaveTracker.Resources.Helpers
{
    public static class Misc
    {
        public static Game GetSelectedGame(IPlayniteAPI api)
        {
            // Get currently selected game from main view
            var selectedGames = api.MainView.SelectedGames;

            var games = selectedGames as Game[] ?? selectedGames.ToArray();
            if (games.Any())
            {
                return games.First();
            }

            return null;
        }
        public static GameUploadData GetGameData(Game game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.InstallDirectory))
                return null;

            var jsonPath = Path.Combine(game.InstallDirectory, ".savetracker_checksums.json");

            if (!File.Exists(jsonPath))
                return null;

            try
            {
                var jsonContent = File.ReadAllText(jsonPath);
                if (string.IsNullOrWhiteSpace(jsonContent))
                    return null;

                var checksumData = JsonConvert.DeserializeObject<GameUploadData>(jsonContent);
                return checksumData;
            }
            catch
            {
                // log or handle errors if needed
                return null;
            }
        }

        public static CloudProvider GetGameProvider(Game game)
        {
            var jsonPath = Path.Combine(game.InstallDirectory, ".savetracker_checksums.json");

            var jsonContent = File.ReadAllText(jsonPath);
            var checksumData = JsonConvert.DeserializeObject<GameUploadData>(jsonContent);

            return checksumData.GameProvider;
        }

        [UsedImplicitly]
        public static bool IsLocalGamedata(Game game)
        {
            var jsonPath = Path.Combine(game.InstallDirectory, ".savetracker_checksums.json");
            return File.Exists(jsonPath);
            
        }
        public static List<string> ConvertFilesToStringList(
            Dictionary<string, FileChecksumRecord> files
        )
        {
            if (files == null || files.Count == 0)
                return new List<string>();

            return files.Values.Select(record => record.Path).ToList();
        }
        public static string NormalizePathToPortable(string fullPath, Game game)
        {
            string portablePath = fullPath;

            try
            {
                // Common Windows environment variables and their paths
                var environmentMappings = new Dictionary<string, string>
                {
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "%APPDATA%"
                    },
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "%LOCALAPPDATA%"
                    },
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "%USERPROFILE%\\Documents"
                    },
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "%USERPROFILE%"
                    },
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "%PROGRAMDATA%"
                    },
                };

                // Add game install directory mapping
                if (!string.IsNullOrEmpty(game.InstallDirectory))
                {
                    environmentMappings[game.InstallDirectory] = "%GAME_DIR%";
                }

                // Sort by length descending to match longest paths first
                var sortedMappings = environmentMappings
                    .OrderByDescending(kvp => kvp.Key.Length)
                    .ToList();

                // Replace with environment variables
                foreach (var mapping in sortedMappings)
                {
                    if (portablePath.StartsWith(mapping.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        portablePath = mapping.Value + portablePath.Substring(mapping.Key.Length);
                        break; // Use first match (longest path)
                    }
                }

                // Normalize path separators
                portablePath = portablePath.Replace('/', '\\');

                return portablePath;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error normalizing path: {ex.Message}");
                return fullPath; // Return original path if normalization fails
            }
        }

        // Helper method to expand portable paths back to full paths when needed
        public static string ExpandPortablePath(string portablePath, Game game)
        {
            try
            {
                string expandedPath = portablePath;

                // Environment variable mappings
                var expansionMappings = new Dictionary<string, string>
                {
                    {
                        "%APPDATA%",
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    },
                    {
                        "%LOCALAPPDATA%",
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    },
                    {
                        "%USERPROFILE%\\Documents",
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    },
                    {
                        "%USERPROFILE%",
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    },
                    {
                        "%PROGRAMDATA%",
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                    },
                    { "%GAME_DIR%", game.InstallDirectory ?? "" }
                };

                // Sort by length descending to match longest variables first
                var sortedMappings = expansionMappings
                    .OrderByDescending(kvp => kvp.Key.Length)
                    .ToList();

                foreach (var mapping in sortedMappings)
                {
                    if (expandedPath.StartsWith(mapping.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        expandedPath = mapping.Value + expandedPath.Substring(mapping.Key.Length);
                        break;
                    }
                }

                return expandedPath;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error expanding portable path: {ex.Message}");
                return portablePath; // Return portable path if expansion fails
            }
        }
        [UsedImplicitly]
        private static bool IsPortablePath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   // Check if path contains any environment variables (starts and ends with %)
                   path.Contains('%');
        }

        // More comprehensive version that checks for specific known variables
        public static bool IsPortablePathStrict(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // List of known portable environment variables
            var portableVariables = new[]
            {
                "%APPDATA%",
                "%LOCALAPPDATA%",
                "%USERPROFILE%",
                "%PROGRAMDATA%",
                "%GAME_DIR%",
                "%USERPROFILE%\\Documents",
            };

            return portableVariables.Any(
                variable => path.StartsWith(variable, StringComparison.OrdinalIgnoreCase)
            );
        }

        // Version with regex for more precise detection
        public static bool IsPortablePathRegex(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Regex pattern to match environment variables like %VARIABLE%
            var pattern = @"%[A-Za-z_][A-Za-z0-9_]*%";
            return System.Text.RegularExpressions.Regex.IsMatch(path, pattern);
        }
    }
}
