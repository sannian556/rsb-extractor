# -*- coding: utf-8 -*-
import io, os
SRC = r'D:\AI deekseek\RSB提取器工程\src'
fails = []
def read(p): return io.open(p, 'r', encoding='utf-8').read().replace('\r\n', '\n')
def write(p, t): io.open(p, 'w', encoding='utf-8', newline='\r\n').write(t)
def rep(t, old, new, label):
    if t.count(old) != 1:
        fails.append('%s x%d' % (label, t.count(old))); return t
    return t.replace(old, new)

# ---------------- Theme.cs：加一个给单选/复选框上色的助手 ----------------
th = read(os.path.join(SRC, 'Theme.cs'))
old_schk = '''        /// <summary>检查框/单选框：深色下改自绘</summary>
        public static void StyleCheck(CheckBox c)
        {
            if (c == null) return;
            if (IsLight) { c.FlatStyle = FlatStyle.System; }
            else
            {
                c.FlatStyle = FlatStyle.Flat;
                c.BackColor = Color.Transparent;
                c.ForeColor = Text;
            }
        }
'''
new_schk = '''        /// <summary>检查框/单选框：深色下改自绘（浅色仍然系统原生）</summary>
        public static void StyleChoice(ButtonBase b)
        {
            if (b == null) return;
            if (IsLight)
            {
                b.FlatStyle = FlatStyle.System;
                b.BackColor = Color.Transparent;
                b.ForeColor = Color.Black;
            }
            else
            {
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Color.Transparent;
                b.ForeColor = Text;
            }
        }
'''
th = rep(th, old_schk, new_schk, 'T1 StyleChoice')
write(os.path.join(SRC, 'Theme.cs'), th)

# ---------------- MainForm.cs ----------------
mf = read(os.path.join(SRC, 'MainForm.cs'))

mf = rep(mf, '        public static bool AutoShowThemeMenu = false;\n',
         '        public static bool AutoShowThemeMenu = false;\n\n'
         '        /// <summary>自动化验证用：启动就停在「设置」页</summary>\n'
         '        public static bool AutoShowSettings = false;\n', 'M1 字段·开关')

mf = rep(mf, '        private ToolStripMenuItem _miThemeAuto, _miThemeLight, _miThemeDark;\n',
         '        private ToolStripMenuItem _miThemeAuto, _miThemeLight, _miThemeDark;\n'
         '        private Panel _pSettings;\n'
         '        private Button _btnSettings, _btnSettingsBack;\n'
         '        private RadioButton _rbThemeAuto, _rbThemeLight, _rbThemeDark;\n'
         '        private Label _lblSetTitle, _lblSetSec1, _lblSetSec2, _lblThemeHint, _lblAbout;\n'
         '        private bool _syncingTheme;\n', 'M2 字段·控件')

mf = rep(mf, '            BuildExportPanel();\n            ApplyTheme();\n',
         '            BuildExportPanel();\n            BuildSettingsPanel();\n            ApplyTheme();\n', 'M3 构造')

mf = rep(mf, '            if (AutoShowThemeMenu)\n                Shown += delegate { try { _btnTheme.ShowDropDown(); } catch { } };\n',
         '            if (AutoShowThemeMenu)\n                Shown += delegate { try { _btnTheme.ShowDropDown(); } catch { } };\n'
         '            if (AutoShowSettings)\n                Shown += delegate { ShowSettings(); };\n', 'M4 构造·设置页')

# 导入界面右下角「设置」按钮
mf = rep(mf, '            _pImport.Resize += delegate { LayoutImportPanel(); };\n            Controls.Add(_pImport);\n',
         '            _btnSettings = new Button();\n'
         '            _btnSettings.Text = "设置";\n'
         '            _btnSettings.Size = new Size(S(88), S(30));\n'
         '            _btnSettings.Click += delegate { ShowSettings(); };\n'
         '            _pImport.Controls.Add(_btnSettings);\n'
         '\n'
         '            _pImport.Resize += delegate { LayoutImportPanel(); };\n            Controls.Add(_pImport);\n', 'M5 设置按钮')

mf = rep(mf, '            _txtApkPath.Left = cx - _txtApkPath.Width / 2;\n            _txtApkPath.Top = _btnImport.Bottom + S(24);\n        }\n',
         '            _txtApkPath.Left = cx - _txtApkPath.Width / 2;\n            _txtApkPath.Top = _btnImport.Bottom + S(24);\n'
         '            // 设置按钮钉在主界面右下角（Windows 的习惯位置）\n'
         '            if (_btnSettings != null)\n'
         '            {\n'
         '                _btnSettings.Left = _pImport.ClientSize.Width - _btnSettings.Width - S(18);\n'
         '                _btnSettings.Top = _pImport.ClientSize.Height - _btnSettings.Height - S(16);\n'
         '            }\n        }\n', 'M6 设置按钮布局')

