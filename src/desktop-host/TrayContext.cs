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
        private DateTime _lastAlarmFired;

        public TrayContext(AppConfig config)
        {
            _config = config;

            _statusItem = new ToolStripMenuItem("状态", null, OnShowStatus);
            _openItem = new ToolStripMenuItem("打开 Web 界面", null, OnOpenWeb);
            _startStopItem = new ToolStripMenuItem("停止", null, OnStartStop);
            _autoStartItem = new ToolStripMenuItem("开机启动", null, OnToggleAutoStart) { CheckOnClick = false };
            _showBallItem = new ToolStripMenuItem("桌面悬浮球", null, OnToggleBall) { CheckOnClick = false };
            _alarmItem = new ToolStripMenuItem("闹钟", null, OnAlarmSettings);
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

            AutoStart.Apply(_config.AutoStart);
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _showBallItem.Checked = _config.ShowBall;

            _lastNotifiedState = ServiceState.Stopped;
            SetupDuplicateLaunchListener();
            SetupConflictScanner();
            SetupAlarmTimer();

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
            _alarmTimer = new Timer();
            _alarmTimer.Interval = 30000;
            _alarmTimer.Tick += OnAlarmTick;
            _alarmTimer.Start();
        }

        private void OnAlarmTick(object sender, EventArgs e)
        {
            if (_exiting) return;
            if (_config.Alarms == null || _config.Alarms.Count == 0) return;

            DateTime now = DateTime.Now;
            var fired = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            if (fired == _lastAlarmFired) return;
            _lastAlarmFired = fired;

            string nowKey = now.ToString("HH:mm");
            var messages = new List<string>();
            foreach (var alarm in _config.Alarms)
            {
                if (alarm == null || !alarm.Enabled) continue;
                if (string.IsNullOrWhiteSpace(alarm.Time)) continue;
                if (alarm.Time.Trim() != nowKey) continue;
                if (string.IsNullOrWhiteSpace(alarm.Message)) continue;

                messages.Add(alarm.Message);
            }

            if (messages.Count == 0) return;

            ShowAlarmPopup(nowKey, string.Join("\r\n\r\n", messages.ToArray()));
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
            var probe = new Rectangle(p.X, p.Y, FloatingBall.BallSize, FloatingBall.BallSize);
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
                var area = Screen.PrimaryScreen.WorkingArea;
                return new Point(area.Right - FloatingBall.BallSize - 16,
                                 area.Bottom - FloatingBall.BallSize - 16);
            }

            return FloatingBall.ClampToWorkArea(p);
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

                _supervisor.Stop();
                DestroyBall();
                _config = form.Result;
                _config.Save();
                BalanceService.Invalidate();
                AutoStart.Apply(_config.AutoStart);
                _autoStartItem.Checked = AutoStart.IsEnabled();
                AttachSupervisor();
                _showBallItem.Checked = _config.ShowBall;
                if (_config.ShowBall) CreateBall();
                _supervisor.Start();
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            _exiting = true;

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
