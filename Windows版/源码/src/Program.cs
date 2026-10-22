using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
            string dumpTheme = null, dumpUi = null, dumpList = null;
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
                //   --theme <auto|light|dark>  指定主题（不读设置文件，自动化验证用）
                //   --no-sys-theme             假装系统不支持深色，用来看「跟随系统」的灰态
                //   --dump-theme <文件>        把主题状态写到文件后退出（自动化断言用）
                else if (args[i] == "--theme" && i + 1 < args.Length) SetStartupTheme(args[i + 1]);
                else if (args[i] == "--no-sys-theme") Theme.ForceNoSystemSupport = true;
                else if (args[i] == "--dump-theme" && i + 1 < args.Length) dumpTheme = args[i + 1];
                //   --show-theme-menu          启动就把「外观」菜单弹出来（自动化看灰态用）
                else if (args[i] == "--show-theme-menu") MainForm.AutoShowThemeMenu = true;
                //   --settings                 启动就停在「设置」页（自动化截图用）
                else if (args[i] == "--settings") MainForm.AutoShowSettings = true;
                //   --dump-ui <文件>           把控件树的真实颜色写出来（自动化断言主题有没有真的生效）
                else if (args[i] == "--dump-ui" && i + 1 < args.Length) dumpUi = args[i + 1];
                //   --dump-list <文件>         把列表项的图标框/文字框矩形写出来（大图标布局排查用）
                else if (args[i] == "--dump-list" && i + 1 < args.Length) dumpList = args[i + 1];
            }

            if (dumpTheme != null)      // 自动化验证：把主题状态写出来就退出，不弹窗
            {
                DumpThemeState(dumpTheme);
                return;
            }

            if (dumpUi != null)         // 同上：把每个控件的实际配色写出来
            {
                // 得真的 Show 一次：只 CreateControl 的话 Dock 布局和 Resize 事件都不完整，
                // dump 出来的 size/pos 是假的（曾据此误判"状态栏不见了"）
                MainForm f = new MainForm(autoOpen, gotoExport, autoOut, autoMulti, autoView, autoDir);
                f.Show();
                for (int k = 0; k < 8; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(80); }
                DumpUi(f, dumpUi, 0);
                return;
            }

            if (dumpList != null)       // 列表矩形：得让窗口真的显示一次，系统才会把各项布局算出来
            {
                MainForm f = new MainForm(autoOpen, gotoExport, autoOut, autoMulti, autoView, autoDir);
                f.Show();
                for (int k = 0; k < 12; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(120); }
                f.DumpList(dumpList);
                return;
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



        /// <summary>把一个控件的关键外观属性写成一行（主题自动化断言用）</summary>
        private static string Describe(Control c)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(c.GetType().Name);
            string tag = c.Text;
            if (tag != null) { tag = tag.Replace("\r", " ").Replace("\n", " ").Trim(); }
            if (!string.IsNullOrEmpty(tag) && tag.Length > 18) tag = tag.Substring(0, 18);
            if (!string.IsNullOrEmpty(tag)) sb.Append("(" + tag + ")");
            sb.Append(" pos=" + c.Left + "," + c.Top + " size=" + c.Width + "x" + c.Height);
            if (c.Dock != DockStyle.None) sb.Append(" dock=" + c.Dock);
            if (!c.Visible) sb.Append(" hidden");
            sb.Append(" back=" + Hex(c.BackColor) + " fore=" + Hex(c.ForeColor));
            if (c is Button) sb.Append(" flat=" + ((Button)c).FlatStyle);
            if (c is ListView) sb.Append(" ownerDraw=" + ((ListView)c).OwnerDraw + " view=" + ((ListView)c).View);
            if (c is TreeView) sb.Append(" line=" + Hex(((TreeView)c).LineColor));
            if (c is ToolStrip) sb.Append(" render=" + ((ToolStrip)c).RenderMode);
            if (!c.Enabled) sb.Append(" DISABLED");
            return sb.ToString();
        }

        private static string Hex(Color c)
        {
            // Color.Transparent 的 R/G/B 也是 FF/FF/FF，只取 RGB 会把它显示成 FFFFFF，
            // 曾经让人误以为"深色下这片还是白底"；透明就明确写"透明"
            if (c.A == 0) return "透明";
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private static void DumpUi(Control root, string path, int depth)
        {
            StringBuilder sb = new StringBuilder();
            DumpUiInto(root, sb, depth);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void DumpUiInto(Control c, StringBuilder sb, int depth)
        {
            sb.Append(new string(' ', depth * 2));
            sb.AppendLine(Describe(c));
            if (c is ToolStrip)
            {
                ToolStrip ts = (ToolStrip)c;
                for (int i = 0; i < ts.Items.Count; i++)
                {
                    ToolStripItem it = ts.Items[i];
                    sb.Append(new string(' ', (depth + 1) * 2));
                    sb.Append("· " + it.GetType().Name + "(" + it.Text + ") enabled=" + it.Enabled
                        + " fore=" + Hex(it.ForeColor) + " check=" + (it is ToolStripMenuItem ? ((ToolStripMenuItem)it).Checked.ToString() : "-") + "\r\n");
                    // 下拉菜单里的子项（「外观」三态就在这儿）也要列出来 —— 灰态就靠 enabled=False 看
                    ToolStripDropDownItem ddi = it as ToolStripDropDownItem;
                    if (ddi != null)
                    {
                        for (int k = 0; k < ddi.DropDownItems.Count; k++)
                        {
                            ToolStripItem sub = ddi.DropDownItems[k];
                            sb.Append(new string(' ', (depth + 2) * 2));
                            sb.Append("- " + sub.GetType().Name + "(" + sub.Text + ") enabled=" + sub.Enabled
                                + " check=" + (sub is ToolStripMenuItem ? ((ToolStripMenuItem)sub).Checked.ToString() : "-")
                                + " tip=" + (sub.ToolTipText == null ? "" : sub.ToolTipText) + "\r\n");
                        }
                    }
                }
            }
            for (int i = 0; i < c.Controls.Count; i++) DumpUiInto(c.Controls[i], sb, depth + 1);
        }

        private static void SetStartupTheme(string v)
        {
            Theme.HasStartupOverride = true;
            if (v == null) return;
            v = v.Trim().ToLowerInvariant();
            if (v == "light") Theme.Current = Theme.Mode.Light;
            else if (v == "dark") Theme.Current = Theme.Mode.Dark;
            else Theme.Current = Theme.Mode.Auto;
        }

        /// <summary>把主题相关的状态写成 key=value（给验收脚本读，不依赖截图）</summary>
        private static void DumpThemeState(string path)
        {
            try
            {
                Theme.Load();
                Theme.Refresh();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("mode=" + Theme.Describe(Theme.Current));
                sb.AppendLine("effective=" + (Theme.IsLight ? "light" : "dark"));
                sb.AppendLine("sysSupport=" + (Theme.SystemSupportsTheme ? "yes" : "no"));
                sb.AppendLine("sysPrefersLight=" + (Theme.ReadSystemPrefersLight() ? "yes" : "no"));
                sb.AppendLine("autoEnabled=" + (Theme.SystemSupportsTheme ? "yes" : "no"));
                sb.AppendLine("autoTooltip=" + (Theme.SystemSupportsTheme ? "跟着 Windows 的「应用模式」走" : "当前系统没有「深色模式」这个设置（Windows 10 1809 以上才有），所以这一档用不了，已按浅色显示；下面「浅色 / 深色」两档随时可用"));
                sb.AppendLine("bg=" + Theme.Bg.R.ToString("X2") + Theme.Bg.G.ToString("X2") + Theme.Bg.B.ToString("X2"));
                sb.AppendLine("panel=" + Theme.Panel.R.ToString("X2") + Theme.Panel.G.ToString("X2") + Theme.Panel.B.ToString("X2"));
                sb.AppendLine("text=" + Theme.Text.R.ToString("X2") + Theme.Text.G.ToString("X2") + Theme.Text.B.ToString("X2"));
                sb.AppendLine("buttonStyle=" + (Theme.IsLight ? "System" : "Flat"));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(path, "error=" + ex.Message); } catch { }
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
