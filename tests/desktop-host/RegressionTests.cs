using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace DshTray
{
    internal static class ReviewTests
    {
        private static int _checks;

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            _checks++;
        }

        private static void SetCache(Type service, object value)
        {
            service.GetField("_cached", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
        }

        public static void Main()
        {
            try { Run(); }
            catch (Exception ex)
            {
                Console.WriteLine(ex.GetType().FullName + ": " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void Run()
        {
            var cfg = new AppConfig
            {
                Port = 4321,
                AutoStart = false,
                AutoRestart = false,
                ShowBall = false,
                ShowCodexQuota = false,
                ApiKey = "test-secret-123",
                Alarms = new List<AlarmItem> {
                    new AlarmItem { Enabled = true, Time = "08:30", Message = "第一行\r\n第二行 \"引号\"" }
                }
            };
            cfg.Save();
            string raw = File.ReadAllText(AppConfig.ConfigPath, Encoding.UTF8);
            Check(!raw.Contains("test-secret-123"), "API Key written as plaintext");
            var loaded = AppConfig.Load();
            loaded.ApplyDefaults();
            Check(!loaded.AutoStart && !loaded.AutoRestart && !loaded.ShowBall && !loaded.ShowCodexQuota,
                "explicit false settings overwritten");
            Check(loaded.Port == 4321, "port changed");
            Check(loaded.ApiKey == cfg.ApiKey, "protected key did not round-trip");
            Check(loaded.Alarms[0].Message == cfg.Alarms[0].Message, "multiline alarm did not round-trip");

            File.WriteAllText(AppConfig.ConfigPath, "{bad json", Encoding.UTF8);
            bool rejected = false;
            try { AppConfig.Load(); } catch { rejected = true; }
            Check(rejected, "malformed config accepted");

            File.WriteAllText(AppConfig.ConfigPath, "{\"Port\":0,\"ApiKey\":\"legacy-key\"}", Encoding.UTF8);
            var legacy = AppConfig.Load();
            Check(legacy.AutoStart && legacy.AutoRestart && legacy.ShowBall && legacy.ShowCodexQuota,
                "missing legacy defaults not applied");
            Check(legacy.Validate().Exists(x => x.Contains("Port")), "unsupported port zero accepted");
            legacy.Port = 3080;
            legacy.Save();
            Check(!File.ReadAllText(AppConfig.ConfigPath, Encoding.UTF8).Contains("legacy-key"),
                "legacy plaintext key not migrated");

            var alarms = new List<AlarmItem> {
                new AlarmItem { Enabled = true, Time = "00:00", Message = "midnight" },
                new AlarmItem { Enabled = true, Time = "08:30", Message = "morning" }
            };
            var due = AlarmSchedule.DueMessages(new DateTime(2026, 10, 15, 23, 59, 30),
                new DateTime(2026, 10, 16, 0, 0, 15), alarms);
            Check(due.Count == 1 && due[0].Contains("midnight"), "on-time midnight alarm failed");
            due = AlarmSchedule.DueMessages(new DateTime(2026, 10, 15, 8, 29, 30),
                new DateTime(2026, 10, 15, 8, 30, 15), alarms);
            Check(due.Count == 1 && due[0].Contains("morning"), "on-time alarm failed");
            due = AlarmSchedule.DueMessages(new DateTime(2026, 10, 15, 8, 30, 15),
                new DateTime(2026, 10, 15, 8, 30, 45), alarms);
            Check(due.Count == 0, "alarm fired twice in one minute");
            due = AlarmSchedule.DueMessages(new DateTime(2026, 10, 15, 8, 29, 30),
                new DateTime(2026, 10, 15, 8, 31, 0), alarms);
            Check(due.Count == 0, "missed alarm was replayed");

            SetCache(typeof(BalanceService), new BalanceResult { Ok = true,
                FetchedAt = DateTime.UtcNow.AddMinutes(-3) });
            Check(BalanceService.GetCached() == null, "stale balance shown");
            SetCache(typeof(BalanceService), new BalanceResult { Ok = false,
                FetchedAt = DateTime.UtcNow });
            Check(BalanceService.GetCached() != null && !BalanceService.GetCached().Ok,
                "recent balance error hidden");

            Console.WriteLine("Passed " + _checks + " checks");
        }
    }
}
