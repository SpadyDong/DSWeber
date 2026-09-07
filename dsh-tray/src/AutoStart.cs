using System;
using Microsoft.Win32;

namespace DshTray
{
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DshTray";

        private static string ExePath
        {
            get { return "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\""; }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    var v = key.GetValue(ValueName) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Apply(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;

                if (enabled)
                {
                    key.SetValue(ValueName, ExePath, RegistryValueKind.String);
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}
