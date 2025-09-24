using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SaveTracker.Resources.Logic
{
    public static class DebugConsole
    {
        private static bool _isEnabled;
        private static bool _isConsoleAllocated;

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

        private const int SwHide = 0;
        private const int SwRestore = 9;

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
                    ShowWindow(consoleWindow, SwRestore);
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
                ShowWindow(consoleWindow, SwHide);
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
        public static void WriteLine(string message = "", string title = "DATA")
        {
            if (!_isEnabled) return;
            
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                Console.WriteLine($"[{timestamp} | {title}] {message}");
            }
            catch
            {
                // Ignore console errors
            }
        }

        /// <summary>
        /// Write an info message with INFO prefix
        /// </summary>
        public static void WriteInfo(string message, string title = "INFO")
        {
            WriteLine($"[{title}] {message}");
        }

        /// <summary>
        /// Write a warning message with WARNING prefix (in yellow if supported)
        /// </summary>
        public static void WriteWarning(string message, string title = "WARNING")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                WriteLine($"[{title}] {message}");
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
        public static void WriteError(string message, string title = "ERROR")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                WriteLine($"[{title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[{title}] {message}");
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
        public static void WriteSuccess(string message, string title = "SUCCESS")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Green;
                WriteLine($"[{title}] {message}");
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
        public static void WriteDebug(string message, string title = "DEBUG")
        {
            if (!_isEnabled) return;
            
            try
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                WriteLine($"[{title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[{title}] {message}");
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
        public static void WriteList<T>(string title, System.Collections.Generic.IEnumerable<T> items, string description = "")
        {
            if (!_isEnabled) return;
            
            WriteLine($"{title}:");
            foreach (var item in items)
            {
                WriteLine(description != "" ? $"  - {item} | {description}" : $"  - {item}");
            }
        }
        
        
        
    }
}