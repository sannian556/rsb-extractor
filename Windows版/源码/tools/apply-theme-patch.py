# -*- coding: utf-8 -*-
import io, os, sys

ROOT = r'D:\AI deekseek\APK提取工具工程\src'
MF = os.path.join(ROOT, 'MainForm.cs')
PG = os.path.join(ROOT, 'Program.cs')

def read(p):
    return io.open(p, 'r', encoding='utf-8').read().replace('\r\n', '\n')

def write(p, t):
    io.open(p, 'w', encoding='utf-8', newline='\r\n').write(t)

fails = []
def rep(t, old, new, times, label):
    c = t.count(old)
    if c != times:
        fails.append('%s: 期望 %d 处，实际 %d 处' % (label, times, c))
        return t
    return t.replace(old, new)

mf = read(MF)

# 1) 字段
mf = rep(mf, '        private Label _lblStatus;\n',
         '        private Label _lblStatus;\n'
         '        private ToolStripDropDownButton _btnTheme;\n'
         '        private ToolStripMenuItem _miThemeAuto, _miThemeLight, _miThemeDark;\n',
         1, 'R1 字段')

# 2) 构造：读设置 + 探测 + 初始底色
mf = rep(mf, '            BackColor = Color.White;\n',
         '            // 主题：先读设置 + 探测系统（决定「跟随系统」能不能用），界面建好后再统一刷一遍\n'
         '            Theme.Load();\n'
         '            Theme.Refresh();\n'
         '            BackColor = Theme.Bg;\n',
         1, 'R2 构造底色')

# 3) 构造：建完界面刷主题
mf = rep(mf, '            BuildExportPanel();\n            ShowImport();\n',
         '            BuildExportPanel();\n            ApplyTheme();\n            ShowImport();\n',
         1, 'R3 建完刷主题')

# 4) 各面板底色（4 处：import 初值 + 拖放离开/放下 + browse + export）
mf = rep(mf, '_pImport.BackColor = Color.White;', '_pImport.BackColor = Theme.Bg;', 3, 'R4 import 底色')
mf = rep(mf, 'if (ok) _pImport.BackColor = Color.FromArgb(236, 244, 255);',
         'if (ok) _pImport.BackColor = Theme.DropHover;', 1, 'R5 拖入高亮')
mf = rep(mf, '_pBrowse.BackColor = Color.White;', '_pBrowse.BackColor = Theme.Bg;', 1, 'R6 browse 底色')
mf = rep(mf, '_pExport.BackColor = Color.White;', '_pExport.BackColor = Theme.Bg;', 1, 'R7 export 底色')

# 5) 底部状态条 + 分隔线
mf = rep(mf, 'bottom.BackColor = Color.FromArgb(247, 248, 250);', 'bottom.BackColor = Theme.PanelAlt;', 1, 'R8 状态条')
mf = rep(mf, 'new Pen(Color.FromArgb(224, 227, 232))', 'new Pen(Theme.Border)', 1, 'R9 分隔线')

# 6) 文字颜色
mf = rep(mf, '_lblCount.ForeColor = Color.FromArgb(100, 106, 116);', '_lblCount.ForeColor = Theme.SubText;', 1, 'R10 计数')
mf = rep(mf, '_lblSelInfo.ForeColor = Color.FromArgb(40, 90, 175);', '_lblSelInfo.ForeColor = Theme.Link;', 1, 'R11 选中信息')
mf = rep(mf, '_lblStatus.ForeColor = Color.FromArgb(90, 96, 106);', '_lblStatus.ForeColor = Theme.SubText;', 1, 'R12 状态')
mf = rep(mf, 'if (!c.Entry.IsSmf) it.ForeColor = Color.FromArgb(170, 175, 182);',
         'if (!c.Entry.IsSmf) it.ForeColor = Theme.Dim;', 1, 'R13 非 smf 淡显')
mf = rep(mf, 'sep.ForeColor = Color.FromArgb(150, 156, 166);', 'sep.ForeColor = Theme.Dim;', 1, 'R14 面包屑分隔')
mf = rep(mf, 'b.ForeColor = last ? Color.FromArgb(25, 30, 38) : Color.FromArgb(50, 100, 185);',
         'b.ForeColor = last ? Theme.Text : Theme.Link;', 1, 'R15 面包屑颜色')

# 7) 导航栏底色
mf = rep(mf, '_navBar.BackColor = Color.FromArgb(250, 251, 252);', '_navBar.BackColor = Theme.Panel;', 1, 'R16 导航栏底色')

