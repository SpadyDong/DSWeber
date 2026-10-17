using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DshTray
{
    internal sealed class AlarmItem
    {
        public bool Enabled { get; set; }
        public string Time { get; set; }
        public string Message { get; set; }
    }

    internal sealed class AppConfig
    {
        private const string ProtectedKeyPrefix = "dpapi:";
        public static readonly string ConfigPath = Path.Combine(
            Path.GetDirectoryName(typeof(AppConfig).Assembly.Location),
            "config.json"
        );

        public string NodePath { get; set; }
        public string DshScript { get; set; }
        public string WorkingDirectory { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool NoOpen { get; set; }
        public bool AutoRestart { get; set; }
        public bool AutoStart { get; set; }
        public bool ShowBall { get; set; }
        public int BallX { get; set; }
        public int BallY { get; set; }
        public bool ShowCodexQuota { get; set; }
        public string ApiKey { get; set; }
        public List<AlarmItem> Alarms { get; set; }

        public AppConfig()
        {
            Port = 3080;
            AutoStart = true;
            AutoRestart = true;
            ShowBall = true;
            ShowCodexQuota = true;
        }

        public void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(NodePath)) NodePath = Probe.FindNode();
            if (string.IsNullOrWhiteSpace(DshScript)) DshScript = Probe.FindDshScript();
            if (string.IsNullOrWhiteSpace(WorkingDirectory))
                WorkingDirectory = Path.GetDirectoryName(ConfigPath);
            if (string.IsNullOrWhiteSpace(Host)) Host = "127.0.0.1";
            if (ApiKey == null) ApiKey = "";
            if (Alarms == null) Alarms = new List<AlarmItem>();
        }

        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(NodePath))
                errors.Add("NodePath 未配置（node.exe 的完整路径）");
            else if (!File.Exists(NodePath))
                errors.Add("NodePath 指向的文件不存在：" + NodePath);

            if (string.IsNullOrWhiteSpace(DshScript))
                errors.Add("DshScript 未配置（dsh 的 bin.js 完整路径）");
            else if (!File.Exists(DshScript))
                errors.Add("DshScript 指向的文件不存在：" + DshScript);

            if (string.IsNullOrWhiteSpace(WorkingDirectory))
                errors.Add("WorkingDirectory 未配置（dsh web 的启动目录）");
            else if (!Directory.Exists(WorkingDirectory))
                errors.Add("WorkingDirectory 指向的目录不存在：" + WorkingDirectory);

            if (Port < 1 || Port > 65535)
                errors.Add("Port 无效，必须为 1-65535");

            return errors;
        }

        public static AppConfig Load()
        {
            if (!File.Exists(ConfigPath))
                return null;

            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            var ser = new JavaScriptSerializer();
            var cfg = ser.Deserialize<AppConfig>(json);
            if (cfg == null) throw new InvalidDataException("配置文件内容为空或不是 JSON 对象");
            var fields = ser.Deserialize<Dictionary<string, object>>(json);
            if (fields == null) throw new InvalidDataException("配置文件不是 JSON 对象");
            if (!fields.ContainsKey("Port")) cfg.Port = 3080;
            if (!fields.ContainsKey("AutoStart")) cfg.AutoStart = true;
            if (!fields.ContainsKey("AutoRestart")) cfg.AutoRestart = true;
            if (!fields.ContainsKey("ShowBall")) cfg.ShowBall = true;
            if (!fields.ContainsKey("ShowCodexQuota")) cfg.ShowCodexQuota = true;
            if (cfg.Alarms == null)
                cfg.Alarms = MigrateLegacyAlarm(ser, json);
            if (!string.IsNullOrEmpty(cfg.ApiKey) && cfg.ApiKey.StartsWith(ProtectedKeyPrefix, StringComparison.Ordinal))
                cfg.ApiKey = UnprotectKey(cfg.ApiKey.Substring(ProtectedKeyPrefix.Length));
            return cfg;
        }

        private static List<AlarmItem> MigrateLegacyAlarm(JavaScriptSerializer ser, string json)
        {
            try
            {
                var dict = ser.Deserialize<Dictionary<string, object>>(json);
                if (dict == null || !dict.ContainsKey("AlarmEnabled"))
                    return new List<AlarmItem>();

                bool enabled = dict["AlarmEnabled"] is bool && (bool)dict["AlarmEnabled"];
                string time = dict.ContainsKey("AlarmTime") ? dict["AlarmTime"] as string : null;
                string msg = dict.ContainsKey("AlarmMessage") ? dict["AlarmMessage"] as string : null;

                var list = new List<AlarmItem>();
                if (enabled || !string.IsNullOrWhiteSpace(time) || !string.IsNullOrWhiteSpace(msg))
                {
                    list.Add(new AlarmItem
                    {
                        Enabled = enabled,
                        Time = time ?? "",
                        Message = msg ?? ""
                    });
                }
                return list;
            }
            catch
            {
                return new List<AlarmItem>();
            }
        }

        public void Save()
        {
            var fields = new Dictionary<string, object>
            {
                { "NodePath", NodePath }, { "DshScript", DshScript },
                { "WorkingDirectory", WorkingDirectory }, { "Host", Host },
                { "Port", Port }, { "NoOpen", NoOpen },
                { "AutoRestart", AutoRestart }, { "AutoStart", AutoStart },
                { "ShowBall", ShowBall }, { "BallX", BallX }, { "BallY", BallY },
                { "ShowCodexQuota", ShowCodexQuota },
                { "ApiKey", string.IsNullOrEmpty(ApiKey) ? "" : ProtectedKeyPrefix + ProtectKey(ApiKey) },
                { "Alarms", Alarms ?? new List<AlarmItem>() }
            };
            string json = new JavaScriptSerializer().Serialize(fields);
            string temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(ConfigPath)) File.Replace(temporary, ConfigPath, null);
                else File.Move(temporary, ConfigPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string ProtectKey(string key)
        {
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        private static string UnprotectKey(string value)
        {
            byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
