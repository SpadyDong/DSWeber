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
    internal sealed class ArkPlan
    {
        public bool Ok { get; set; }
        public int? SessionLeftPercent { get; set; }
        public int? MonthlyLeftPercent { get; set; }
        public string ErrorText { get; set; }
        public string ErrorDetail { get; set; }
        public DateTime FetchedAt { get; set; }
    }

    internal static class ArkPlanService
    {
        private const int TimeoutMs = 8000;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

        private static readonly object Gate = new object();
        private static ArkPlan _cached;
        private static bool _inFlight;
        private static int _generation;
        public static event Action<ArkPlan> Refreshed;

        public static ArkPlan GetCached()
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

        public static void QueryAsync(AppConfig config, Action<ArkPlan> callback)
        {
            Query(config, callback, false);
        }

        private static void Query(AppConfig config, Action<ArkPlan> callback, bool force)
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
                        : Failure("火山方舟正在查询中，请稍候…", null));
                    return;
                }

                _inFlight = true;
                generation = _generation;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                ArkPlan result;
                try
                {
                    result = Fetch(config);
                }
                catch (Exception ex)
                {
                    result = Failure("火山方舟查询失败", ex.Message);
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

        private static ArkPlan FreshCache()
        {
            return _cached != null && DateTime.UtcNow - _cached.FetchedAt < CacheTtl
                ? _cached : null;
        }

        private static void Dispatch(SynchronizationContext ctx, Action<ArkPlan> callback, ArkPlan result)
        {
            if (ctx != null)
                ctx.Post(delegate { callback(result); }, null);
            else
                callback(result);
        }

        private static ArkPlan Fetch(AppConfig config)
        {
            string exe = Probe.FindArkCli();
            if (string.IsNullOrEmpty(exe))
                return Failure("未检测到火山方舟 CLI", "未找到 npm 全局安装的 @volcengine/ark-cli");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "usage plan --product coding-plan --format json",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.EnvironmentVariables["ARKCLI_NO_UPDATE_NOTIFIER"] = "1";
            psi.EnvironmentVariables["ARKCLI_CALLER_TYPE"] = "ai_agent";
            psi.EnvironmentVariables["ARKCLI_CALLER_NAME"] = "trae";
            psi.EnvironmentVariables["ARKCLI_SKILL_NAME"] = "arkcli-usage";

            Process proc = null;
            try
            {
                proc = new Process { StartInfo = psi };
                proc.Start();

                var utf8 = new UTF8Encoding(false);
                var stdout = new StreamReader(proc.StandardOutput.BaseStream, utf8);
                var stderr = new StreamReader(proc.StandardError.BaseStream, utf8);

                var stderrText = new StringBuilder();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        string line;
                        while ((line = stderr.ReadLine()) != null)
                        {
                            if (stderrText.Length < 400) stderrText.AppendLine(line);
                        }
                    }
                    catch
                    {
                    }
                });

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

                string outText = stdout.ReadToEnd();
                proc.WaitForExit();

                return Parse(outText, stderrText.ToString());
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

        private static ArkPlan Parse(string stdout, string stderr)
        {
            string json = ExtractJson(stdout);
            if (json == null)
            {
                string detail = FirstLine(stderr);
                if (detail == null) detail = FirstLine(stdout);
                return Failure("火山方舟查询失败", detail);
            }

            try
            {
                var ser = new JavaScriptSerializer();
                var root = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return Failure("火山方舟返回无法解析", null);

                object okObj;
                if (root.TryGetValue("ok", out okObj) && okObj is bool && !(bool)okObj)
                {
                    string message = DescribeError(root);
                    if (LooksLikeAuthFailure(message))
                        return Failure("登录已过期", "请在托盘菜单点「重新登录火山方舟」");
                    return Failure("火山方舟未就绪", message);
                }

                object itemsObj;
                if (!root.TryGetValue("items", out itemsObj) || !(itemsObj is object[]))
                    return Failure("火山方舟返回缺少 items", null);

                foreach (var raw in (object[])itemsObj)
                {
                    var item = raw as Dictionary<string, object>;
                    if (item == null) continue;
                    if (!string.Equals(Str(item, "product"), "coding-plan", StringComparison.OrdinalIgnoreCase))
                        continue;

                    object subObj;
                    if (!(item.TryGetValue("subscribed", out subObj) && subObj is bool && (bool)subObj))
                        return Failure("未订阅 Coding Plan", null);

                    var plan = new ArkPlan { Ok = true };

                    object periodsObj;
                    if (item.TryGetValue("periods", out periodsObj) && periodsObj is object[])
                    {
                        foreach (var rawPeriod in (object[])periodsObj)
                        {
                            var period = rawPeriod as Dictionary<string, object>;
                            if (period == null) continue;

                            string label = Str(period, "label");
                            int? left = LeftPercent(period);
                            if (left == null) continue;

                            if (string.Equals(label, "session", StringComparison.OrdinalIgnoreCase))
                                plan.SessionLeftPercent = left;
                            else if (string.Equals(label, "monthly", StringComparison.OrdinalIgnoreCase))
                                plan.MonthlyLeftPercent = left;
                        }
                    }

                    if (plan.SessionLeftPercent == null && plan.MonthlyLeftPercent == null)
                        return Failure("Coding Plan 无配额数据", null);

                    return plan;
                }

                return Failure("未订阅 Coding Plan", null);
            }
            catch (Exception ex)
            {
                return Failure("火山方舟解析失败", ex.Message);
            }
        }

        private static int? LeftPercent(Dictionary<string, object> period)
        {
            object percentObj;
            if (!period.TryGetValue("percent", out percentObj) || percentObj == null) return null;

            double used;
            try
            {
                used = Convert.ToDouble(percentObj, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }

            double left = 100.0 - used;
            if (left < 0) left = 0;
            if (left > 100) left = 100;
            return (int)Math.Round(left, MidpointRounding.AwayFromZero);
        }

        private static string ExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            return text.Substring(start, end - start + 1);
        }

        private static string DescribeError(Dictionary<string, object> root)
        {
            object errObj;
            if (!root.TryGetValue("error", out errObj) || errObj == null) return null;

            var errMap = errObj as Dictionary<string, object>;
            if (errMap == null) return Convert.ToString(errObj, CultureInfo.InvariantCulture);

            string message = Str(errMap, "message");
            if (!string.IsNullOrEmpty(message)) return message;
            return Convert.ToString(errObj, CultureInfo.InvariantCulture);
        }

        private static bool LooksLikeAuthFailure(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            return message.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("SSO", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("STS", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("refresh_token", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("access_token", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("未登录", StringComparison.Ordinal) >= 0
                || message.IndexOf("鉴权", StringComparison.Ordinal) >= 0
                || message.IndexOf("凭证", StringComparison.Ordinal) >= 0;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0) return line.Length > 160 ? line.Substring(0, 160) : line;
            }
            return null;
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static ArkPlan Failure(string text, string detail)
        {
            return new ArkPlan
            {
                Ok = false,
                ErrorText = text,
                ErrorDetail = detail
            };
        }
    }
}
