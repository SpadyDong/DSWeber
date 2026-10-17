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

        public static string FindCodexScript()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidate = Path.Combine(appData,
                @"npm\node_modules\@openai\codex\bin\codex.js");
            return File.Exists(candidate) ? candidate : null;
        }

        public static string FindArkCli()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var binDir = Path.Combine(appData, @"npm\node_modules\@volcengine\ark-cli\bin");
            if (!Directory.Exists(binDir)) return null;

            try
            {
                var candidates = Directory.GetFiles(binDir, "arkcli*.exe");
                if (candidates.Length == 0) return null;

                foreach (var candidate in candidates)
                {
                    if (candidate.IndexOf("windows-amd64", StringComparison.OrdinalIgnoreCase) >= 0)
                        return candidate;
                }
                return candidates[0];
            }
            catch
            {
                return null;
            }
        }
    }
}
