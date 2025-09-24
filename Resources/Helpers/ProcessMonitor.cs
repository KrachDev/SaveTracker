using System;
using System.IO;
using System.Management;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Helpers
{
    public static class ProcessMonitor
    {
        public static async Task<string> GetProcessFromDir(string dir, int timeout = 30)
        {
            string result = "";
            DateTime startTime = DateTime.Now;
            TimeSpan minWait = TimeSpan.FromSeconds(5); // mandatory wait
            TimeSpan maxWait = TimeSpan.FromSeconds(timeout);

            while (DateTime.Now - startTime < maxWait)
            {
                try
                {
                    string query = "SELECT ProcessId, Name, ExecutablePath FROM Win32_Process";
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);

                    foreach (var o in searcher.Get())
                    {
                        var obj = (ManagementObject)o;
                        string path = obj["ExecutablePath"]?.ToString();
                        if (path != null && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        {
                            // Found candidate
                            string exeName = Path.GetFileName(path);
                            result = exeName;
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                // Check if we've waited at least 5 sec and found something
                if (result != "" && (DateTime.Now - startTime) >= minWait)
                {
                    return result;
                }

                await Task.Delay(500); // polling interval
            }

            return result;
        }
    }
}