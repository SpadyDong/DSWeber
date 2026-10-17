using System;
using System.Threading;
using System.Windows.Forms;

namespace DshTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 必须在创建任何窗口之前启用，否则高缩放显示器上位图会被拉伸而模糊
            Dpi.Enable();

            bool created;
            using (var mutex = new Mutex(true, "Local\\DshTray_SingleInstance", out created))
            {
                if (!created)
                {
                    SignalRunningInstance();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppConfig config = null;
                try
                {
                    config = AppConfig.Load();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("配置文件无法读取：" + ex.Message + "\r\n\r\n请在设置窗口重新保存配置。原文件会保留备份。",
                        "dsh 托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (System.IO.File.Exists(AppConfig.ConfigPath))
                    {
                        try
                        {
                            System.IO.File.Copy(AppConfig.ConfigPath,
                                AppConfig.ConfigPath + ".invalid-" + Guid.NewGuid().ToString("N"), false);
                        }
                        catch (Exception backupError)
                        {
                            MessageBox.Show("无法备份原配置，程序将退出以避免覆盖它：" + backupError.Message,
                                "dsh 托盘", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }
                System.Collections.Generic.List<string> errors = null;
                if (config != null)
                {
                    config.ApplyDefaults();
                    errors = config.Validate();
                }

                if (config == null || errors.Count > 0)
                {
                    if (errors != null && errors.Count > 0)
                    {
                        MessageBox.Show("以下配置缺失或无效，请确认（其余字段已自动填默认值）：\r\n\r\n"
                            + string.Join("\r\n", errors.ToArray()),
                            "dsh 托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    using (var form = new SettingsForm(config))
                    {
                        if (form.ShowDialog() != DialogResult.OK) return;
                        config = form.Result;
                        try { config.Save(); }
                        catch (Exception ex)
                        {
                            MessageBox.Show("保存配置失败：" + ex.Message, "dsh 托盘",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }

                int owner = PortGuard.GetListenerPid(config.Port);
                if (owner != 0)
                {
                    MessageBox.Show(
                        "端口 " + config.Port + " 已被其他程序占用，dsh 托盘不会启动。\r\n\r\n"
                        + "占用者：" + PortGuard.DescribeProcess(owner) + "\r\n\r\n"
                        + "如果你在终端里手动运行了 dsh web，请先关闭它，再重新启动本程序。",
                        "dsh 托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Application.Run(new TrayContext(config));
            }
        }

        private static void SignalRunningInstance()
        {
            EventWaitHandle handle = null;
            try
            {
                handle = EventWaitHandle.OpenExisting(TrayContext.DuplicateLaunchEventName);
                handle.Set();
            }
            catch
            {
            }
            finally
            {
                if (handle != null) { try { handle.Close(); } catch { } }
            }
        }
    }
}