# 8) 视图按钮后面挂「外观」三态菜单
mf = rep(mf, '            _navBar.Items.Add(_viewDetail);\n            _navBar.Items.Add(_viewLarge);\n',
         '            _navBar.Items.Add(_viewDetail);\n            _navBar.Items.Add(_viewLarge);\n'
         '\n'
         '            // 外观三态：跟随系统 / 浅色 / 深色。\n'
         '            // 系统没有深色模式那一档（Win10 1809 以下）时，「跟随系统」变灰 + Tooltip 说明原因。\n'
         '            _btnTheme = new ToolStripDropDownButton();\n'
         '            _btnTheme.DisplayStyle = ToolStripItemDisplayStyle.Text;\n'
         '            _btnTheme.Alignment = ToolStripItemAlignment.Right;\n'
         '            _btnTheme.Margin = new Padding(S(1), S(2), S(1), S(2));\n'
         '            _btnTheme.Padding = new Padding(S(5), S(0), S(5), 0);\n'
         '            _btnTheme.AutoToolTip = false;\n'
         '            _miThemeAuto = new ToolStripMenuItem("跟随系统");\n'
         '            _miThemeLight = new ToolStripMenuItem("浅色");\n'
         '            _miThemeDark = new ToolStripMenuItem("深色");\n'
         '            _miThemeAuto.Click += delegate { SetThemeMode(Theme.Mode.Auto); };\n'
         '            _miThemeLight.Click += delegate { SetThemeMode(Theme.Mode.Light); };\n'
         '            _miThemeDark.Click += delegate { SetThemeMode(Theme.Mode.Dark); };\n'
         '            _btnTheme.DropDownItems.Add(_miThemeAuto);\n'
         '            _btnTheme.DropDownItems.Add(new ToolStripSeparator());\n'
         '            _btnTheme.DropDownItems.Add(_miThemeLight);\n'
         '            _btnTheme.DropDownItems.Add(_miThemeDark);\n'
         '            _navBar.Items.Add(_btnTheme);\n',
         1, 'R17 外观菜单')

# 9) ListView 自绘接线
mf = rep(mf, '            _list.Resize += delegate { AdjustColumns(); };\n',
         '            _list.Resize += delegate { AdjustColumns(); };\n'
         '            // 深色下系统画的表头/行是白的，改成自绘；浅色模式仍走系统原生，保持原来的样子\n'
         '            _list.DrawColumnHeader += OnDrawColumnHeader;\n'
         '            _list.DrawItem += OnDrawListItem;\n'
         '            _list.DrawSubItem += OnDrawListSubItem;\n',
         1, 'R18 自绘接线')

