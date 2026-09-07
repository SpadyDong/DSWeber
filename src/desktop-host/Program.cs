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

                var config = AppConfig.Load();
                var errors = config == null ? null : config.Validate();

                if (config == null || errors.Count > 0)
                {
                    if (errors != null && errors.Count > 0)
                    {
                        MessageBox.Show("配置需要补全：\r\n\r\n" + string.Join("\r\n", errors.ToArray()),
                            "dsh 托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    using (var form = new SettingsForm(config))
                    {
                        if (form.ShowDialog() != DialogResult.OK) return;
                        config = form.Result;
                        config.Save();
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
