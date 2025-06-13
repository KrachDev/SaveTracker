using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SaveTracker
{
    public static class DebugConsole
    {
        private static bool _isEnabled = false;
        private static bool _isConsoleAllocated = false;

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleTitle(string lpConsoleTitle);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        /// <summary>
        /// Enable or disable console debugging
        /// </summary>
        public static void Enable(bool enable = true)
        {
            _isEnabled = enable;
            
            if (enable)
            {
                ShowConsole();
            }
            else
            {
                HideConsole();
            }
        }

        /// <summary>
        /// Check if console debugging is enabled
        /// </summary>
        public static bool IsEnabled => _isEnabled;

        /// <summary>
        /// Show the console window
        /// </summary>
        public static void ShowConsole()
        {
            if (!_isConsoleAllocated)
            {
                AllocConsole();
                _isConsoleAllocated = true;
                
                // Redirect console output
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                
                // Set console title
                SetConsoleTitle("SaveTracker Debug Console");
                
                WriteHeader();
            }
            else
            {
                IntPtr consoleWindow = GetConsoleWindow();
                if (consoleWindow != IntPtr.Zero)
                {
                    ShowWindow(consoleWindow, SW_RESTORE);
                }
            }
        }

        /// <summary>
        /// Hide the console window
        /// </summary>
        public static void HideConsole()
        {
            IntPtr consoleWindow = GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                ShowWindow(consoleWindow, SW_HIDE);
            }
        }

        /// <summary>
        /// Close and free the console
        /// </summary>
        public static void CloseConsole()
        {
            if (_isConsoleAllocated)
            {
                FreeConsole();
                _isConsoleAllocated = false;
            }
        }

        /// <summary>
        /// Write a message to console if enabled
        /// </summary>
        public static void WriteLine(string message = "", string Title = "Data")
        {
            if (!_isEnabled) return;
            
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                Console.WriteLine($"[{timestamp} | {Title}] {message}");
            }
            catch
            {
                // Ignore console errors
            }
        }

        /// <summary>
        /// Write an info message with INFO prefix
        /// </summary>
        public static void WriteInfo(string message, string Title = "INFO")
        {
            WriteLine($"[{Title}] {message}");
        }

        /// <summary>
        /// Write a warning message with WARNING prefix (in yellow if supported)
        /// </summary>
        public static void WriteWarning(string message, string Title = "WARNING")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                WriteLine($"[{Title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[WARNING] {message}");
            }
        }

        /// <summary>
        /// Write an error message with ERROR prefix (in red if supported)
        /// </summary>
        public static void WriteError(string message, string Title = "ERROR")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                WriteLine($"[{Title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[{Title}] {message}");
            }
        }

        /// <summary>
        /// Write an exception with full details
        /// </summary>
        public static void WriteException(Exception ex, string context = "")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                WriteLine($"[EXCEPTION] {(!string.IsNullOrEmpty(context) ? $"{context}: " : "")}{ex.Message}");
                WriteLine($"[STACK TRACE] {ex.StackTrace}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[EXCEPTION] {(!string.IsNullOrEmpty(context) ? $"{context}: " : "")}{ex.Message}");
                WriteLine($"[STACK TRACE] {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Write a success message with SUCCESS prefix (in green if supported)
        /// </summary>
        public static void WriteSuccess(string message, string Title = "SUCCESS")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Green;
                WriteLine($"[{Title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[SUCCESS] {message}");
            }
        }

        /// <summary>
        /// Write a debug message with DEBUG prefix (in gray if supported)
        /// </summary>
        public static void WriteDebug(string message, string Title = "DEBUG")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                WriteLine($"[{Title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[{Title}] {message}");
            }
        }

        /// <summary>
        /// Write a separator line
        /// </summary>
        public static void WriteSeparator(char character = '=', int length = 50)
        {
            WriteLine(new string(character, length));
        }

        /// <summary>
        /// Write a section header
        /// </summary>
        public static void WriteSection(string title)
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                WriteLine();
                WriteSeparator('=', 60);
                WriteLine($"  {title.ToUpper()}");
                WriteSeparator('=', 60);
                Console.ResetColor();
            }
            catch
            {
                WriteLine();
                WriteSeparator('=', 60);
                WriteLine($"  {title.ToUpper()}");
                WriteSeparator('=', 60);
            }
        }

        /// <summary>
        /// Clear the console
        /// </summary>
        public static void Clear()
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.Clear();
                WriteHeader();
            }
            catch
            {
                // Ignore clear errors
            }
        }

        /// <summary>
        /// Write the initial header
        /// </summary>
        private static void WriteHeader()
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                WriteSeparator('=', 60);
                WriteLine("  SAVETRACKER DEBUG CONSOLE");
                WriteLine($"  Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteSeparator('=', 60);
                Console.ResetColor();
                WriteLine();
            }
            catch
            {
                WriteSeparator('=', 60);
                WriteLine("  SAVETRACKER DEBUG CONSOLE");
                WriteLine($"  Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteSeparator('=', 60);
                WriteLine();
            }
        }

        /// <summary>
        /// Write key-value pairs in a formatted way
        /// </summary>
        public static void WriteKeyValue(string key, object value)
        {
            WriteLine($"{key}: {value ?? "null"}");
        }

        /// <summary>
        /// Write a list of items
        /// </summary>
        public static void WriteList<T>(string title, System.Collections.Generic.IEnumerable<T> items)
        {
            if (!_isEnabled) return;
            
            WriteLine($"{title}:");
            foreach (var item in items)
            {
                WriteLine($"  - {item}");
            }
        }
        
        
        
        public static async Task UploadIconChanager( System.Windows.Shapes.Path icon, int status)
        {
            var svgPath = "";
            // SVG path from IcoMoon
            var pathIdle = "M512 328.771c0-41.045-28.339-75.45-66.498-84.74-1.621-64.35-54.229-116.031-118.931-116.031-37.896 0-71.633 17.747-93.427 45.366-12.221-15.799-31.345-25.98-52.854-25.98-36.905 0-66.821 29.937-66.821 66.861 0 3.218 0.24 6.38 0.682 9.477-5.611-1.012-11.383-1.569-17.285-1.569-53.499-0.001-96.866 43.393-96.866 96.921 0 53.531 43.367 96.924 96.865 96.924l328.131-0.006c48.069-0.092 87.004-39.106 87.004-87.223z";
            var pathUpload = "M27.883 12.078c0.076-0.347 0.117-0.708 0.117-1.078 0-2.761-2.239-5-5-5-0.444 0-0.875 0.058-1.285 0.167-0.775-2.417-3.040-4.167-5.715-4.167-2.73 0-5.033 1.823-5.76 4.318-0.711-0.207-1.462-0.318-2.24-0.318-4.418 0-8 3.582-8 8s3.582 8 8 8h4v6h8v-6h7c2.761 0 5-2.239 5-5 0-2.46-1.777-4.505-4.117-4.922zM18 20v6h-4v-6h-5l7-7 7 7h-5z";
            var pathDownload="M27.844 11.252c-0.101-4.022-3.389-7.252-7.433-7.252-2.369 0-4.477 1.109-5.839 2.835-0.764-0.987-1.959-1.624-3.303-1.624-2.307 0-4.176 1.871-4.176 4.179 0 0.201 0.015 0.399 0.043 0.592-0.351-0.063-0.711-0.098-1.080-0.098-3.344-0-6.054 2.712-6.054 6.058s2.71 6.058 6.054 6.058h2.868l7.078 7.328 7.078-7.328 3.484-0c3.004-0.006 5.438-2.444 5.438-5.451 0-2.565-1.771-4.716-4.156-5.296zM16 26l-6-6h4v-6h4v6h4l-6 6z";
            var pathSucces="M27.883 16.078c0.076-0.347 0.117-0.708 0.117-1.078 0-2.761-2.239-5-5-5-0.445 0-0.875 0.058-1.285 0.167-0.775-2.417-3.040-4.167-5.715-4.167-2.73 0-5.033 1.823-5.76 4.318-0.711-0.207-1.462-0.318-2.24-0.318-4.418 0-8 3.582-8 8s3.582 8 8 8h19c2.761 0 5-2.239 5-5 0-2.46-1.777-4.505-4.117-4.922zM13 24l-5-5 2-2 3 3 7-7 2 2-9 9z";
            var pathFailed="M21.462 22h2.539c2.761 0 4.999-2.244 4.999-5 0-2.096-1.287-3.892-3.117-4.634v0c-0.523-2.493-2.734-4.366-5.383-4.366-0.863 0-1.679 0.199-2.406 0.553-1.203-2.121-3.481-3.553-6.094-3.553-3.866 0-7 3.134-7 7 0 0.138 0.004 0.275 0.012 0.412v0c-1.772 0.77-3.012 2.538-3.012 4.588 0 2.761 2.232 5 4.999 5h2.539l5.962-10 5.962 10zM15.5 14l6.5 11h-13l6.5-11zM15 18v3h1v-3h-1zM15 22v1h1v-1h-1z";
            var pathHandeling="M27.802 5.197c-2.925-3.194-7.13-5.197-11.803-5.197-8.837 0-16 7.163-16 16h3c0-7.18 5.82-13 13-13 3.844 0 7.298 1.669 9.678 4.322l-4.678 4.678h11v-11l-4.198 4.197z";
            
            switch (status)
            {
                case 0:
                    //idel
                    svgPath = pathIdle;
                    break;
                case 1:
                    //Upload
                    svgPath = pathUpload;
                    break;
                case 2:
                    //Download
                    svgPath = pathDownload;
                    break;
                case 3:
                    //Failed
                    svgPath = pathFailed;
                    break;
                case 4:
                    //Success
                    svgPath = pathSucces;
                    break;
                case 5:
                    //Handling
                    svgPath = pathHandeling;
                    break;
                default:
                    //idle
                    svgPath = pathIdle;
                break;
                
            }
            var pathGeometry = Geometry.Parse(svgPath);
            icon.Data = pathGeometry;
        }
    }
}