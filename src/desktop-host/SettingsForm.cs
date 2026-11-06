using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class SettingsForm : Form
    {
        private readonly TextBox _apiKey = new TextBox();
        private readonly CheckBox _autoStart = new CheckBox();
        private readonly CheckBox _showCodex = new CheckBox();

        public AppConfig Result { get; private set; }

        private readonly AppConfig _existing;

        public SettingsForm(AppConfig existing)
        {
            _existing = existing;
            Text = "dsh 托盘 - 配置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 236);
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = IconFactory.CreateGlyphIcon(32, Color.FromArgb(77, 107, 254));

            int y = 18;
            Controls.Add(new Label { Left = 16, Top = y + 4, Width = 110, Text = "DeepSeek API Key" });
            _apiKey.Left = 130;
            _apiKey.Top = y;
            _apiKey.Width = 414;
            _apiKey.UseSystemPasswordChar = true;
            Controls.Add(_apiKey);
            y += 36;

            AddCheck(_autoStart, "开机自动启动本程序", ref y);
            AddCheck(_showCodex, "在余额气泡中显示 Codex 配额（5 小时 / 7 天剩余）", ref y);

            var tip = new Label
            {
                Left = 16,
                Top = y + 6,
                Width = 528,
                Height = 62,
                ForeColor = Color.DimGray,
                Text = "API Key 可以留空：留空时会读取 %USERPROFILE%\\.dsh\\.credentials.yaml 中的 DEEPSEEK_API_KEY。\r\nKey 保存时用 Windows DPAPI（当前用户）加密，不会明文写入配置文件。\r\nCodex 配额需本机已安装 codex CLI，路径自动探测，无需填写。"
            };
            Controls.Add(tip);

            var ok = new Button { Text = "保存", Left = 350, Top = ClientSize.Height - 34, Width = 100 };
            var cancel = new Button { Text = "取消", Left = 458, Top = ClientSize.Height - 34, Width = 86 };
            ok.Click += OnOk;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            if (existing != null)
            {
                _autoStart.Checked = existing.AutoStart;
                _showCodex.Checked = existing.ShowCodexQuota;
                _apiKey.Text = existing.ApiKey ?? "";
            }
            else
            {
                _autoStart.Checked = true;
                _showCodex.Checked = true;
                _apiKey.Text = "";
            }
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

        private void OnOk(object sender, EventArgs e)
        {
            Result = new AppConfig
            {
                AutoStart = _autoStart.Checked,
                ShowBall = _existing == null || _existing.ShowBall,
                BallX = _existing != null ? _existing.BallX : 0,
                BallY = _existing != null ? _existing.BallY : 0,
                ShowCodexQuota = _showCodex.Checked,
                ApiKey = _apiKey.Text.Trim(),
                Alarms = CloneAlarms(_existing != null ? _existing.Alarms : null)
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private static List<AlarmItem> CloneAlarms(List<AlarmItem> source)
        {
            var list = new List<AlarmItem>();
            if (source == null) return list;
            foreach (var a in source)
            {
                if (a == null) continue;
                list.Add(new AlarmItem { Enabled = a.Enabled, Time = a.Time, Message = a.Message });
            }
            return list;
        }
    }
}