# 10) 追加「主题」整段（放在 小工具 之前）
theme_block = u'''
        // ============================================================ 主题
        /// <summary>把当前调色板刷到所有控件上（切主题、系统换主题时都走这里）</summary>
        private void ApplyTheme()
        {
            BackColor = Theme.Bg;
            if (_pImport != null) _pImport.BackColor = Theme.Bg;
            if (_pBrowse != null) _pBrowse.BackColor = Theme.Bg;
            if (_pExport != null) _pExport.BackColor = Theme.Bg;

            // 深色下系统原生按钮是白的，改成自绘扁平；浅色下保持系统原生
            Button[] btns = new Button[] { _btnImport, _btnGoExport, _btnPickDir, _btnDoExport };
            for (int i = 0; i < btns.Length; i++) Theme.StyleButton(btns[i]);
            Theme.StyleTextBox(_txtApkPath);
            Theme.StyleTextBox(_txtOut);

            if (_lblCount != null) { _lblCount.BackColor = Color.Transparent; _lblCount.ForeColor = Theme.SubText; }
            if (_lblSelInfo != null) { _lblSelInfo.BackColor = Color.Transparent; _lblSelInfo.ForeColor = Theme.Link; }
            if (_lblStatus != null) { _lblStatus.BackColor = Color.Transparent; _lblStatus.ForeColor = Theme.SubText; }
            if (_lblOutCaption != null) { _lblOutCaption.BackColor = Color.Transparent; _lblOutCaption.ForeColor = Theme.Text; }

            if (_navBar != null)
            {
                _navBar.BackColor = Theme.Panel;
                if (Theme.IsLight) _navBar.RenderMode = ToolStripRenderMode.System;
                else
                {
                    _navBar.RenderMode = ToolStripRenderMode.Professional;
                    _navBar.Renderer = new DarkToolStripRenderer();
                }
            }

            if (_tree != null)
            {
                _tree.BackColor = Theme.Bg;
                _tree.ForeColor = Theme.Text;
                _tree.LineColor = Theme.Border;
                Theme.ApplyScrollTheme(_tree);
            }
            if (_list != null)
            {
                _list.BackColor = Theme.Bg;
                _list.ForeColor = Theme.Text;
                _list.OwnerDraw = !Theme.IsLight;      // 浅色交给系统画，深色才自绘
                Theme.ApplyScrollTheme(_list);
            }

            // 分栏条 / 底部状态条（这两个是局部变量，按 Dock 找回来）
            if (_pBrowse != null)
            {
                for (int i = 0; i < _pBrowse.Controls.Count; i++)
                {
                    Control c = _pBrowse.Controls[i];
                    if (c is SplitContainer) c.BackColor = Theme.Border;
                    else if (c.Dock == DockStyle.Bottom) c.BackColor = Theme.PanelAlt;
                }
            }

            UpdateThemeButton();
            Theme.ApplyTitleBar(Handle, Theme.IsLight);
            Invalidate(true);
        }

        /// <summary>「外观」菜单的文字 / 勾选 / 可用性</summary>
        private void UpdateThemeButton()
        {
            if (_btnTheme == null) return;
            _btnTheme.Text = "外观：" + Theme.Describe(Theme.Current);
            _miThemeAuto.Checked = (Theme.Current == Theme.Mode.Auto);
            _miThemeLight.Checked = (Theme.Current == Theme.Mode.Light);
            _miThemeDark.Checked = (Theme.Current == Theme.Mode.Dark);

            // ★ 方案 B：系统没有深色模式时，「跟随系统」这一档变灰不可点，并把原因写在提示里
            _miThemeAuto.Enabled = Theme.SystemSupportsTheme;
            if (Theme.SystemSupportsTheme)
            {
                _miThemeAuto.ToolTipText = "跟着 Windows 的「应用模式」走（当前系统："
                    + (Theme.ReadSystemPrefersLight() ? "浅色" : "深色") + "）——你在系统里改，这里会实时跟着变";
            }
            else
            {
                _miThemeAuto.ToolTipText = "当前系统没有「深色模式」这个设置（Windows 10 1809 以上才有），"
                    + "所以这一档用不了，已按浅色显示；下面「浅色 / 深色」两档随时可用";
            }
            _btnTheme.ToolTipText = Theme.CurrentDescription();
        }

        private void SetThemeMode(Theme.Mode m)
        {
            if (m == Theme.Mode.Auto && !Theme.SystemSupportsTheme) return;   // 灰的那一档，点了也当没点
            Theme.Current = m;
            Theme.Save();
            Theme.Refresh();
            ApplyTheme();
            if (_list != null) _list.Invalidate();
            if (_tree != null) _tree.Invalidate();
        }

        /// <summary>用户在 Windows 里换了「应用模式」—— WM_SETTINGCHANGE(ImmersiveColorSet) 时跟随模式实时生效</summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_SETTINGCHANGE = 0x001A;
            if (m.Msg == WM_SETTINGCHANGE && Theme.Current == Theme.Mode.Auto)
            {
                bool before = Theme.IsLight;
                Theme.Refresh();
                if (Theme.IsLight != before) ApplyTheme();
                else UpdateThemeButton();
            }
            base.WndProc(ref m);
        }

        // ---- ListView 自绘（只在深色下启用）----
        private Color ListItemColor(ListViewItem it)
        {
            Color c = it.ForeColor;
            if (c == Control.DefaultForeColor || c.IsEmpty) c = Theme.Text;
            if (!it.Enabled) c = Theme.Dim;
            return c;
        }

        private void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.PanelAlt))
                e.Graphics.FillRectangle(b, e.Bounds);
            using (Pen p = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + S(4), e.Bounds.Right - 1, e.Bounds.Bottom - S(4));
            }
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            flags |= (e.Header.TextAlign == HorizontalAlignment.Right) ? TextFormatFlags.Right : TextFormatFlags.Left;
            Rectangle r = e.Bounds;
            r.Inflate(-S(6), 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, _list.Font, r, Theme.SubText, flags);
        }

        private void OnDrawListItem(object sender, DrawListViewItemEventArgs e)
        {
            if (_list.View == View.Details)
            {
                // 先把整行铺满（最右侧空白区不属于任何列，不铺会留一条白边）
                Color back = e.Item.Selected ? Theme.SelBg
                           : (e.ItemIndex % 2 == 1 ? Theme.RowAlt : Theme.Bg);
                using (SolidBrush b = new SolidBrush(back))
                    e.Graphics.FillRectangle(b, e.Bounds);
                return;
            }

            // 大图标视图：图标居中、名字在下面
            using (SolidBrush b = new SolidBrush(e.Item.Selected ? Theme.SelBg : Theme.Bg))
                e.Graphics.FillRectangle(b, e.Bounds);

            Image img = null;
            if (e.Item.ImageList != null && !string.IsNullOrEmpty(e.Item.ImageKey))
                img = e.Item.ImageList.Images[e.Item.ImageKey];
            int top = e.Bounds.Top + S(8);
            if (img != null)
            {
                int x = e.Bounds.Left + (e.Bounds.Width - img.Width) / 2;
                e.Graphics.DrawImage(img, x, top, img.Width, img.Height);
                top += img.Height + S(6);
            }
            Rectangle tr = new Rectangle(e.Bounds.Left + S(4), top, e.Bounds.Width - S(8), e.Bounds.Bottom - top - S(2));
            TextRenderer.DrawText(e.Graphics, e.Item.Text, _list.Font, tr, ListItemColor(e.Item),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        private void OnDrawListSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (_list.View != View.Details) return;
            Color back = e.Item.Selected ? Theme.SelBg
                       : (e.ItemIndex % 2 == 1 ? Theme.RowAlt : Theme.Bg);
            using (SolidBrush b = new SolidBrush(back))
                e.Graphics.FillRectangle(b, e.Bounds);

            Color c = ListItemColor(e.Item);
            if (e.Item.Selected && Theme.IsLight) c = Theme.SelText;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            bool right = (e.ColumnIndex > 0 && e.ColumnIndex < _list.Columns.Count
                          && _list.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right);
            flags |= right ? TextFormatFlags.Right : TextFormatFlags.Left;
            Rectangle r = e.Bounds;
            r.Inflate(-S(6), 0);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _list.Font, r, c, flags);
        }

'''
mf = rep(mf, '        // ============================================================ 小工具\n',
         theme_block + '        // ============================================================ 小工具\n', 1, 'R19 主题方法块')

