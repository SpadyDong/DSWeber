using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace DshTray
{
    internal sealed class BalanceResult
    {
        public bool Ok { get; set; }
        public string Currency { get; set; }
        public string TotalBalance { get; set; }
        public string GrantedBalance { get; set; }
        public string ToppedUpBalance { get; set; }
        public bool IsAvailable { get; set; }
        public string ErrorText { get; set; }
        public string ErrorDetail { get; set; }
        public DateTime FetchedAt { get; set; }
        public string KeySource { get; set; }
    }

    internal static class BalanceService
    {
        private const string Endpoint = "https://api.deepseek.com/user/balance";
        private const int TimeoutMs = 10000;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

        private static readonly object Gate = new object();
        private static BalanceResult _cached;
        private static bool _inFlight;
        private static int _generation;
        public static event Action<BalanceResult> Refreshed;

        public static string CredentialsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".dsh",
                    ".credentials.yaml");
            }
        }

        public static void Invalidate()
        {
            lock (Gate) { _cached = null; _generation++; _inFlight = false; }
        }

        public static BalanceResult GetCached()
        {
            lock (Gate) { return FreshCache(); }
        }

        public static void Refresh(AppConfig config)
        {
            Query(config, delegate { }, true);
        }

        public static void QueryAsync(AppConfig config, Action<BalanceResult> callback)
        {
            Query(config, callback, false);
        }

        private static void Query(AppConfig config, Action<BalanceResult> callback, bool force)
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
                        : Failure("正在查询中，请稍候…", null));
                    return;
                }

                _inFlight = true;
                generation = _generation;
            }

            string keySource;
            string key = ResolveKey(config, out keySource);

            ThreadPool.QueueUserWorkItem(delegate
            {
                BalanceResult result;
                try
                {
                    if (string.IsNullOrEmpty(key))
                    {
                        result = Failure(
                            "未找到 API Key",
                            "请在「设置…」中填写，或确认 " + CredentialsPath + " 内存在 DEEPSEEK_API_KEY");
                    }
                    else
                    {
                        result = Fetch(key);
                        result.KeySource = keySource;
                    }
                }
                catch (Exception ex)
                {
                    result = Failure("查询失败", ex.Message);
                }

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

        private static BalanceResult FreshCache()
        {
            return _cached != null && DateTime.UtcNow - _cached.FetchedAt < CacheTtl
                ? _cached : null;
        }

        private static void Dispatch(SynchronizationContext ctx, Action<BalanceResult> callback, BalanceResult result)
        {
            if (ctx != null)
                ctx.Post(delegate { callback(result); }, null);
            else
                callback(result);
        }

        public static string ResolveKey(AppConfig config, out string source)
        {
            if (config != null && !string.IsNullOrEmpty(config.ApiKey) && config.ApiKey.Trim().Length > 0)
            {
                source = "设置";
                return config.ApiKey.Trim();
            }

            string fromFile = ReadKeyFromCredentials();
            if (!string.IsNullOrEmpty(fromFile))
            {
                source = ".credentials.yaml";
                return fromFile;
            }

            source = null;
            return null;
        }

        private static string ReadKeyFromCredentials()
        {
            try
            {
                string path = CredentialsPath;
                if (!File.Exists(path)) return null;

                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    var m = Regex.Match(line, @"^DEEPSEEK_API_KEY\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;

                    var value = m.Groups[1].Value.Trim().Trim('"', '\'');
                    if (value.Length > 0) return value;
                }
            }
            catch
            {
            }

            return null;
        }

        private static BalanceResult Fetch(string apiKey)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

            var request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "GET";
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.Accept = "application/json";
            request.UserAgent = "DshTray/1.0";
            request.Headers.Add("Authorization", "Bearer " + apiKey);
            request.KeepAlive = false;
            request.Proxy = WebRequest.DefaultWebProxy;

            string body;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    body = reader.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                return Failure(DescribeWebError(wex), ReadErrorBody(wex));
            }

            return Parse(body);
        }

        private static string DescribeWebError(WebException wex)
        {
            var http = wex.Response as HttpWebResponse;
            if (http != null)
            {
                int code = (int)http.StatusCode;
                if (code == 401) return "API Key 无效（401）";
                if (code == 403) return "无权限访问（403）";
                if (code == 429) return "请求过于频繁（429）";
                return "服务返回 HTTP " + code;
            }

            if (wex.Status == WebExceptionStatus.Timeout) return "请求超时";
            if (wex.Status == WebExceptionStatus.NameResolutionFailure) return "域名解析失败";
            if (wex.Status == WebExceptionStatus.ConnectFailure) return "网络连接失败";
            if (wex.Status == WebExceptionStatus.TrustFailure) return "TLS 证书校验失败";
            if (wex.Status == WebExceptionStatus.SecureChannelFailure) return "TLS 握手失败";

            return "网络错误";
        }

        private static string ReadErrorBody(WebException wex)
        {
            try
            {
                if (wex.Response == null) return wex.Message;
                using (var stream = wex.Response.GetResponseStream())
                {
                    if (stream == null) return wex.Message;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var text = reader.ReadToEnd().Trim();
                        if (text.Length == 0) return wex.Message;
                        if (text.Length > 160) text = text.Substring(0, 160) + "…";
                        return text;
                    }
                }
            }
            catch
            {
                return wex.Message;
            }
        }

        private static BalanceResult Parse(string json)
        {
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) return Failure("返回内容无法解析", null);

            bool available = false;
            object availObj;
            if (root.TryGetValue("is_available", out availObj) && availObj is bool)
                available = (bool)availObj;

            object infosObj;
            if (!root.TryGetValue("balance_infos", out infosObj))
                return Failure("返回内容缺少余额字段", null);

            var infos = infosObj as object[];
            if (infos == null || infos.Length == 0)
                return Failure("账户无余额信息", null);

            var first = infos[0] as Dictionary<string, object>;
            if (first == null) return Failure("余额信息格式异常", null);

            return new BalanceResult
            {
                Ok = true,
                IsAvailable = available,
                Currency = Str(first, "currency"),
                TotalBalance = Str(first, "total_balance"),
                GrantedBalance = Str(first, "granted_balance"),
                ToppedUpBalance = Str(first, "topped_up_balance"),
                FetchedAt = DateTime.UtcNow
            };
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return "";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static BalanceResult Failure(string text, string detail)
        {
            return new BalanceResult
            {
                Ok = false,
                ErrorText = text,
                ErrorDetail = detail,
                FetchedAt = DateTime.UtcNow
            };
        }
    }
}