# 设置页：整段插在「主题」之前
settings_block = u'''
        // ============================================================ 界面 4：设置
        /// <summary>
        /// 照安卓版 RSB 提取器的思路：主界面右下角一个「设置」按钮，点进去是设置页。
        /// 样式照 Windows 来 —— 普通按钮 + 单选按钮组，不用手机那种大圆角卡片。
        /// 目前两项：外观（跟随系统 / 浅色 / 深色）+ 关于（版本、设置文件位置）。
        /// </summary>
        private void BuildSettingsPanel()
        {
            _pSettings = new Panel();
            _pSettings.Dock = DockStyle.Fill;
            _pSettings.BackColor = Theme.Bg;
            _pSettings.Visible = false;

            _lblSetTitle = new Label();
            _lblSetTitle.Text = "设置";
            _lblSetTitle.AutoSize = true;
            _lblSetTitle.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
            _lblSetTitle.Location = new Point(S(28), S(24));
            _pSettings.Controls.Add(_lblSetTitle);

            _lblSetSec1 = SectionLabel("外观", S(78));
            _pSettings.Controls.Add(_lblSetSec1);

            _rbThemeAuto = NewThemeRadio("跟随系统主题", S(108));
            _rbThemeLight = NewThemeRadio("浅色", S(140));
            _rbThemeDark = NewThemeRadio("深色", S(172));
            _pSettings.Controls.Add(_rbThemeAuto);
            _pSettings.Controls.Add(_rbThemeLight);
            _pSettings.Controls.Add(_rbThemeDark);

            _lblThemeHint = new Label();
            _lblThemeHint.AutoSize = false;
            _lblThemeHint.Location = new Point(S(56), S(206));
            _lblThemeHint.Size = new Size(S(620), S(44));
            _pSettings.Controls.Add(_lblThemeHint);

            _lblSetSec2 = SectionLabel("关于", S(268));
            _pSettings.Controls.Add(_lblSetSec2);

            _lblAbout = new Label();
            _lblAbout.AutoSize = false;
            _lblAbout.Location = new Point(S(56), S(298));
            _lblAbout.Size = new Size(S(620), S(76));
            _pSettings.Controls.Add(_lblAbout);

            _btnSettingsBack = new Button();
            _btnSettingsBack.Text = "返回主界面";
            _btnSettingsBack.Size = new Size(S(120), S(32));
            _btnSettingsBack.Click += delegate { BackToImport(); };
            _pSettings.Controls.Add(_btnSettingsBack);

            _pSettings.Resize += delegate { LayoutSettingsPanel(); };
            Controls.Add(_pSettings);
        }

        private Label SectionLabel(string text, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
            l.Location = new Point(S(28), y);
            return l;
        }

        private RadioButton NewThemeRadio(string text, int y)
        {
            RadioButton rb = new RadioButton();
            rb.Text = text;
            rb.AutoSize = true;
            rb.Location = new Point(S(36), y);
            rb.CheckedChanged += delegate { OnThemeRadioChanged(); };
            return rb;
        }

        /// <summary>单选按钮变了就切主题（同步时会重入，用 _syncingTheme 挡住）</summary>
        private void OnThemeRadioChanged()
        {
            if (_syncingTheme) return;
            if (_rbThemeDark != null && _rbThemeDark.Checked) SetThemeMode(Theme.Mode.Dark);
            else if (_rbThemeLight != null && _rbThemeLight.Checked) SetThemeMode(Theme.Mode.Light);
            else if (_rbThemeAuto != null && _rbThemeAuto.Checked) SetThemeMode(Theme.Mode.Auto);
        }

        private void LayoutSettingsPanel()
        {
            if (_btnSettingsBack != null)
            {
                _btnSettingsBack.Left = S(28);
                _btnSettingsBack.Top = _pSettings.ClientSize.Height - _btnSettingsBack.Height - S(24);
            }
            if (_lblAbout != null)
            {
                string ver = "?";
                try { ver = Application.ProductVersion; } catch { }
                _lblAbout.Text = "RSB 提取器　版本 " + ver
                    + "\\r\\n外观设置存在：" + Theme.SettingsPath
                    + "\\r\\n（删掉那个文件就恢复默认：跟随系统）";
            }
        }

        private void ShowSettings()
        {
            _pImport.Visible = false;
            _pBrowse.Visible = false;
            _pExport.Visible = false;
            _pSettings.Visible = true;
            UpdateThemeButton();
            LayoutSettingsPanel();
            _pSettings.Invalidate(true);
        }

'''
mf = rep(mf, '        // ============================================================ 主题\n',
         settings_block + '        // ============================================================ 主题\n', 'M7 设置页')