write(MF, mf)

# ---------------- Program.cs ----------------
pg = read(PG)
pg = rep(pg, '            string autoOpen = null, autoOut = null, autoView = null, autoDir = null;\n',
         '            string autoOpen = null, autoOut = null, autoView = null, autoDir = null;\n'
         '            string dumpTheme = null;\n', 1, 'P1 dumpTheme 变量')

pg = rep(pg, '                else if (args[i] == "--dir" && i + 1 < args.Length) autoDir = args[i + 1];\n            }\n',
         '                else if (args[i] == "--dir" && i + 1 < args.Length) autoDir = args[i + 1];\n'
         '                //   --theme <auto|light|dark>  指定主题（不读设置文件，自动化验证用）\n'
         '                //   --no-sys-theme             假装系统不支持深色，用来看「跟随系统」的灰态\n'
         '                //   --dump-theme <文件>        把主题状态写到文件后退出（自动化断言用）\n'
         '                else if (args[i] == "--theme" && i + 1 < args.Length) SetStartupTheme(args[i + 1]);\n'
         '                else if (args[i] == "--no-sys-theme") Theme.ForceNoSystemSupport = true;\n'
         '                else if (args[i] == "--dump-theme" && i + 1 < args.Length) dumpTheme = args[i + 1];\n'
         '            }\n', 1, 'P2 参数分支')

pg = rep(pg, '            try\n            {\n                Application.Run(new MainForm(autoOpen, gotoExport, autoOut, autoMulti, autoView, autoDir));\n            }\n',
         '            if (dumpTheme != null)      // 自动化验证：把主题状态写出来就退出，不弹窗\n'
         '            {\n'
         '                DumpThemeState(dumpTheme);\n'
         '                return;\n'
         '            }\n'
         '\n'
         '            try\n            {\n                Application.Run(new MainForm(autoOpen, gotoExport, autoOut, autoMulti, autoView, autoDir));\n            }\n',
         1, 'P3 dump 分支')

helpers = u'''
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

'''
pg = rep(pg, '        public static string CrashLogPath\n',
         helpers + '        public static string CrashLogPath\n', 1, 'P4 辅助方法')

pg = rep(pg, 'using System.Threading;\n', 'using System.Text;\nusing System.Threading;\n', 1, 'P5 using System.Text')

write(PG, pg)

print('替换失败项：', fails if fails else '无（全部命中）')
