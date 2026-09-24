using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class AlarmDialog : Form
    {
        private readonly CheckedListBox _list = new CheckedListBox();
        private readonly List<AlarmItem> _working = new List<AlarmItem>();

        public List<AlarmItem> Result { get; private set; }

        public AlarmDialog(List<AlarmItem> alarms)
        {
            Text = "闹钟设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 300);
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = IconFactory.CreateGlyphIcon(32, Color.FromArgb(77, 107, 254));

            _list.Left = 14;
            _list.Top = 14;
            _list.Width = 320;
            _list.Height = 200;
            _list.IntegralHeight = false;
            Controls.Add(_list);

            int bx = 348;
            int by = 14;
            AddButton("添加", bx, by, OnAdd);
            AddButton("编辑", bx, by + 34, OnEdit);
            AddButton("删除", bx, by + 68, OnDelete);

            var tip = new Label
            {
                Left = 14,
                Top = 224,
                Width = 430,
                ForeColor = Color.DimGray,
                Text = "勾选表示启用该闹钟；每天到时间后弹出提醒气泡，点击气泡关闭。"
            };
            Controls.Add(tip);

            var ok = new Button { Text = "保存", Left = 264, Top = 260, Width = 90 };
            var cancel = new Button { Text = "取消", Left = 362, Top = 260, Width = 86 };
            ok.Click += OnOk;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            if (alarms != null)
            {
                foreach (var a in alarms)
                {
                    if (a == null) continue;
                    _working.Add(new AlarmItem { Enabled = a.Enabled, Time = a.Time, Message = a.Message });
                }
            }
            RefreshList();
        }

        private void AddButton(string text, int x, int y, EventHandler handler)
        {
            var btn = new Button { Text = text, Left = x, Top = y, Width = 96 };
            btn.Click += handler;
            Controls.Add(btn);
        }

        private void RefreshList()
        {
            _list.Items.Clear();
            foreach (var a in _working)
            {
                _list.Items.Add(FormatItem(a), a.Enabled);
            }
        }

        private static string FormatItem(AlarmItem a)
        {
            string time = string.IsNullOrWhiteSpace(a.Time) ? "--:--" : a.Time.Trim();
            string msg = a.Message == null ? "" : a.Message.Replace("\r", " ").Replace("\n", " ");
            if (msg.Length > 24) msg = msg.Substring(0, 24) + "…";
            return time + "    " + msg;
        }

        private void OnAdd(object sender, EventArgs e)
        {
            using (var edit = new AlarmEditDialog("", ""))
            {
                if (edit.ShowDialog(this) != DialogResult.OK) return;
                _working.Add(new AlarmItem { Enabled = true, Time = edit.Time, Message = edit.Message });
                RefreshList();
                _list.SelectedIndex = _working.Count - 1;
            }
        }

        private void OnEdit(object sender, EventArgs e)
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _working.Count) return;

            var a = _working[idx];
            using (var edit = new AlarmEditDialog(a.Time, a.Message))
            {
                if (edit.ShowDialog(this) != DialogResult.OK) return;
                a.Time = edit.Time;
                a.Message = edit.Message;
                RefreshList();
                _list.SelectedIndex = idx;
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _working.Count) return;
            _working.RemoveAt(idx);
            RefreshList();
        }

        private void OnOk(object sender, EventArgs e)
        {
            for (int i = 0; i < _working.Count && i < _list.Items.Count; i++)
            {
                _working[i].Enabled = _list.GetItemChecked(i);
            }

            for (int i = 0; i < _working.Count; i++)
            {
                var a = _working[i];
                if (!a.Enabled) continue;

                DateTime parsed;
                if (!DateTime.TryParseExact((a.Time ?? "").Trim(), "HH:mm",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    MessageBox.Show(this, "第 " + (i + 1) + " 个闹钟的时间格式不正确，请使用 HH:mm（例如 08:30）。",
                        "闹钟设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(a.Message))
                {
                    MessageBox.Show(this, "第 " + (i + 1) + " 个闹钟未填写提醒内容。",
                        "闹钟设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Result = _working;
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class AlarmEditDialog : Form
        {
            private readonly TextBox _time = new TextBox();
            private readonly TextBox _message = new TextBox();

            public string Time { get; private set; }
            public string Message { get; private set; }

            public AlarmEditDialog(string time, string message)
            {
                Text = "编辑闹钟";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(360, 190);
                Font = new Font("Microsoft YaHei UI", 9f);

                Controls.Add(new Label { Left = 16, Top = 20, Width = 80, Text = "提醒时间" });
                _time.Left = 100;
                _time.Top = 16;
                _time.Width = 90;
                _time.MaxLength = 5;
                _time.Text = time ?? "";
                Controls.Add(_time);
                Controls.Add(new Label
                {
                    Left = 200,
                    Top = 20,
                    Width = 150,
                    ForeColor = Color.DimGray,
                    Text = "HH:mm，例如 08:30"
                });

                Controls.Add(new Label { Left = 16, Top = 56, Width = 80, Text = "提醒内容" });
                _message.Left = 100;
                _message.Top = 52;
                _message.Width = 244;
                _message.Height = 66;
                _message.Multiline = true;
                _message.ScrollBars = ScrollBars.Vertical;
                _message.Text = message ?? "";
                Controls.Add(_message);

                var ok = new Button { Text = "确定", Left = 166, Top = 150, Width = 90 };
                var cancel = new Button { Text = "取消", Left = 262, Top = 150, Width = 82 };
                ok.Click += OnOk;
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(ok);
                Controls.Add(cancel);
                AcceptButton = ok;
                CancelButton = cancel;
            }

            private void OnOk(object sender, EventArgs e)
            {
                string time = _time.Text.Trim();
                DateTime parsed;
                if (!DateTime.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out parsed))
                {
                    MessageBox.Show(this, "提醒时间格式不正确，请使用 HH:mm（例如 08:30）。",
                        "闹钟设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(_message.Text))
                {
                    MessageBox.Show(this, "请填写提醒内容。",
                        "闹钟设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Time = time;
                Message = _message.Text;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}