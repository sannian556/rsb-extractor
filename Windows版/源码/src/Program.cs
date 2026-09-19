using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ApkSmfExtractor
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // 出异常要留下证据：写 %TEMP%\APK提取工具-crash.log，同时弹个框告诉用户
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { ReportCrash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException +=
                delegate(object s, UnhandledExceptionEventArgs e) { ReportCrash(e.ExceptionObject as Exception); };

            // XP 上没有 SetProcessDPIAware，调用会抛 EntryPointNotFoundException，忽略即可
            try { SetProcessDPIAware(); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 可选启动参数（正常双击用不到，只为自动化 / 批量验证留的口子）：
            //   --open <apk>            启动后直接导入并停在「文件浏览」界面
            //   --dir <内部路径>         配合 --open：直接进到 apk 里的某个目录（如 assets）
            //   --view <detail|large>   浏览界面的初始视图
            //   --open-export <apk>     导入、自动选中 assets 下的 .smf 并进「导出」界面
            //   --out <文件夹>           预填导出目录
            //   --multi <N>             配合 --open-export：一次选中前 N 个 .smf（默认 1）
            string autoOpen = null, autoOut = null, autoView = null, autoDir = null;
            int autoMulti = 0;
            bool gotoExport = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--open" && i + 1 < args.Length) autoOpen = args[i + 1];
                else if (args[i] == "--open-export" && i + 1 < args.Length)
                {
                    autoOpen = args[i + 1];
                    gotoExport = true;
                }
                else if (args[i] == "--out" && i + 1 < args.Length) autoOut = args[i + 1];
                else if (args[i] == "--multi" && i + 1 < args.Length) int.TryParse(args[i + 1], out autoMulti);
                else if (args[i] == "--view" && i + 1 < args.Length) autoView = args[i + 1];
                else if (args[i] == "--dir" && i + 1 < args.Length) autoDir = args[i + 1];
            }

            try
            {
                Application.Run(new MainForm(autoOpen, gotoExport, autoOut, autoMulti, autoView, autoDir));
            }
            catch (Exception ex)
            {
                ReportCrash(ex);
            }
        }

        public static string CrashLogPath
        {
            get { return Path.Combine(Path.GetTempPath(), "APK提取工具-crash.log"); }
        }

        private static void ReportCrash(Exception ex)
        {
            try
            {
                File.AppendAllText(CrashLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                    (ex == null ? "(无异常对象)" : ex.ToString()) + "\r\n\r\n");
            }
            catch { }
            try
            {
                MessageBox.Show("程序出错了：\n\n" + (ex == null ? "未知错误" : ex.Message) +
                    "\n\n详细信息已写入：\n" + CrashLogPath,
                    "APK 提取工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
