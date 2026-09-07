using System;
using System.IO;

namespace DshTray
{
    internal static class Probe
    {
        public static string FindNode()
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVar))
            {
                foreach (var dir in pathVar.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        var candidate = Path.Combine(dir.Trim(), "node.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }

        public static string FindDshScript()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidate = Path.Combine(appData,
                @"npm\node_modules\@deepseek-ai\dsh\lib\bin.js");
            return File.Exists(candidate) ? candidate : null;
        }
    }
}
