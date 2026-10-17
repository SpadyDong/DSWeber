using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DshTray
{
    internal sealed class CodexWindow
    {
        public string Label { get; set; }
        public int LeftPercent { get; set; }
        public DateTime? ResetsAtLocal { get; set; }
    }

    internal sealed class CodexQuota
    {
        public bool Ok { get; set; }
        public string PlanType { get; set; }
        public List<CodexWindow> Windows { get; set; }
        public string ErrorText { get; set; }
        public string ErrorDetail { get; set; }
        public DateTime FetchedAt { get; set; }
    }

    internal static class CodexQuotaService
    {
        private const int TimeoutMs = 8000;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

        private static readonly object Gate = new object();
        private static CodexQuota _cached;
        private static bool _inFlight;
        private static int _generation;
        public static event Action<CodexQuota> Refreshed;

        public static CodexQuota GetCached()
        {
            lock (Gate) { return FreshCache(); }
        }

        public static void Invalidate()
        {
            lock (Gate) { _cached = null; _generation++; _inFlight = false; }
        }

        public static void Refresh(AppConfig config)
        {
            Query(config, delegate { }, true);
        }

        public static void QueryAsync(AppConfig config, Action<CodexQuota> callback)
        {
            Query(config, callback, false);
        }

        private static void Query(AppConfig config, Action<CodexQuota> callback, bool force)
        {
            if (callback == null) return;

            var ctx = SynchronizationContext.Current;

            int generation;
            lock (Gate)
            {
                var cached = FreshCache();
                if (!force && cached != null)
                {
                    Dispatch(ctx, callback, cached);
                    return;
                }

                if (_inFlight)
                {
                    Dispatch(ctx, callback, cached != null
                        ? cached
                        : Failure("Codex 正在查询中，请稍候…", null));
                    return;
                }

                _inFlight = true;
                generation = _generation;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                CodexQuota result;
                try
                {
                    result = Fetch(config);
                }
                catch (Exception ex)
                {
                    result = Failure("Codex 查询失败", ex.Message);
                }

                result.FetchedAt = DateTime.UtcNow;
                bool current;
                lock (Gate)
                {
                    current = generation == _generation;
                    if (current)
                    {
                        _inFlight = false;
                        _cached = result;
                    }
                }

                if (!current) return;
                Dispatch(ctx, callback, result);
                var refreshed = Refreshed;
                if (refreshed != null) refreshed(result);
            });
        }

        private static CodexQuota FreshCache()
        {
            return _cached != null && DateTime.UtcNow - _cached.FetchedAt < CacheTtl
                ? _cached : null;
        }

        private static void Dispatch(SynchronizationContext ctx, Action<CodexQuota> callback, CodexQuota result)
        {
            if (ctx != null)
                ctx.Post(delegate { callback(result); }, null);
            else
                callback(result);
        }

        private static CodexQuota Fetch(AppConfig config)
        {
            string script = Probe.FindCodexScript();
            if (string.IsNullOrEmpty(script))
                return Failure("未检测到 Codex", "未找到 npm 全局安装的 @openai/codex");

            string node = config == null ? null : config.NodePath;
            if (string.IsNullOrEmpty(node) || !File.Exists(node))
                node = Probe.FindNode();
            if (string.IsNullOrEmpty(node))
                return Failure("未检测到 node.exe", "请在「设置」中填写 node.exe 路径");

            var psi = new ProcessStartInfo
            {
                FileName = node,
                Arguments = "\"" + script + "\" app-server --listen stdio://",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (config != null && !string.IsNullOrEmpty(config.WorkingDirectory)
                && Directory.Exists(config.WorkingDirectory))
            {
                psi.WorkingDirectory = config.WorkingDirectory;
            }

            Process proc = null;
            try
            {
                proc = new Process { StartInfo = psi };
                proc.Start();

                var utf8 = new UTF8Encoding(false);
                var stdin = proc.StandardInput;
                var stdout = new StreamReader(proc.StandardOutput.BaseStream, utf8);
                var stderr = new StreamReader(proc.StandardError.BaseStream, utf8);

                // 排空 stderr，避免子进程写满管道而阻塞
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { while (stderr.ReadLine() != null) { } } catch { }
                });

                // 看门狗：超时强杀，令 stdout 结束，ReadLine 返回 null
                var watchdog = new Thread(delegate()
                {
                    try
                    {
                        if (!proc.WaitForExit(TimeoutMs))
                        {
                            try { proc.Kill(); } catch { }
                        }
                    }
                    catch
                    {
                    }
                });
                watchdog.IsBackground = true;
                watchdog.Start();

                stdin.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"dshtray\",\"version\":\"1.0.0\"}}}");
                stdin.Flush();

                string rateLine = null;
                bool inited = false;
                string line;

                while ((line = stdout.ReadLine()) != null)
                {
                    int id = ReadId(line);
                    if (id == 1)
                    {
                        if (inited) continue;
                        inited = true;
                        stdin.WriteLine("{\"method\":\"initialized\"}");
                        stdin.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":null}");
                        stdin.Flush();
                    }
                    else if (id == 2)
                    {
                        rateLine = line;
                        break;
                    }
                }

                if (rateLine == null)
                    return Failure("Codex 查询超时",
                        "未在 " + (TimeoutMs / 1000) + " 秒内返回配额数据");

                return Parse(rateLine);
            }
            finally
            {
                if (proc != null)
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                    try { proc.Dispose(); } catch { }
                }
            }
        }

        private static int ReadId(string line)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                var obj = ser.DeserializeObject(line) as Dictionary<string, object>;
                if (obj == null) return 0;

                object id;
                if (!obj.TryGetValue("id", out id) || id == null) return 0;
                return Convert.ToInt32(id, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static CodexQuota Parse(string line)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                var root = ser.DeserializeObject(line) as Dictionary<string, object>;
                if (root == null) return Failure("Codex 返回无法解析", null);

                object errObj;
                if (root.TryGetValue("error", out errObj) && errObj != null)
                    return Failure("Codex 返回错误", Describe(errObj));

                object resObj;
                if (!root.TryGetValue("result", out resObj) || resObj == null)
                    return Failure("Codex 返回缺少 result", null);

                var res = resObj as Dictionary<string, object>;
                if (res == null) return Failure("Codex 返回格式异常", null);

                object rlObj;
                if (!res.TryGetValue("rateLimits", out rlObj) || rlObj == null)
                    return Failure("Codex 未返回配额", null);

                var rl = rlObj as Dictionary<string, object>;
                if (rl == null) return Failure("Codex 配额格式异常", null);

                var quota = new CodexQuota
                {
                    Ok = true,
                    PlanType = Str(rl, "planType"),
                    Windows = new List<CodexWindow>()
                };

                AddWindow(quota.Windows, rl, "primary", "5 小时");
                AddWindow(quota.Windows, rl, "secondary", "7 天");

                if (quota.Windows.Count == 0)
                    return Failure("Codex 配额为空", "当前账户可能不包含速率窗口");

                return quota;
            }
            catch (Exception ex)
            {
                return Failure("Codex 解析失败", ex.Message);
            }
        }

        private static void AddWindow(List<CodexWindow> windows, Dictionary<string, object> rateLimits,
                                      string key, string fallbackLabel)
        {
            object windowObj;
            if (!rateLimits.TryGetValue(key, out windowObj) || windowObj == null) return;

            var window = windowObj as Dictionary<string, object>;
            if (window == null) return;

            object usedObj;
            if (!window.TryGetValue("usedPercent", out usedObj) || usedObj == null) return;

            int used;
            try
            {
                used = Convert.ToInt32(usedObj, CultureInfo.InvariantCulture);
            }
            catch
            {
                return;
            }

            int left = 100 - used;
            if (left < 0) left = 0;
            if (left > 100) left = 100;

            windows.Add(new CodexWindow
            {
                Label = WindowLabel(window, fallbackLabel),
                LeftPercent = left,
                ResetsAtLocal = ReadResetsAt(window)
            });
        }

        private static DateTime? ReadResetsAt(Dictionary<string, object> window)
        {
            object resetObj;
            if (!window.TryGetValue("resetsAt", out resetObj) || resetObj == null) return null;

            long seconds;
            try
            {
                seconds = Convert.ToInt64(resetObj, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }

            if (seconds <= 0) return null;

            try
            {
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return epoch.AddSeconds(seconds).ToLocalTime();
            }
            catch
            {
                return null;
            }
        }

        private static string WindowLabel(Dictionary<string, object> window, string fallback)
        {
            object durObj;
            if (!window.TryGetValue("windowDurationMins", out durObj) || durObj == null)
                return fallback;

            long minutes;
            try
            {
                minutes = Convert.ToInt64(durObj, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }

            if (minutes <= 0) return fallback;
            if (minutes == 300) return "5 小时";
            if (minutes == 10080) return "7 天";
            if (minutes % 1440 == 0) return (minutes / 1440) + " 天";
            if (minutes % 60 == 0) return (minutes / 60) + " 小时";
            return minutes + " 分钟";
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string Describe(object errObj)
        {
            var map = errObj as Dictionary<string, object>;
            if (map == null) return Convert.ToString(errObj, CultureInfo.InvariantCulture);

            string message = Str(map, "message");
            if (!string.IsNullOrEmpty(message)) return message;
            return Convert.ToString(errObj, CultureInfo.InvariantCulture);
        }

        private static CodexQuota Failure(string text, string detail)
        {
            return new CodexQuota
            {
                Ok = false,
                ErrorText = text,
                ErrorDetail = detail
            };
        }
    }
}
