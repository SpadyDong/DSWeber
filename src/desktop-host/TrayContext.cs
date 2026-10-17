using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class TrayContext : ApplicationContext
    {
        public const string DuplicateLaunchEventName = "Local\\DshTray_DuplicateLaunch";

        private const int BalloonMs = 5000;
        private const int ScanIntervalMs = 60000;
        private const int QuotaRefreshMs = 60000;

        private static readonly Color ColorRunning = Color.FromArgb(77, 107, 254);
        private static readonly Color ColorStopped = Color.FromArgb(150, 150, 150);
        private static readonly Color ColorCrashed = Color.FromArgb(214, 69, 69);

        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _startStopItem;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ToolStripMenuItem _showBallItem;
        private readonly ToolStripMenuItem _alarmItem;
        private readonly ToolStripMenuItem _reloginArkItem;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _exitItem;
        private readonly ToolStripSeparator _exitSeparator;
        private bool _ballMenuPending;

        private AppConfig _config;
        private DshSupervisor _supervisor;
        private FloatingBall _ball;
        private Icon _currentIcon;
        private bool _exiting;
        private readonly System.Threading.SynchronizationContext _ui;

        private System.Threading.EventWaitHandle _duplicateEvent;
        private System.Threading.RegisteredWaitHandle _duplicateWait;
        private Timer _scanTimer;
        private ServiceState _lastNotifiedState;
        private Timer _alarmTimer;
        private AlarmPopup _alarmPopup;
        private DateTime _lastAlarmCheck;
        private Timer _quotaTimer;

        public TrayContext(AppConfig config)
        {
            _config = config;

            _statusItem = new ToolStripMenuItem("状态", null, OnShowStatus);
            _openItem = new ToolStripMenuItem("打开 Web 界面", null, OnOpenWeb);
            _startStopItem = new ToolStripMenuItem("停止", null, OnStartStop);
            _autoStartItem = new ToolStripMenuItem("开机启动", null, OnToggleAutoStart) { CheckOnClick = false };
            _showBallItem = new ToolStripMenuItem("桌面悬浮球", null, OnToggleBall) { CheckOnClick = false };
            _alarmItem = new ToolStripMenuItem("闹钟", null, OnAlarmSettings);
            _reloginArkItem = new ToolStripMenuItem("重新登录火山方舟", null, OnReloginArk);
            _settingsItem = new ToolStripMenuItem("设置", null, OnSettings);
            _exitItem = new ToolStripMenuItem("退出", null, OnExit);
            _exitSeparator = new ToolStripSeparator();

            _menu = new ContextMenuStrip();
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_openItem);
            _menu.Items.Add(_startStopItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_autoStartItem);
            _menu.Items.Add(_showBallItem);
            _menu.Items.Add(_alarmItem);
            _menu.Items.Add(_reloginArkItem);
            _menu.Items.Add(_settingsItem);
            _menu.Items.Add(_exitSeparator);
            _menu.Items.Add(_exitItem);
            _menu.Opening += OnMenuOpening;

            _tray = new NotifyIcon
            {
                ContextMenuStrip = _menu,
                Visible = true,
                Text = "dsh web"
            };
            _tray.DoubleClick += OnOpenWeb;

            _ui = System.Threading.SynchronizationContext.Current
                  ?? new WindowsFormsSynchronizationContext();

            BalanceService.Refreshed += OnBalanceRefreshed;
            CodexQuotaService.Refreshed += OnCodexRefreshed;
            ArkPlanService.Refreshed += OnArkRefreshed;

            AutoStart.Apply(_config.AutoStart);
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _showBallItem.Checked = _config.ShowBall;

            _lastNotifiedState = ServiceState.Stopped;
            SetupDuplicateLaunchListener();
            SetupConflictScanner();
            SetupAlarmTimer();
            SetupQuotaTimer();
            OnQuotaTick(null, EventArgs.Empty);

            AttachSupervisor();
            if (_config.ShowBall) CreateBall();
            _supervisor.Start();
        }

        private void SetupDuplicateLaunchListener()
        {
            try
            {
                bool created;
                _duplicateEvent = new System.Threading.EventWaitHandle(
                    false,
                    System.Threading.EventResetMode.AutoReset,
                    DuplicateLaunchEventName,
                    out created);

                _duplicateWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(
                    _duplicateEvent,
                    OnDuplicateLaunchSignalled,
                    null,
                    System.Threading.Timeout.Infinite,
                    false);
            }
            catch
            {
                _duplicateEvent = null;
                _duplicateWait = null;
            }
        }

        private void OnDuplicateLaunchSignalled(object state, bool timedOut)
        {
            if (timedOut || _exiting) return;
            try
            {
                _ui.Post(delegate
                {
                    if (_exiting) return;
                    ShowBalloon("dsh 托盘已经在运行",
                        "刚才那次启动已自动退出，避免与当前实例冲突。托盘图标就在这里。");
                }, null);
            }
            catch
            {
            }
        }

        private void SetupConflictScanner()
        {
            _scanTimer = new Timer();
            _scanTimer.Interval = ScanIntervalMs;
            _scanTimer.Tick += OnConflictScan;
            _scanTimer.Start();
        }

        private void SetupAlarmTimer()
        {
            _lastAlarmCheck = DateTime.Now;
            _alarmTimer = new Timer();
            _alarmTimer.Interval = 30000;
            _alarmTimer.Tick += OnAlarmTick;
            _alarmTimer.Start();
        }

        private void SetupQuotaTimer()
        {
            _quotaTimer = new Timer();
            _quotaTimer.Interval = QuotaRefreshMs;
            _quotaTimer.Tick += OnQuotaTick;
            _quotaTimer.Start();
        }

        private void OnQuotaTick(object sender, EventArgs e)
        {
            if (_exiting || _config == null) return;

            BalanceService.Refresh(_config);
            if (_config.ShowCodexQuota) CodexQuotaService.Refresh(_config);
            ArkPlanService.Refresh(_config);
        }

        private void OnBalanceRefreshed(BalanceResult result)
        {
            try { _ui.Post(delegate {
                if (!_exiting && ReferenceEquals(BalanceService.GetCached(), result) && _ball != null)
                    _ball.UpdateBalance(result);
            }, null); } catch { }
        }

        private void OnCodexRefreshed(CodexQuota result)
        {
            try { _ui.Post(delegate {
                if (!_exiting && ReferenceEquals(CodexQuotaService.GetCached(), result) && _ball != null)
                    _ball.UpdateCodex(result);
            }, null); } catch { }
        }

        private void OnArkRefreshed(ArkPlan result)
        {
            try { _ui.Post(delegate {
                if (!_exiting && ReferenceEquals(ArkPlanService.GetCached(), result) && _ball != null)
                    _ball.UpdateArk(result);
            }, null); } catch { }
        }

        private void OnAlarmTick(object sender, EventArgs e)
        {
            if (_exiting) return;
            DateTime now = DateTime.Now;
            DateTime previous = _lastAlarmCheck;
            _lastAlarmCheck = now;
            var messages = AlarmSchedule.DueMessages(previous, now, _config.Alarms);

            if (messages.Count == 0) return;

            ShowAlarmPopup("提醒", string.Join("\r\n\r\n", messages.ToArray()));
        }

        private void ShowAlarmPopup(string time, string message)
        {
            if (_alarmPopup == null) _alarmPopup = new AlarmPopup();
            _alarmPopup.ShowMessage(time, message);
        }

        private void OnAlarmSettings(object sender, EventArgs e)
        {
            using (var dlg = new AlarmDialog(_config.Alarms))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                _config.Alarms = dlg.Result;
                _lastAlarmCheck = DateTime.Now;
                try
                {
                    _config.Save();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存闹钟设置失败：" + ex.Message, "dsh web",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnReloginArk(object sender, EventArgs e)
        {
            string exe = Probe.FindArkCli();
            if (string.IsNullOrEmpty(exe))
            {
                MessageBox.Show(
                    "未找到火山方舟 CLI（arkcli）。\r\n\r\n"
                    + "请先安装：npm i @volcengine/ark-cli -g",
                    "dsh web", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process proc;
            try
            {
                proc = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "auth login volc-sso",
                    UseShellExecute = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法启动火山方舟登录：\r\n" + ex.Message, "dsh web",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (proc == null) return;

            var cfg = _config;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { proc.WaitForExit(); } catch { }
                try
                {
                    ArkPlanService.Invalidate();
                    if (cfg != null) ArkPlanService.Refresh(cfg);
                }
                catch
                {
                }
            });
        }

        private void OnConflictScan(object sender, EventArgs e)
        {
            if (_exiting) return;

            int selfPid = _supervisor == null ? 0 : _supervisor.ProcessId;
            List<int> strays = new List<int>();

            foreach (int pid in PortGuard.FindDshWebProcesses(_config.DshScript))
            {
                if (pid != selfPid) strays.Add(pid);
            }

            if (strays.Count == 0) return;

            var sb = new StringBuilder();
            sb.Append("检测到 ");
            sb.Append(strays.Count);
            sb.Append(" 个不是由本托盘启动的 dsh web 进程（PID ");
            for (int i = 0; i < strays.Count; i++)
            {
                if (i > 0) sb.Append("、");
                sb.Append(strays[i]);
            }
            sb.Append("）。它们会和托盘抢占端口 ");
            sb.Append(_config.Port);
            sb.Append("，建议关掉那个终端。");

            ShowBalloon("发现外部的 dsh web", sb.ToString());
        }

        private void ShowBalloon(string title, string text)
        {
            try
            {
                _tray.ShowBalloonTip(BalloonMs, title, text, ToolTipIcon.Warning);
            }
            catch
            {
            }
        }

        private void CreateBall()
        {
            if (_ball != null) return;
            _ball = new FloatingBall(_supervisor,
                                     _config,
                                     EnsureOnScreen(new Point(_config.BallX, _config.BallY)),
                                     _menu,
                                     OnBallMoved,
                                     OnBallMenuRequested);
            _ball.Show();
        }

        private static Point EnsureOnScreen(Point p)
        {
            var home = Screen.FromPoint(new Point(p.X + FloatingBall.BaseBallSize / 2,
                                                 p.Y + FloatingBall.BaseBallSize / 2));
            int ballSize = Dpi.Scaled(FloatingBall.BaseBallSize, Dpi.ScaleForScreen(home));

            var probe = new Rectangle(p.X, p.Y, ballSize, ballSize);
            bool anyIntersects = false;
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(probe))
                {
                    anyIntersects = true;
                    break;
                }
            }

            if (!anyIntersects)
            {
                float primaryScale = Dpi.ScaleForScreen(Screen.PrimaryScreen);
                int primaryBall = Dpi.Scaled(FloatingBall.BaseBallSize, primaryScale);
                int margin = Dpi.Scaled(16, primaryScale);
                var area = Screen.PrimaryScreen.WorkingArea;
                return new Point(area.Right - primaryBall - margin,
                                 area.Bottom - primaryBall - margin);
            }

            return FloatingBall.ClampToWorkArea(p, ballSize);
        }

        private void DestroyBall()
        {
            if (_ball == null) return;
            _ball.Close();
            _ball.Dispose();
            _ball = null;
        }

        private void OnBallMoved(Point location)
        {
            _config.BallX = location.X;
            _config.BallY = location.Y;
            try
            {
                _config.Save();
            }
            catch
            {
            }
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            bool fromBall = _ballMenuPending;
            _ballMenuPending = false;

            _exitItem.Visible = !fromBall;
            _exitSeparator.Visible = !fromBall;
        }

        private void OnBallMenuRequested()
        {
            _ballMenuPending = true;
        }

        private void OnToggleBall(object sender, EventArgs e)
        {
            bool target = !_showBallItem.Checked;
            if (target) CreateBall(); else DestroyBall();

            _showBallItem.Checked = target;
            _config.ShowBall = target;
            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存悬浮球设置失败：" + ex.Message, "dsh web",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AttachSupervisor()
        {
            if (_supervisor != null)
            {
                _supervisor.StateChanged -= OnStateChanged;
                _supervisor.Dispose();
            }
            _supervisor = new DshSupervisor(_config);
            _supervisor.StateChanged += OnStateChanged;
            RefreshUi();
        }

        private void OnStateChanged()
        {
            if (_exiting) return;
            try
            {
                _ui.Post(delegate { if (!_exiting) RefreshUi(); }, null);
            }
            catch
            {
            }
        }

        private void RefreshUi()
        {
            Color color;
            string label;

            switch (_supervisor.State)
            {
                case ServiceState.Running:
                    color = ColorRunning;
                    label = "运行中";
                    break;
                case ServiceState.Starting:
                    color = ColorRunning;
                    label = "启动中";
                    break;
                case ServiceState.Crashed:
                    color = ColorCrashed;
                    label = "已异常退出";
                    break;
                case ServiceState.PortConflict:
                    color = ColorCrashed;
                    label = "启动失败：端口被占用";
                    break;
                default:
                    color = ColorStopped;
                    label = "已停止";
                    break;
            }

            var newIcon = IconFactory.CreateGlyphIcon(16, color);
            _tray.Icon = newIcon;
            if (_currentIcon != null) _currentIcon.Dispose();
            _currentIcon = newIcon;

            var tip = "dsh web - " + label + "\r\n" + _supervisor.WebUrl;
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            bool running = _supervisor.State == ServiceState.Running
                        || _supervisor.State == ServiceState.Starting;
            _startStopItem.Text = running ? "停止" : "启动";
            _openItem.Enabled = running;
            _statusItem.Text = "状态：" + label;

            if (_ball != null) _ball.Redraw();

            var state = _supervisor.State;
            if (state != _lastNotifiedState)
            {
                _lastNotifiedState = state;
                if (state == ServiceState.PortConflict) QueuePortConflictDialog();
            }
        }

        private void QueuePortConflictDialog()
        {
            try
            {
                _ui.Post(delegate
                {
                    if (_exiting) return;
                    if (_supervisor.State != ServiceState.PortConflict) return;

                    int owner = _supervisor.ConflictOwnerPid;
                    var sb = new StringBuilder();
                    sb.AppendLine("dsh web 启动失败：端口 " + _config.Port + " 已被其他程序占用。");
                    sb.AppendLine();
                    sb.AppendLine("占用者：" + (owner > 0 ? PortGuard.DescribeProcess(owner) : "未知"));
                    sb.AppendLine();
                    sb.Append("如果你在终端里手动运行了 dsh web，请先关闭它，再从托盘菜单点「启动」。");

                    MessageBox.Show(sb.ToString(), "dsh 托盘",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }, null);
            }
            catch
            {
            }
        }

        private void OnShowStatus(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("状态：" + _statusItem.Text.Replace("状态：", ""));
            sb.AppendLine("地址：" + _supervisor.WebUrl);

            int pid = _supervisor.ProcessId;
            sb.AppendLine("进程 PID：" + (pid > 0 ? pid.ToString() : "无"));

            if (_supervisor.State == ServiceState.Running)
            {
                var span = DateTime.Now - _supervisor.StartedAt;
                sb.AppendLine("已运行：" + FormatSpan(span));
            }

            sb.AppendLine("自动重启次数：" + _supervisor.RestartCount);

            if (!string.IsNullOrEmpty(_supervisor.LastError))
            {
                sb.AppendLine();
                sb.AppendLine("最近错误：" + _supervisor.LastError);
            }

            MessageBox.Show(sb.ToString(), "dsh web 状态",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string FormatSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return (int)span.TotalHours + " 小时 " + span.Minutes + " 分";
            if (span.TotalMinutes >= 1)
                return span.Minutes + " 分 " + span.Seconds + " 秒";
            return span.Seconds + " 秒";
        }

        private void OnOpenWeb(object sender, EventArgs e)
        {
            if (_supervisor.State != ServiceState.Running) return;
            try
            {
                Process.Start(new ProcessStartInfo(_supervisor.WebUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开浏览器：" + ex.Message, "dsh web",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnStartStop(object sender, EventArgs e)
        {
            if (_supervisor.State == ServiceState.Running || _supervisor.State == ServiceState.Starting)
            {
                _supervisor.Stop();
            }
            else
            {
                _supervisor.Restart();
            }
        }

        private void OnToggleAutoStart(object sender, EventArgs e)
        {
            bool target = !_autoStartItem.Checked;
            try
            {
                AutoStart.Apply(target);
                _autoStartItem.Checked = AutoStart.IsEnabled();
                _config.AutoStart = _autoStartItem.Checked;
                _config.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机启动失败：" + ex.Message, "dsh web",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnSettings(object sender, EventArgs e)
        {
            using (var form = new SettingsForm(_config))
            {
                if (form.ShowDialog() != DialogResult.OK) return;

                var updated = form.Result;
                try { updated.Save(); }
                catch (Exception ex)
                {
                    MessageBox.Show("保存设置失败，运行中的服务未改变：" + ex.Message, "dsh web",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _supervisor.Stop();
                DestroyBall();
                _config = updated;
                BalanceService.Invalidate();
                CodexQuotaService.Invalidate();
                ArkPlanService.Invalidate();
                try { AutoStart.Apply(_config.AutoStart); }
                catch (Exception ex)
                {
                    MessageBox.Show("设置已保存，但更新开机启动失败：" + ex.Message, "dsh web",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                _autoStartItem.Checked = AutoStart.IsEnabled();
                AttachSupervisor();
                _showBallItem.Checked = _config.ShowBall;
                if (_config.ShowBall) CreateBall();
                _supervisor.Start();
                OnQuotaTick(null, EventArgs.Empty);
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            _exiting = true;
            BalanceService.Refreshed -= OnBalanceRefreshed;
            CodexQuotaService.Refreshed -= OnCodexRefreshed;
            ArkPlanService.Refreshed -= OnArkRefreshed;

            if (_scanTimer != null)
            {
                _scanTimer.Stop();
                _scanTimer.Dispose();
                _scanTimer = null;
            }

            if (_alarmTimer != null)
            {
                _alarmTimer.Stop();
                _alarmTimer.Dispose();
                _alarmTimer = null;
            }

            if (_alarmPopup != null)
            {
                _alarmPopup.ClosePopup();
                _alarmPopup.Dispose();
                _alarmPopup = null;
            }

            if (_quotaTimer != null)
            {
                _quotaTimer.Stop();
                _quotaTimer.Dispose();
                _quotaTimer = null;
            }

            if (_duplicateWait != null)
            {
                try { _duplicateWait.Unregister(null); } catch { }
                _duplicateWait = null;
            }

            if (_duplicateEvent != null)
            {
                try { _duplicateEvent.Close(); } catch { }
                _duplicateEvent = null;
            }

            _tray.Visible = false;
            DestroyBall();
            _supervisor.Dispose();
            _tray.Dispose();
            if (_currentIcon != null) _currentIcon.Dispose();
            ExitThread();
        }
    }
}