# 其它页面显示时把设置页藏掉
mf = rep(mf, '        private void ShowImport()\n        {\n            _pImport.Visible = true;\n            _pBrowse.Visible = false;\n            _pExport.Visible = false;\n        }\n',
         '        private void ShowImport()\n        {\n            _pImport.Visible = true;\n            _pBrowse.Visible = false;\n            _pExport.Visible = false;\n'
         '            if (_pSettings != null) _pSettings.Visible = false;\n        }\n', 'M8 隐藏设置页·import')
mf = rep(mf, '            _pExport.Visible = false;\n            // 隐藏的控件没有句柄，uxtheme 那两下压不进去',
         '            _pExport.Visible = false;\n            if (_pSettings != null) _pSettings.Visible = false;\n'
         '            // 隐藏的控件没有句柄，uxtheme 那两下压不进去', 'M9 隐藏设置页·browse')

# ApplyTheme：给设置页控件上色
mf = rep(mf, '            UpdateThemeButton();\n            Theme.ApplyTitleBar(Handle, Theme.IsLight);\n',
         '            if (_pSettings != null)\n'
         '            {\n'
         '                _pSettings.BackColor = Theme.Bg;\n'
         '                Label[] sl = new Label[] { _lblSetTitle, _lblSetSec1, _lblSetSec2 };\n'
         '                for (int i = 0; i < sl.Length; i++)\n'
         '                {\n'
         '                    if (sl[i] == null) continue;\n'
         '                    sl[i].BackColor = Color.Transparent;\n'
         '                    sl[i].ForeColor = Theme.Text;\n'
         '                }\n'
         '                Label[] sl2 = new Label[] { _lblThemeHint, _lblAbout };\n'
         '                for (int i = 0; i < sl2.Length; i++)\n'
         '                {\n'
         '                    if (sl2[i] == null) continue;\n'
         '                    sl2[i].BackColor = Color.Transparent;\n'
         '                    sl2[i].ForeColor = Theme.SubText;\n'
         '                }\n'
         '                Theme.StyleChoice(_rbThemeAuto);\n'
         '                Theme.StyleChoice(_rbThemeLight);\n'
         '                Theme.StyleChoice(_rbThemeDark);\n'
         '                Theme.StyleButton(_btnSettingsBack);\n'
         '            }\n'
         '            Theme.StyleButton(_btnSettings);\n'
         '\n'
         '            UpdateThemeButton();\n            Theme.ApplyTitleBar(Handle, Theme.IsLight);\n', 'M10 ApplyTheme')

# UpdateThemeButton：菜单和设置页单选按钮同步
mf = rep(mf, '            _btnTheme.ToolTipText = Theme.CurrentDescription();\n        }\n',
         '            _btnTheme.ToolTipText = Theme.CurrentDescription();\n'
         '\n'
         '            // 设置页里的那组单选按钮跟菜单是一份状态，一起同步\n'
         '            _syncingTheme = true;\n'
         '            try\n'
         '            {\n'
         '                if (_rbThemeAuto != null)\n'
         '                {\n'
         '                    _rbThemeAuto.Checked = (Theme.Current == Theme.Mode.Auto);\n'
         '                    _rbThemeAuto.Enabled = Theme.SystemSupportsTheme;\n'
         '                }\n'
         '                if (_rbThemeLight != null) _rbThemeLight.Checked = (Theme.Current == Theme.Mode.Light);\n'
         '                if (_rbThemeDark != null) _rbThemeDark.Checked = (Theme.Current == Theme.Mode.Dark);\n'
         '                if (_lblThemeHint != null)\n'
         '                {\n'
         '                    _lblThemeHint.Text = Theme.SystemSupportsTheme\n'
         '                        ? "「跟随系统主题」跟着 Windows 的「应用模式」走：你在系统里改成深色，本程序会实时跟着变。"\n'
         '                        : "本系统没有「深色模式」这个设置（Windows 10 1809 以上才有），所以「跟随系统主题」用不了、"\n'
         '                          + "已经变灰，外观按浅色显示；下面「浅色 / 深色」两档随时可选。";\n'
         '                }\n'
         '            }\n'
         '            finally { _syncingTheme = false; }\n        }\n', 'M11 同步单选')

write(os.path.join(SRC, 'MainForm.cs'), mf)

# ---------------- Program.cs：--settings ----------------
pg = read(os.path.join(SRC, 'Program.cs'))
pg = rep(pg, '                else if (args[i] == "--show-theme-menu") MainForm.AutoShowThemeMenu = true;\n',
         '                else if (args[i] == "--show-theme-menu") MainForm.AutoShowThemeMenu = true;\n'
         '                //   --settings                 启动就停在「设置」页（自动化截图用）\n'
         '                else if (args[i] == "--settings") MainForm.AutoShowSettings = true;\n', 'P1 --settings')
write(os.path.join(SRC, 'Program.cs'), pg)

print('失败：', fails if fails else '无')
