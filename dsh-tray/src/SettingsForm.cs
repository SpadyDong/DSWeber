using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class SettingsForm : Form
    {
        private readonly TextBox _nodePath = new TextBox();
        private readonly TextBox _dshScript = new TextBox();
        private readonly TextBox _workDir = new TextBox();
        private readonly TextBox _host = new TextBox();
        private readonly TextBox _port = new TextBox();
        private readonly TextBox _apiKey = new TextBox();
        private readonly CheckBox _noOpen = new CheckBox();
        private readonly CheckBox _autoRestart = new CheckBox();
        private readonly CheckBox _autoStart = new CheckBox();

        public AppConfig Result { get; private set; }

        private readonly AppConfig _existing;

        public SettingsForm(AppConfig existing)
        {
            _existing = existing;
            Text = "dsh 托盘 - 配置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 386);
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = IconFactory.CreateGlyphIcon(32, Color.FromArgb(77, 107, 254));

            int y = 14;
            AddRow("node.exe 路径", _nodePath, ref y, BrowseFile);
            AddRow("dsh bin.js 路径", _dshScript, ref y, BrowseFile);
            AddRow("工作目录", _workDir, ref y, BrowseFolder);
            AddRow("监听 Host", _host, ref y, null);
            AddRow("监听端口", _port, ref y, null);

            _apiKey.Left = 130;
            _apiKey.Top = y;
            _apiKey.Width = 340;
            Controls.Add(new Label { Left = 16, Top = y + 4, Width = 110, Text = "DeepSeek API Key" });
            Controls.Add(_apiKey);
            y += 30;

            y += 6;
            AddCheck(_noOpen, "启动时不自动打开浏览器（--no-open）", ref y);
            AddCheck(_autoRestart, "进程异常退出后自动重启（最多 5 次）", ref y);
            AddCheck(_autoStart, "开机自动启动本托盘程序", ref y);

            var tip = new Label
            {
                Left = 16,
                Top = y + 4,
                Width = 528,
                Height = 46,
                ForeColor = Color.DimGray,
                Text = "所有字段均需显式填写，不提供隐含默认值。Host 填 127.0.0.1 只允许本机访问，填 0.0.0.0 允许局域网访问。\r\nAPI Key 留空时，点击悬浮球查余额将自动读取 %USERPROFILE%\\.dsh\\.credentials.yaml 中的 DEEPSEEK_API_KEY。"
            };
            Controls.Add(tip);

            var ok = new Button { Text = "保存并启动", Left = 350, Top = ClientSize.Height - 40, Width = 100 };
            var cancel = new Button { Text = "取消", Left = 458, Top = ClientSize.Height - 40, Width = 86 };
            ok.Click += OnOk;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            if (existing != null)
            {
                _nodePath.Text = existing.NodePath ?? "";
                _dshScript.Text = existing.DshScript ?? "";
                _workDir.Text = existing.WorkingDirectory ?? "";
                _host.Text = existing.Host ?? "";
                _port.Text = existing.Port.ToString();
                _noOpen.Checked = existing.NoOpen;
                _autoRestart.Checked = existing.AutoRestart;
                _autoStart.Checked = existing.AutoStart;
                _apiKey.Text = existing.ApiKey ?? "";
            }
            else
            {
                _nodePath.Text = Probe.FindNode() ?? "";
                _dshScript.Text = Probe.FindDshScript() ?? "";
            }
        }

        private void AddRow(string label, TextBox box, ref int y, Action<TextBox> browse)
        {
            Controls.Add(new Label { Left = 16, Top = y + 4, Width = 110, Text = label });
            box.Left = 130;
            box.Top = y;
            box.Width = browse != null ? 340 : 200;
            Controls.Add(box);

            if (browse != null)
            {
                var btn = new Button { Text = "浏览", Left = 478, Top = y - 1, Width = 66 };
                var target = box;
                var action = browse;
                btn.Click += delegate { action(target); };
                Controls.Add(btn);
            }

            y += 30;
        }

        private void AddCheck(CheckBox box, string text, ref int y)
        {
            box.Left = 130;
            box.Top = y;
            box.Width = 414;
            box.Text = text;
            Controls.Add(box);
            y += 26;
        }

        private static void BrowseFile(TextBox box)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "可执行文件或脚本|*.exe;*.js|所有文件|*.*";
                if (!string.IsNullOrWhiteSpace(box.Text) && File.Exists(box.Text))
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(box.Text);
                    dlg.FileName = Path.GetFileName(box.Text);
                }
                if (dlg.ShowDialog() == DialogResult.OK) box.Text = dlg.FileName;
            }
        }

        private static void BrowseFolder(TextBox box)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 dsh web 的工作目录";
                if (!string.IsNullOrWhiteSpace(box.Text) && Directory.Exists(box.Text))
                    dlg.SelectedPath = box.Text;
                if (dlg.ShowDialog() == DialogResult.OK) box.Text = dlg.SelectedPath;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            var cfg = new AppConfig
            {
                NodePath = _nodePath.Text.Trim(),
                DshScript = _dshScript.Text.Trim(),
                WorkingDirectory = _workDir.Text.Trim(),
                Host = _host.Text.Trim(),
                NoOpen = _noOpen.Checked,
                AutoRestart = _autoRestart.Checked,
                AutoStart = _autoStart.Checked,
                ShowBall = _existing != null && _existing.ShowBall,
                BallX = _existing != null ? _existing.BallX : 0,
                BallY = _existing != null ? _existing.BallY : 0,
                ApiKey = _apiKey.Text.Trim()
            };

            var errors = new List<string>();

            int port;
            if (string.IsNullOrWhiteSpace(_port.Text))
                errors.Add("端口未填写");
            else if (!int.TryParse(_port.Text.Trim(), out port))
                errors.Add("端口必须是整数");
            else
                cfg.Port = port;

            if (string.IsNullOrWhiteSpace(cfg.Host))
                errors.Add("Host 未填写");

            errors.AddRange(cfg.Validate());

            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join("\r\n", errors.ToArray()),
                    "配置不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = cfg;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
