using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DshTray
{
    internal sealed class AppConfig
    {
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
        public string ApiKey { get; set; }

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

            if (Port < 0 || Port > 65535)
                errors.Add("Port 无效，必须为 0-65535（0 表示由系统分配空闲端口）");

            return errors;
        }

        public static AppConfig Load()
        {
            if (!File.Exists(ConfigPath))
                return null;

            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            var ser = new JavaScriptSerializer();
            return ser.Deserialize<AppConfig>(json);
        }

        public void Save()
        {
            var lines = new List<string>
            {
                Quote("NodePath", NodePath),
                Quote("DshScript", DshScript),
                Quote("WorkingDirectory", WorkingDirectory),
                Quote("Host", Host),
                Raw("Port", Port.ToString()),
                Raw("NoOpen", NoOpen ? "true" : "false"),
                Raw("AutoRestart", AutoRestart ? "true" : "false"),
                Raw("AutoStart", AutoStart ? "true" : "false"),
                Raw("ShowBall", ShowBall ? "true" : "false"),
                Raw("BallX", BallX.ToString()),
                Raw("BallY", BallY.ToString()),
                Quote("ApiKey", ApiKey)
            };

            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append(string.Join(",\r\n", lines.ToArray()));
            sb.Append("\r\n}\r\n");

            File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Quote(string name, string value)
        {
            return string.Format("  \"{0}\": \"{1}\"", name, Escape(value));
        }

        private static string Raw(string name, string literal)
        {
            return string.Format("  \"{0}\": {1}", name, literal);
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}