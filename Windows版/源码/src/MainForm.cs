using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ApkSmfExtractor
{
    /// <summary>
    /// 主窗口 —— 三个界面用三个 Panel 来回切：
    ///   1) 导入：只有一个「导入 apk 文件」按钮
    ///   2) 浏览：左边文件夹树 + 右边文件列表（可多选），右下角「导出smf文件」
    ///             只有选中【文件】时才亮；选中文件夹（或只是展开树）不亮
    ///   3) 导出：输出文件夹输入框 + 「选择文件夹」+ 「导出文件」
    /// 导出结束后弹窗报"成功 N 个 / 失败 M 个"，点确定回界面 1。
    /// </summary>
    public class MainForm : Form
    {
        // ---------------- DPI ----------------
        private float _scale = 1f;
        private int S(int px) { return (int)Math.Round(px * _scale); }

        // ---------------- 数据 ----------------
        private ApkReader _apk = new ApkReader();
        private ApkNode _curDir;
        private List<ApkEntry> _picked = new List<ApkEntry>();

        // ---------------- 控件 ----------------
        private Panel _pImport, _pBrowse, _pExport;
        private Button _btnImport;
        private TextBox _txtApkPath;
        private TreeView _tree;
        private ListView _list;
        private ToolStrip _navBar;
        private ToolStripButton _btnBackToMain, _navBack, _navForward, _navUp;
        private ToolStripButton _viewDetail, _viewLarge;
        private int _crumbStart;
        private int _crumbCount;
        private List<ApkNode> _navHistory = new List<ApkNode>();
        private int _navPos = -1;
        private Dictionary<string, TreeNode> _treeMap = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        private bool _syncingTree;
        private bool _largeIconView;
        private Label _lblCount;
        private Label _lblSelInfo;
        private Button _btnGoExport;
        private Label _lblOutCaption;
        private TextBox _txtOut;
        private Button _btnPickDir;
        private Button _btnDoExport;
        private Label _lblStatus;
        private ProgressBar _bar;
        private string _exportDir;
        private CheckBox _chkSameAsApk;
        private Label _lblDefaultOut;
        private TextBox _txtDefaultOut;
        private Button _btnPickDefaultOut;
        private Label _lblSetSec3;
        private bool _sameAsApk = true;
        private long _lastPct = -1;
        private long _lastMs;
        private ToolStripDropDownButton _btnTheme;
        private ToolStripMenuItem _miThemeAuto, _miThemeLight, _miThemeDark;
        private Panel _pSettings;
        private Button _btnSettings, _btnSettingsBack;
        private RadioButton _rbThemeAuto, _rbThemeLight, _rbThemeDark;
        private Label _lblSetTitle, _lblSetSec1, _lblSetSec2, _lblThemeHint, _lblAbout;
        private bool _syncingTheme;

        /// <summary>自动化验证用：启动就把「外观」菜单弹出来（看灰态）</summary>
        public static bool AutoShowThemeMenu = false;

        /// <summary>自动化验证用：启动就停在「设置」页</summary>
        public static bool AutoShowSettings = false;

        public MainForm() : this(null, false, null, 0, null, null) { }

        public MainForm(string autoOpen, bool gotoExport, string autoOut, int autoMulti, string autoView, string autoDir)
        {
            using (Graphics g = CreateGraphics()) _scale = g.DpiX / 96f;
            if (_scale < 1f) _scale = 1f;
            Theme.UiScale = _scale;      // 自绘控件（比如设置页的单选按钮）也要按同样的比例放大

            Text = "RSB 提取器";
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(940), S(600));
            MinimumSize = new Size(S(720), S(460));
            // XP 上没有"微软雅黑"，字体族不存在时 new Font 可能抛异常，兜一下
            try { Font = new Font("Microsoft YaHei UI", 9f); }
            catch { Font = SystemFonts.DefaultFont; }
            // 主题：先读设置 + 探测系统（决定「跟随系统」能不能用），界面建好后再统一刷一遍
            Theme.Load();
            Theme.Refresh();
            _sameAsApk = Theme.GetSetting("same_as_apk", "1") != "0";   // 默认打开
            BackColor = Theme.Bg;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildImportPanel();
            BuildBrowsePanel();
            BuildExportPanel();
            BuildSettingsPanel();
            ApplyTheme();
            if (AutoShowThemeMenu)
                Shown += delegate { try { _btnTheme.ShowDropDown(); } catch { } };
            ShowImport();
            // --settings 要在这里就切过去，不能挂在 Shown 上：
            // Shown 只有跑了消息循环才触发，而 --dump-ui 这类自动化口子不跑消息循环，会看到一片"还没进设置页"的假象
            if (AutoShowSettings)
                ShowSettings();

            if (autoOpen != null)
            {
                string p = autoOpen;
                bool g = gotoExport;
                string o = autoOut;
                int m = autoMulti;
                string v = autoView;
                string d = autoDir;
                Shown += delegate { AutoOpen(p, g, o, m, v, d); };
            }
        }

        /// <summary>命令行 --open / --open-export 用：直接导入并跳到指定界面（自动化验证用）</summary>
        private void AutoOpen(string path, bool gotoExport, string autoOut, int autoMulti, string autoView, string autoDir)
        {
            if (!TryOpenApk(path)) return;      // 里面已经 FillTree / FillList / ShowBrowse

            if (!string.IsNullOrEmpty(autoDir))
            {
                TreeNode tn;
                if (_treeMap.TryGetValue(autoDir.Trim('/'), out tn))
                {
                    ApkNode node = tn.Tag as ApkNode;
                    if (node != null) NavigateTo(node);
                }
            }
            if (!string.IsNullOrEmpty(autoView) && autoView.ToLowerInvariant().StartsWith("l"))
                SetViewMode(true);

            if (!gotoExport) return;

            // 找 assets 目录（.smf 都在那儿），选中第一个 .smf 后进导出界面
            ApkNode assets = null;
            for (int i = 0; i < _apk.Root.Children.Count; i++)
            {
                ApkNode c = _apk.Root.Children[i];
                if (c.IsDir && string.Equals(c.Name, "assets", StringComparison.OrdinalIgnoreCase)) { assets = c; break; }
            }
            if (assets != null) FillList(assets);

            int want = autoMulti > 0 ? autoMulti : 1;
            int got = 0;
            for (int i = 0; i < _list.Items.Count && got < want; i++)
            {
                ApkNode n = _list.Items[i].Tag as ApkNode;
                if (n != null && !n.IsDir && n.Entry.IsSmf) { _list.Items[i].Selected = true; got++; }
            }
            if (!string.IsNullOrEmpty(autoOut)) _txtOut.Text = autoOut;
            UpdateExportButton();
            if (_picked.Count > 0) OnGoExportClick();
        }

        // ============================================================ 界面 1
        /// <summary>
        /// 导入界面：一个「导入 apk 文件」按钮 + 下面一个路径输入框。
        /// 三种导入方式：点按钮选、把 apk 直接拖进窗口、在输入框里粘贴路径后按回车。
        /// （按用户要求，输入框右边不放"浏览"按钮 —— 选文件走上面那个大按钮。）
        /// </summary>
        private void BuildImportPanel()
        {
            _pImport = new Panel();
            _pImport.Dock = DockStyle.Fill;
            _pImport.BackColor = Theme.Bg;
            _pImport.AllowDrop = true;

            _btnImport = new Button();
            _btnImport.Text = "导入 apk 文件";
            _btnImport.Size = new Size(S(240), S(70));
            _btnImport.Font = new Font(Font.FontFamily, 14f, FontStyle.Bold);
            _btnImport.FlatStyle = FlatStyle.System;
            _btnImport.Click += delegate { OnImportClick(); };
            _pImport.Controls.Add(_btnImport);

            _txtApkPath = new TextBox();
            _txtApkPath.Width = S(430);
            _txtApkPath.Font = new Font(Font.FontFamily, 10f);
            _txtApkPath.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    TryOpenApk(_txtApkPath.Text.Trim());
                }
            };
            _pImport.Controls.Add(_txtApkPath);

            try
            {
                SendMessage(_txtApkPath.Handle, EM_SETCUEBANNER, (IntPtr)1,
                    "把 apk 文件拖到这里，或粘贴路径后按回车");
            }
            catch { }

            // 拖放：面板、按钮、输入框都要挂 AllowDrop ——
            // 只给面板挂的话，拖到按钮/输入框上面会没反应（拖放是按窗口走的）
            Control[] drops = new Control[] { _pImport, _btnImport, _txtApkPath };
            for (int i = 0; i < drops.Length; i++)
            {
                drops[i].AllowDrop = true;
                drops[i].DragEnter += OnImportDragEnter;
                drops[i].DragLeave += OnImportDragLeave;
                drops[i].DragDrop += OnImportDragDrop;
            }

            _btnSettings = new Button();
            _btnSettings.Text = "设置";
            _btnSettings.Size = new Size(S(88), S(30));
            _btnSettings.Click += delegate { ShowSettings(); };
            _pImport.Controls.Add(_btnSettings);

            _pImport.Resize += delegate { LayoutImportPanel(); };
            Controls.Add(_pImport);
        }

        private void LayoutImportPanel()
        {
            int cx = _pImport.ClientSize.Width / 2;
            int cy = _pImport.ClientSize.Height / 2;
            _btnImport.Left = cx - _btnImport.Width / 2;
            _btnImport.Top = cy - _btnImport.Height - S(14);
            _txtApkPath.Left = cx - _txtApkPath.Width / 2;
            _txtApkPath.Top = _btnImport.Bottom + S(24);
            // 设置按钮钉在主界面右下角（Windows 的习惯位置）
            if (_btnSettings != null)
            {
                _btnSettings.Left = _pImport.ClientSize.Width - _btnSettings.Width - S(18);
                _btnSettings.Top = _pImport.ClientSize.Height - _btnSettings.Height - S(16);
            }
        }

        private static string[] GetDropFiles(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            return e.Data.GetData(DataFormats.FileDrop) as string[];
        }

        // 输入框的水印提示（EM_SETCUEBANNER 是 Vista 才有的；XP 上调用会静静失败，不影响功能）
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

        private void OnImportDragEnter(object sender, DragEventArgs e)
        {
            string[] files = GetDropFiles(e);
            bool ok = files != null && files.Length > 0;
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok) _pImport.BackColor = Theme.DropHover;   // 拖进来时给点视觉反馈
        }

        private void OnImportDragLeave(object sender, EventArgs e)
        {
            _pImport.BackColor = Theme.Bg;
        }

        private void OnImportDragDrop(object sender, DragEventArgs e)
        {
            _pImport.BackColor = Theme.Bg;
            string[] files = GetDropFiles(e);
            if (files == null || files.Length == 0) return;
            TryOpenApk(files[0]);
        }

        // ============================================================ 界面 2
        /// <summary>
        /// 照 Windows 资源管理器的样子做：
        ///   顶部一条导航栏 —— [返回主界面] | [◀后退][▶前进][▲上一级] | 面包屑地址 | [详细信息][大图标]
        ///   中间左树右表（都带系统图标，图标跟资源管理器里显示的是同一套）
        ///   底部状态栏 —— 左边"共 N 项（x 个文件夹，y 个文件）"，右边选中信息和导出按钮
        /// </summary>
        private void BuildBrowsePanel()
        {
            _pBrowse = new Panel();
            _pBrowse.Dock = DockStyle.Fill;
            _pBrowse.BackColor = Theme.Bg;
            _pBrowse.Visible = false;

            // ---- 底部状态条（先加，Dock 顺序才对）----
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = S(50);
            bottom.BackColor = Theme.PanelAlt;
            bottom.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 0, 0, bottom.ClientSize.Width, 0);
            };

            _lblCount = new Label();
            _lblCount.AutoSize = true;
            _lblCount.Location = new Point(S(14), S(17));
            _lblCount.ForeColor = Theme.SubText;
            bottom.Controls.Add(_lblCount);

            _lblSelInfo = new Label();
            _lblSelInfo.AutoSize = true;
            _lblSelInfo.ForeColor = Theme.Link;
            bottom.Controls.Add(_lblSelInfo);

            _btnGoExport = new Button();
            _btnGoExport.Text = "导出smf文件";
            _btnGoExport.Size = new Size(S(130), S(32));
            _btnGoExport.FlatStyle = FlatStyle.System;
            _btnGoExport.Enabled = false;                       // 平时灰着
            _btnGoExport.Click += delegate { OnGoExportClick(); };
            bottom.Controls.Add(_btnGoExport);
            bottom.Resize += delegate { LayoutBottom(); };
            // 选中信息是 AutoSize 的：文字一变长它只会往右长，Left 不重算就会盖到按钮上面去
            // （用户截图里"导出smf文件"只剩半截，就是被它压的）
            _lblSelInfo.TextChanged += delegate { LayoutBottom(); };

            // ---- 顶部导航栏 ----
            _navBar = new ToolStrip();
            _navBar.Dock = DockStyle.Top;
            _navBar.AutoSize = false;
            _navBar.Height = S(38);
            _navBar.GripStyle = ToolStripGripStyle.Hidden;
            _navBar.Padding = new Padding(S(8), S(5), S(8), S(5));
            _navBar.RenderMode = ToolStripRenderMode.System;
            _navBar.BackColor = Theme.Panel;
            _navBar.CanOverflow = false;        // 不加这句会冒出一个多余的"溢出箭头"按钮

            _btnBackToMain = NavButton("← 返回主界面", "回到最开始那个界面", delegate { BackToImport(); });
            _navBar.Items.Add(_btnBackToMain);
            _navBar.Items.Add(new ToolStripSeparator());

            _navBack = NavButton("←", "后退", delegate { GoBack(); });
            _navForward = NavButton("→", "前进", delegate { GoForward(); });
            _navUp = NavButton("↑", "上一级", delegate { GoUp(); });
            _navBar.Items.Add(_navBack);
            _navBar.Items.Add(_navForward);
            _navBar.Items.Add(_navUp);
            _navBar.Items.Add(new ToolStripSeparator());

            _crumbStart = _navBar.Items.Count;      // 面包屑从这里开始动态插入

            // 视图切换按钮用 Alignment=Right 顶到最右（.NET 2.0 的 ToolStripItem 没有 Spring 属性）
            _viewLarge = NavButton("大图标", "按大图标显示", delegate { SetViewMode(true); });
            _viewLarge.Alignment = ToolStripItemAlignment.Right;
            _viewDetail = NavButton("详细信息", "按详细信息显示（带大小和类型）", delegate { SetViewMode(false); });
            _viewDetail.Alignment = ToolStripItemAlignment.Right;
            _viewDetail.Checked = true;                 // 默认是详细信息视图
            _navBar.Items.Add(_viewDetail);
            _navBar.Items.Add(_viewLarge);

            // 外观三态：跟随系统 / 浅色 / 深色。
            // 系统没有深色模式那一档（Win10 1809 以下）时，「跟随系统」变灰 + Tooltip 说明原因。
            _btnTheme = new ToolStripDropDownButton();
            _btnTheme.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _btnTheme.Alignment = ToolStripItemAlignment.Right;
            _btnTheme.Margin = new Padding(S(1), S(2), S(1), S(2));
            _btnTheme.Padding = new Padding(S(5), S(0), S(5), 0);
            _btnTheme.AutoToolTip = false;
            _miThemeAuto = new ToolStripMenuItem("跟随系统");
            _miThemeLight = new ToolStripMenuItem("浅色");
            _miThemeDark = new ToolStripMenuItem("深色");
            _miThemeAuto.Click += delegate { SetThemeMode(Theme.Mode.Auto); };
            _miThemeLight.Click += delegate { SetThemeMode(Theme.Mode.Light); };
            _miThemeDark.Click += delegate { SetThemeMode(Theme.Mode.Dark); };
            _btnTheme.DropDownItems.Add(_miThemeAuto);
            _btnTheme.DropDownItems.Add(new ToolStripSeparator());
            _btnTheme.DropDownItems.Add(_miThemeLight);
            _btnTheme.DropDownItems.Add(_miThemeDark);
            _navBar.Items.Add(_btnTheme);

            // ---- 中间分栏 ----
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterWidth = S(6);

            _tree = new TreeView();
            _tree.Dock = DockStyle.Fill;
            _tree.HideSelection = false;
            _tree.BorderStyle = BorderStyle.None;
            _tree.ShowLines = true;
            _tree.ShowRootLines = true;
            _tree.ShowPlusMinus = true;
            _tree.ImageList = ShellIcons.SmallList;
            _tree.AfterSelect += delegate { OnTreeSelect(); };
            split.Panel1.Controls.Add(_tree);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;                  // ← Ctrl / Shift 多选
            _list.HideSelection = false;
            _list.BorderStyle = BorderStyle.None;
            _list.SmallImageList = ShellIcons.SmallList;
            _list.LargeImageList = ShellIcons.LargeList;
            _list.ShowItemToolTips = true;             // 大图标里名字太长会被省略，鼠标停上去能看到全名
            _list.Columns.Add("名称", S(340), HorizontalAlignment.Left);
            _list.Columns.Add("大小", S(110), HorizontalAlignment.Right);
            _list.Columns.Add("类型", S(150), HorizontalAlignment.Left);
            _list.ItemSelectionChanged += delegate { UpdateExportButton(); };
            _list.DoubleClick += delegate { OnListDoubleClick(); };
            _list.Resize += delegate { AdjustColumns(); };
            // 深色下系统画的表头/行是白的，改成自绘；浅色模式仍走系统原生，保持原来的样子
            _list.DrawColumnHeader += OnDrawColumnHeader;
            _list.DrawItem += OnDrawListItem;
            _list.DrawSubItem += OnDrawListSubItem;
            split.Panel2.Controls.Add(_list);

            _pBrowse.Controls.Add(split);
            _pBrowse.Controls.Add(_navBar);
            _pBrowse.Controls.Add(bottom);
            Controls.Add(_pBrowse);

            // 分栏位置等窗体有真实宽度后再设，避免 SplitterDistance 抛异常
            _pBrowse.HandleCreated += delegate
            {
                try { split.SplitterDistance = S(250); } catch { }
            };
        }

        private ToolStripButton NavButton(string text, string tip, EventHandler onClick)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.ToolTipText = tip;
            b.AutoToolTip = true;
            b.Margin = new Padding(S(1), S(2), S(1), S(2));
            b.Padding = new Padding(S(5), 0, S(5), 0);
            b.Click += onClick;
            return b;
        }

        // ============================================================ 界面 3
        private void BuildExportPanel()
        {
            _pExport = new Panel();
            _pExport.Dock = DockStyle.Fill;
            _pExport.BackColor = Theme.Bg;
            _pExport.Visible = false;

            int y = S(56);

            _lblOutCaption = new Label();
            _lblOutCaption.Text = "导出到：";
            _lblOutCaption.AutoSize = true;
            _lblOutCaption.Location = new Point(S(36), y + S(7));
            _pExport.Controls.Add(_lblOutCaption);

            _txtOut = new TextBox();
            _txtOut.Location = new Point(S(104), y);
            _txtOut.Width = S(380);
            _pExport.Controls.Add(_txtOut);

            _btnPickDir = new Button();
            _btnPickDir.Text = "选择文件夹";
            _btnPickDir.Size = new Size(S(112), S(28));
            _btnPickDir.Location = new Point(S(496), y - S(1));
            _btnPickDir.FlatStyle = FlatStyle.System;
            _btnPickDir.Click += delegate { OnPickFolder(); };
            _pExport.Controls.Add(_btnPickDir);

            _btnDoExport = new Button();
            _btnDoExport.Text = "导出文件";
            _btnDoExport.Size = new Size(S(112), S(28));
            _btnDoExport.Location = new Point(S(618), y - S(1));
            _btnDoExport.FlatStyle = FlatStyle.System;
            _btnDoExport.Click += delegate { OnDoExport(); };
            _pExport.Controls.Add(_btnDoExport);

            _lblStatus = new Label();
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(S(36), y + S(44));
            _lblStatus.ForeColor = Theme.SubText;
            _pExport.Controls.Add(_lblStatus);

            // 真实字节进度条：0~1000 对应 0.0%~100.0%
            _bar = new ProgressBar();
            _bar.Location = new Point(S(36), y + S(76));
            _bar.Size = new Size(S(694), S(18));
            _bar.Minimum = 0;
            _bar.Maximum = 1000;
            _bar.Value = 0;
            _pExport.Controls.Add(_bar);

            Controls.Add(_pExport);
        }

        // ============================================================ 切换
        private void ShowImport()
        {
            _pImport.Visible = true;
            _pBrowse.Visible = false;
            _pExport.Visible = false;
            if (_pSettings != null) _pSettings.Visible = false;
        }

        private void ShowBrowse()
        {
            _pImport.Visible = false;
            _pBrowse.Visible = true;
            _pExport.Visible = false;
            if (_pSettings != null) _pSettings.Visible = false;
            // 隐藏的控件没有句柄，uxtheme 那两下压不进去 —— 等真显示了再补一次（深色下滚动条别是白的）
            Theme.ApplyScrollTheme(_tree);
            Theme.ApplyScrollTheme(_list);
            LayoutBottom();         // 底部状态条也在这里补一次：光等 Resize 事件，隐藏期间排好的版可能压根不触发
        }

        private void ShowExport()
        {
            _pImport.Visible = false;
            _pBrowse.Visible = false;
            _pExport.Visible = true;
        }

        private void BackToImport()
        {
            try { _apk.Close(); } catch { }
            if (_tree != null) _tree.Nodes.Clear();
            if (_list != null) _list.Items.Clear();
            _treeMap.Clear();
            _navHistory.Clear();
            _navPos = -1;
            _crumbCount = 0;
            _picked.Clear();
            _curDir = null;
            _txtOut.Text = "";
            _lblStatus.Text = "";
            _exportDir = null;
            if (_bar != null) _bar.Value = 0;
            _lblSelInfo.Text = "";
            if (_lblCount != null) _lblCount.Text = "";
            _btnGoExport.Enabled = false;
            _btnDoExport.Enabled = true;
            _btnPickDir.Enabled = true;
            ShowImport();
        }

        // ============================================================ 动作
        private void OnImportClick()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "选择 apk 文件";
                d.Filter = "Android 应用包 (*.apk)|*.apk|所有文件 (*.*)|*.*";
                d.CheckFileExists = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                TryOpenApk(d.FileName);
            }
        }

        /// <summary>
        /// 打开一个 apk —— 按钮选、拖进来、输入框回车，三条路都走这里。
        /// 成功后顺手 FillTree / FillList / ShowBrowse；失败返回 false。
        /// </summary>
        private bool TryOpenApk(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "找不到这个文件：\n" + path, "打开失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                _apk.Open(path);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, "打开失败：\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            Cursor = Cursors.Default;

            if (_txtApkPath != null) _txtApkPath.Text = path;
            FillTree();
            FillList(_apk.Root);
            ShowBrowse();
            return true;
        }

        private void FillTree()
        {
            string dirKey = ShellIcons.GetKey("", true);

            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            _treeMap.Clear();

            TreeNode root = new TreeNode(_apk.Root.Name);
            root.ImageKey = dirKey;
            root.SelectedImageKey = dirKey;
            root.Tag = _apk.Root;
            _tree.Nodes.Add(root);
            _treeMap[""] = root;
            AddDirNodes(root, _apk.Root, dirKey);
            root.Expand();
            _tree.EndUpdate();
        }

        private void AddDirNodes(TreeNode tn, ApkNode n, string dirKey)
        {
            for (int i = 0; i < n.Children.Count; i++)
            {
                ApkNode c = n.Children[i];
                if (!c.IsDir) continue;
                TreeNode t = new TreeNode(c.Name);
                t.ImageKey = dirKey;
                t.SelectedImageKey = dirKey;
                t.Tag = c;
                tn.Nodes.Add(t);
                _treeMap[c.FullPath] = t;
                AddDirNodes(t, c, dirKey);
            }
        }

        private void FillList(ApkNode dir)
        {
            if (dir == null) return;
            _curDir = dir;

            _list.BeginUpdate();
            _list.Items.Clear();
            for (int i = 0; i < dir.Children.Count; i++)
            {
                ApkNode c = dir.Children[i];
                ListViewItem it = new ListViewItem(c.Name);
                it.ImageKey = ShellIcons.GetKey(c.Name, c.IsDir);      // 系统图标，和资源管理器同一套
                if (c.IsDir)
                {
                    it.SubItems.Add("");
                    it.SubItems.Add("文件夹");
                }
                else
                {
                    it.SubItems.Add(FormatSize(c.Size));
                    it.SubItems.Add(DescribeType(c.Name));
                    if (!c.Entry.IsSmf) it.ForeColor = Theme.Dim;   // 非 .smf 淡显
                }
                it.Tag = c;
                _list.Items.Add(it);
            }
            _list.EndUpdate();

            UpdateCrumb();
            UpdateStatusCount();
            UpdateNavButtons();
            SyncTreeSelection();
            AdjustColumns();
            UpdateExportButton();
        }

        // ============================================================ 导航（照资源管理器的行为）
        private void NavigateTo(ApkNode dir)
        {
            if (dir == null || !dir.IsDir) return;
            while (_navHistory.Count > _navPos + 1) _navHistory.RemoveAt(_navHistory.Count - 1);
            _navHistory.Add(dir);
            _navPos = _navHistory.Count - 1;
            FillList(dir);
        }

        private void GoBack()
        {
            if (_navPos <= 0) return;
            _navPos--;
            FillList(_navHistory[_navPos]);
        }

        private void GoForward()
        {
            if (_navPos >= _navHistory.Count - 1) return;
            _navPos++;
            FillList(_navHistory[_navPos]);
        }

        private void GoUp()
        {
            if (_curDir == null || _curDir.Parent == null) return;
            NavigateTo(_curDir.Parent);
        }

        private void UpdateNavButtons()
        {
            if (_navBar == null) return;
            _navBack.Enabled = _navPos > 0;
            _navForward.Enabled = _navPos < _navHistory.Count - 1;
            _navUp.Enabled = _curDir != null && _curDir.Parent != null;
        }

        private void OnTreeSelect()
        {
            if (_syncingTree) return;
            if (_tree.SelectedNode == null) return;
            ApkNode n = _tree.SelectedNode.Tag as ApkNode;
            if (n != null && n.IsDir) NavigateTo(n);
        }

        private void SyncTreeSelection()
        {
            if (_curDir == null || _treeMap.Count == 0) return;
            TreeNode t;
            if (!_treeMap.TryGetValue(_curDir.FullPath, out t)) return;
            if (_tree.SelectedNode == t) return;
            _syncingTree = true;
            try { _tree.SelectedNode = t; } catch { }
            _syncingTree = false;
        }

        /// <summary>重建地址栏面包屑（根 › assets › properties，每一段都能点着跳）</summary>
        private void UpdateCrumb()
        {
            while (_crumbCount > 0)
            {
                _navBar.Items.RemoveAt(_crumbStart);
                _crumbCount--;
            }
            if (_curDir == null) return;

            List<ApkNode> chain = new List<ApkNode>();
            ApkNode n = _curDir;
            while (n != null) { chain.Insert(0, n); n = n.Parent; }

            int at = _crumbStart;
            for (int i = 0; i < chain.Count; i++)
            {
                if (i > 0)
                {
                    ToolStripLabel sep = new ToolStripLabel("›");
                    sep.ForeColor = Theme.Dim;
                    sep.Margin = new Padding(S(2), S(7), S(2), 0);
                    _navBar.Items.Insert(at++, sep);
                    _crumbCount++;
                }
                ApkNode target = chain[i];
                string tip = target.FullPath.Length == 0 ? "APK 根目录" : target.FullPath;
                ToolStripButton b = NavButton(target.Name, tip, delegate { NavigateTo(target); });
                bool last = (i == chain.Count - 1);
                b.ForeColor = last ? Theme.Text : Theme.Link;
                if (last) b.Font = new Font(Font, FontStyle.Bold);
                _navBar.Items.Insert(at++, b);
                _crumbCount++;
            }
        }

        private void UpdateStatusCount()
        {
            if (_lblCount == null) return;
            if (_curDir == null) { _lblCount.Text = ""; return; }
            int dirs = 0, files = 0;
            for (int i = 0; i < _curDir.Children.Count; i++)
            {
                if (_curDir.Children[i].IsDir) dirs++; else files++;
            }
            _lblCount.Text = "共 " + _curDir.Children.Count + " 项（" + dirs + " 个文件夹，" + files + " 个文件）";
        }

        private void SetViewMode(bool large)
        {
            _largeIconView = large;
            _list.View = large ? View.LargeIcon : View.Details;
            _viewDetail.Checked = !large;
            _viewLarge.Checked = large;
            if (!large) AdjustColumns();
        }

        /// <summary>名称列自适应剩余宽度（跟资源管理器一样，拉窗口列就跟着变）</summary>
        private void AdjustColumns()
        {
            if (_list == null || _list.Columns.Count < 3) return;
            if (_list.View != View.Details) return;
            int others = 0;
            for (int i = 1; i < _list.Columns.Count; i++) others += _list.Columns[i].Width;
            int w = _list.ClientSize.Width - others - S(8);
            if (w > S(180)) _list.Columns[0].Width = w;
        }

        /// <summary>需求 3：只有选中【文件】时「导出smf文件」才亮；选中文件夹 / 只展开目录都不亮。</summary>
        /// <summary>底部状态栏重排：按钮始终靠右，选中信息贴在它左边（文字一变就要重算）</summary>
        private void LayoutBottom()
        {
            if (_btnGoExport == null || _btnGoExport.Parent == null) return;
            Control p = _btnGoExport.Parent;
            _btnGoExport.Left = p.ClientSize.Width - _btnGoExport.Width - S(14);
            _btnGoExport.Top = (p.ClientSize.Height - _btnGoExport.Height) / 2;
            if (_lblSelInfo != null)
            {
                // 宽度自己量：Label 是 AutoSize 的，TextChanged 触发时它的 Width 还是旧值，
                // 拿旧宽度算 Left 会让文字往右长、又压到按钮上（用户截图里那个"n文件"）
                int tw = string.IsNullOrEmpty(_lblSelInfo.Text)
                    ? 0 : TextRenderer.MeasureText(_lblSelInfo.Text, _lblSelInfo.Font).Width;
                _lblSelInfo.Left = _btnGoExport.Left - tw - S(16);
                if (_lblSelInfo.Left < S(8)) _lblSelInfo.Left = S(8);   // 兜底：宁可挤也别跑到外面
                _lblSelInfo.Top = (p.ClientSize.Height - _lblSelInfo.Height) / 2;
            }
        }

        private void UpdateExportButton()
        {
            _picked.Clear();
            for (int i = 0; i < _list.SelectedItems.Count; i++)
            {
                ApkNode n = _list.SelectedItems[i].Tag as ApkNode;
                if (n != null && !n.IsDir) _picked.Add(n.Entry);
            }
            _btnGoExport.Enabled = _picked.Count > 0;
            if (_picked.Count > 0)
            {
                int smf = 0;
                for (int i = 0; i < _picked.Count; i++) if (_picked[i].IsSmf) smf++;
                _lblSelInfo.Text = smf == _picked.Count
                    ? "已选 " + _picked.Count + " 个文件"
                    : "已选 " + _picked.Count + " 个文件（其中 .smf " + smf + " 个）";
            }
            else
            {
                _lblSelInfo.Text = "";
            }
        }

        private void OnListDoubleClick()
        {
            if (_list.SelectedItems.Count != 1) return;
            ApkNode n = _list.SelectedItems[0].Tag as ApkNode;
            if (n != null && n.IsDir) NavigateTo(n);
        }

        private void OnGoExportClick()
        {
            if (_picked.Count == 0) return;
            if (_txtOut.Text.Trim().Length == 0)
            {
                string dir = null;
                if (_sameAsApk)
                {
                    try { dir = Path.GetDirectoryName(_apk.Path); } catch { }
                }
                if (string.IsNullOrEmpty(dir)) dir = Theme.GetSetting("default_out", "");
                if (string.IsNullOrEmpty(dir)) dir = Theme.GetSetting("last_out", "");
                if (!string.IsNullOrEmpty(dir)) _txtOut.Text = dir;
            }
            _lblStatus.Text = "";
            if (_bar != null) _bar.Value = 0;
            ShowExport();
            _txtOut.Focus();
            _txtOut.SelectAll();
        }

        private void OnPickFolder()
        {
            string cur = _txtOut.Text.Trim();
            if (cur.Length == 0)
            {
                try { cur = Path.GetDirectoryName(_apk.Path); }
                catch { }
            }
            string r = FolderPicker.Pick(Handle, cur);
            if (r != null)
            {
                _txtOut.Text = r;
                _txtOut.SelectionStart = _txtOut.Text.Length;
            }
        }

        // ============================================================ 导出
        private void OnDoExport()
        {
            string dir = _txtOut.Text.Trim();
            if (dir.Length == 0)
            {
                MessageBox.Show(this, "请先填写或选择导出文件夹。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "文件夹不存在，且无法创建：\n" + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            _btnDoExport.Enabled = false;
            _btnPickDir.Enabled = false;
            if (_bar != null) { _bar.Maximum = 1000; _bar.Value = 0; }
            _lblStatus.Text = "正在导出，请稍候…";
            Application.DoEvents();

            string outDir = dir;
            _exportDir = outDir;
            Theme.SetSetting("last_out", outDir);       // 下次导出页默认填这个
            _lastPct = -1;
            _lastMs = 0;

            Thread t = new Thread(new ThreadStart(delegate
            {
                List<ApkEntry> list = new List<ApkEntry>(_picked);
                ExportOutcome outcome = Exporter.Export(_apk, list, outDir,
                    delegate(int idx, int total, string name)
                    {
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            if (_lblStatus != null)
                                _lblStatus.Text = "正在导出 " + idx + " / " + total + " 个 .smf";
                        }));
                    },
                    delegate(long done, long total, string name)
                    {
                        long pct = total <= 0 ? 0 : Math.Min(1000, done * 1000 / total);
                        long now = Environment.TickCount;
                        if (pct == _lastPct && now - _lastMs < 120 && done < total) return;
                        _lastPct = pct;
                        _lastMs = now;
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            if (_bar != null) _bar.Value = (int)pct;
                            string who = string.IsNullOrEmpty(name) ? "正在收尾…" : ("正在导出：" + name);
                            if (_lblStatus != null)
                                _lblStatus.Text = who + "　" + FormatSize(done) + " / " + FormatSize(total)
                                                + "（" + (pct / 10) + "." + (pct % 10) + "%）";
                        }));
                    });

                BeginInvoke(new MethodInvoker(delegate
                {
                    _btnDoExport.Enabled = true;
                    _btnPickDir.Enabled = true;
                    if (_bar != null) _bar.Value = 1000;
                    _lblStatus.Text = "";

                    string msg = "导出完成\n\n成功 " + outcome.Success + " 个，失败 " + outcome.Failed + " 个";
                    msg += "\n\n输出到：\n" + outDir;
                    if (outcome.Failed > 0)
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < outcome.Detail.Count && sb.Length < 1200; i++)
                            sb.AppendLine("· " + outcome.Detail[i]);
                        msg += "\n\n失败明细：\n" + sb.ToString().TrimEnd();
                    }
                    MessageBox.Show(this, msg, "导出结果", MessageBoxButtons.OK,
                        outcome.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    BackToImport();       // 点确定 → 回界面 1
                }));
            }));
            t.IsBackground = true;
            t.Start();
        }



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

            _lblSetSec3 = SectionLabel("导出", S(250));
            _pSettings.Controls.Add(_lblSetSec3);

            _chkSameAsApk = new CheckBox();
            _chkSameAsApk.Text = "导出位置和安装包位置一样";
            _chkSameAsApk.AutoSize = true;
            _chkSameAsApk.Location = new Point(S(36), S(278));
            _chkSameAsApk.Checked = _sameAsApk;
            _chkSameAsApk.CheckedChanged += delegate { OnSameAsApkChanged(); };
            _pSettings.Controls.Add(_chkSameAsApk);

            _lblDefaultOut = new Label();
            _lblDefaultOut.Text = "默认导出文件夹";
            _lblDefaultOut.AutoSize = true;
            _lblDefaultOut.Location = new Point(S(36), S(310));
            _pSettings.Controls.Add(_lblDefaultOut);

            _txtDefaultOut = new TextBox();
            _txtDefaultOut.Location = new Point(S(36), S(332));
            _txtDefaultOut.Width = S(520);
            _txtDefaultOut.Text = Theme.GetSetting("default_out", "");
            _txtDefaultOut.Leave += delegate { SaveDefaultOut(); };
            _pSettings.Controls.Add(_txtDefaultOut);

            _btnPickDefaultOut = new Button();
            _btnPickDefaultOut.Text = "选择默认导出文件夹";
            _btnPickDefaultOut.Size = new Size(S(160), S(26));
            _btnPickDefaultOut.Location = new Point(S(566), S(330));
            _btnPickDefaultOut.FlatStyle = FlatStyle.System;
            _btnPickDefaultOut.Click += delegate { PickDefaultOut(); };
            _pSettings.Controls.Add(_btnPickDefaultOut);

            _lblSetSec2 = SectionLabel("关于", S(404));
            _pSettings.Controls.Add(_lblSetSec2);

            _lblAbout = new Label();
            _lblAbout.AutoSize = false;
            _lblAbout.Location = new Point(S(56), S(432));
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

        private void OnSameAsApkChanged()
        {
            _sameAsApk = _chkSameAsApk.Checked;
            Theme.SetSetting("same_as_apk", _sameAsApk ? "1" : "0");
            ApplySameAsApkState();
        }

        /// <summary>开关打开：默认导出路径的控件全部禁用并变灰；关闭：恢复</summary>
        private void ApplySameAsApkState()
        {
            bool on = _sameAsApk;
            if (_txtDefaultOut != null) _txtDefaultOut.Enabled = !on;
            if (_btnPickDefaultOut != null) _btnPickDefaultOut.Enabled = !on;
            if (_lblDefaultOut != null)
                _lblDefaultOut.ForeColor = on ? Theme.SubText : Theme.Text;
        }

        private void SaveDefaultOut()
        {
            if (_txtDefaultOut == null) return;
            string p = _txtDefaultOut.Text.Trim();
            if (p.Length > 0 && !Directory.Exists(p))
            {
                try { Directory.CreateDirectory(p); }
                catch { }
            }
            Theme.SetSetting("default_out", p);
        }

        private void PickDefaultOut()
        {
            if (_txtDefaultOut == null) return;
            string r = FolderPicker.Pick(Handle, _txtDefaultOut.Text.Trim());
            if (r != null)
            {
                _txtDefaultOut.Text = r;
                SaveDefaultOut();
            }
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
            RadioButton rb = new ThemeRadio();      // 自绘的：深色下小圆点才看得见
            rb.Text = text;
            // 不给 AutoSize：自绘控件的自动宽度是按系统那套圆点算的，会把「跟随系统主题」裁成「跟随系统…」
            rb.AutoSize = false;
            rb.Size = new Size(S(240), S(24));
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
                try
                {
                    ver = Application.ProductVersion;
                    // 有 InformationalVersion 时 ProductVersion 会带一段说明，只取前面的版本号
                    int sp = ver.IndexOf(' ');
                    if (sp > 0) ver = ver.Substring(0, sp);
                    // 1.0.2.0 → 1.0.2；1.0.0.0 → 1.0
                    while (ver.EndsWith(".0")) ver = ver.Substring(0, ver.Length - 2);
                }
                catch { }
                _lblAbout.Text = "RSB 提取器　版本 " + ver
                    + "\r\n外观设置存在：" + Theme.SettingsPath
                    + "\r\n（删掉那个文件就恢复默认：跟随系统）";
            }
            ApplySameAsApkState();
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

        // ============================================================ 主题
        /// <summary>把当前调色板刷到所有控件上（切主题、系统换主题时都走这里）</summary>
        private void ApplyTheme()
        {
            BackColor = Theme.Bg;
            if (_pImport != null) _pImport.BackColor = Theme.Bg;
            if (_pBrowse != null) _pBrowse.BackColor = Theme.Bg;
            if (_pExport != null) _pExport.BackColor = Theme.Bg;

            // 深色下系统原生按钮是白的，改成自绘扁平；浅色下保持系统原生
            Button[] btns = new Button[] { _btnImport, _btnGoExport, _btnPickDir, _btnDoExport, _btnPickDefaultOut };
            for (int i = 0; i < btns.Length; i++) { if (btns[i] != null) Theme.StyleButton(btns[i]); }
            Theme.StyleTextBox(_txtApkPath);
            Theme.StyleTextBox(_txtOut);
            if (_txtDefaultOut != null) Theme.StyleTextBox(_txtDefaultOut);
            if (_chkSameAsApk != null) _chkSameAsApk.ForeColor = Theme.Text;

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

            if (_pSettings != null)
            {
                _pSettings.BackColor = Theme.Bg;
                Label[] sl = new Label[] { _lblSetTitle, _lblSetSec1, _lblSetSec2, _lblSetSec3 };
                for (int i = 0; i < sl.Length; i++)
                {
                    if (sl[i] == null) continue;
                    sl[i].BackColor = Color.Transparent;
                    sl[i].ForeColor = Theme.Text;
                }
                Label[] sl2 = new Label[] { _lblThemeHint, _lblAbout };
                for (int i = 0; i < sl2.Length; i++)
                {
                    if (sl2[i] == null) continue;
                    sl2[i].BackColor = Color.Transparent;
                    sl2[i].ForeColor = Theme.SubText;
                }
                Theme.StyleChoice(_rbThemeAuto);
                Theme.StyleChoice(_rbThemeLight);
                Theme.StyleChoice(_rbThemeDark);
                Theme.StyleButton(_btnSettingsBack);
                ApplySameAsApkState();
            }
            Theme.StyleButton(_btnSettings);

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

            // 设置页里的那组单选按钮跟菜单是一份状态，一起同步
            _syncingTheme = true;
            try
            {
                if (_rbThemeAuto != null)
                {
                    _rbThemeAuto.Checked = (Theme.Current == Theme.Mode.Auto);
                    _rbThemeAuto.Enabled = Theme.SystemSupportsTheme;
                }
                if (_rbThemeLight != null) _rbThemeLight.Checked = (Theme.Current == Theme.Mode.Light);
                if (_rbThemeDark != null) _rbThemeDark.Checked = (Theme.Current == Theme.Mode.Dark);
                if (_lblThemeHint != null)
                {
                    // 只在"系统不支持"时才显示这行 —— 那是单选按钮变灰的原因，必须写出来；
                    // 支持的时候不啰嗦（用户要求删掉那句）。
                    _lblThemeHint.Visible = !Theme.SystemSupportsTheme;
                    if (!Theme.SystemSupportsTheme)
                    {
                        _lblThemeHint.Text = "本系统没有「深色模式」这个设置（Windows 10 1809 以上才有），"
                            + "所以「跟随系统主题」用不了、已经变灰，外观按浅色显示；下面「浅色 / 深色」两档随时可选。";
                    }
                }
            }
            finally { _syncingTheme = false; }
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

            // 图标框和文字框都问系统要（ItemBoundsPortion）：
            // 自己拿 e.Bounds 减图标高度算文字位置，算出来只剩几个像素，名字会被压成一条缝
            Image img = LargeIconOf(e.Item);
            if (img != null)
            {
                Rectangle ir = e.Item.GetBounds(ItemBoundsPortion.Icon);
                int x = ir.Left + (ir.Width - img.Width) / 2;
                int y = ir.Top + (ir.Height - img.Height) / 2;
                e.Graphics.DrawImage(img, x, y, img.Width, img.Height);
            }
            Rectangle tr = e.Item.GetBounds(ItemBoundsPortion.Label);
            // 上下沿用系统给的（正好一行），左右必须收进项矩形：
            // 系统给的那个框宽度 = 文字自然宽度，长名字会画到项外面去、跟邻居糊成一片
            tr = new Rectangle(e.Bounds.Left + S(2), tr.Top, e.Bounds.Width - S(4), tr.Height);
            if (tr.Height < S(8) || tr.Width < S(8))
                tr = new Rectangle(e.Bounds.Left + S(2), e.Bounds.Bottom - S(26), e.Bounds.Width - S(4), S(22));
            TextRenderer.DrawText(e.Graphics, e.Item.Text, _list.Font, tr, ListItemColor(e.Item),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        /// <summary>取列表项的大图标</summary>
        private Image LargeIconOf(ListViewItem it)
        {
            try
            {
                ImageList il = _list.LargeImageList;
                if (il == null) return null;
                if (!string.IsNullOrEmpty(it.ImageKey) && il.Images.ContainsKey(it.ImageKey))
                    return il.Images[it.ImageKey];
                if (it.ImageIndex >= 0 && it.ImageIndex < il.Images.Count)
                    return il.Images[it.ImageIndex];
            }
            catch { }
            return null;
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

            // 第 0 列的文字前面要画小图标。
            // 浅色是系统自绘的，图标本来就有；改成自绘之后系统不再帮忙画，就会"详细信息里没有小图标"
            if (e.ColumnIndex == 0)
            {
                Image img = SmallIconOf(e.Item);
                if (img != null)
                {
                    int top = e.Bounds.Top + (e.Bounds.Height - img.Height) / 2;
                    e.Graphics.DrawImage(img, e.Bounds.Left + S(2), top, img.Width, img.Height);
                    int shift = img.Width + S(4);
                    r.X += shift;
                    r.Width -= shift;
                }
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _list.Font, r, c, flags);
        }

        /// <summary>
        /// 自动化用：把列表视图的关键矩形写出来（大图标模式下的图标框/文字框最容易出错，
        /// 靠猜没用，直接看系统给的实际数字）。--dump-list 调用。
        /// </summary>
        public void DumpList(string path)
        {
            StringBuilder sb = new StringBuilder();
            string small = _list.SmallImageList == null ? "-" : (_list.SmallImageList.ImageSize.Width + "x" + _list.SmallImageList.ImageSize.Height);
            string large = _list.LargeImageList == null ? "-" : (_list.LargeImageList.ImageSize.Width + "x" + _list.LargeImageList.ImageSize.Height);
            sb.AppendLine("view=" + _list.View + "  client=" + _list.ClientSize.Width + "x" + _list.ClientSize.Height
                + "  smallImage=" + small + "  largeImage=" + large
                + "  labelWrap=" + _list.LabelWrap);
            for (int i = 0; i < _list.Items.Count && i < 14; i++)
            {
                ListViewItem it = _list.Items[i];
                Rectangle b = it.Bounds;
                Rectangle ic = it.GetBounds(ItemBoundsPortion.Icon);
                Rectangle lb = it.GetBounds(ItemBoundsPortion.Label);
                sb.AppendLine(string.Format(
                    "{0,2} {1,-36} bounds={2},{3} {4}x{5} | icon={6},{7} {8}x{9} | label={10},{11} {12}x{13}",
                    i, it.Text,
                    b.Left, b.Top, b.Width, b.Height,
                    ic.Left, ic.Top, ic.Width, ic.Height,
                    lb.Left, lb.Top, lb.Width, lb.Height));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>取列表项的小图标（ImageKey / ImageIndex 两种设法都兜住）</summary>
        private Image SmallIconOf(ListViewItem it)
        {
            try
            {
                ImageList il = it.ImageList;
                if (il == null) il = _list.SmallImageList;
                if (il == null) return null;
                if (!string.IsNullOrEmpty(it.ImageKey) && il.Images.ContainsKey(it.ImageKey))
                    return il.Images[it.ImageKey];
                if (it.ImageIndex >= 0 && it.ImageIndex < il.Images.Count)
                    return il.Images[it.ImageIndex];
            }
            catch { }
            return null;
        }

        // ============================================================ 小工具
        private static string FormatSize(long n)
        {
            if (n < 1024) return n + " B";
            if (n < 1024 * 1024) return (n / 1024.0).ToString("0.0") + " KB";
            if (n < 1024L * 1024 * 1024) return (n / 1048576.0).ToString("0.00") + " MB";
            return (n / 1073741824.0).ToString("0.00") + " GB";
        }

        private static string DescribeType(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith(".rsb.smf")) return "RSB 资源包";
            if (lower.EndsWith(".rton.smf")) return "RTON 数据";
            if (lower.EndsWith(".json.smf")) return "JSON 数据";
            if (lower.EndsWith(".xml.smf")) return "XML 数据";
            if (lower.EndsWith(".tag.smf")) return "校验标签";
            if (lower.EndsWith(".smf")) return "SMF";
            if (lower.EndsWith(".dex")) return "Dalvik 字节码";
            if (lower.EndsWith(".so")) return "动态库";
            if (lower.EndsWith(".png")) return "图片";
            if (lower.EndsWith(".xml")) return "XML";
            if (lower.EndsWith(".json")) return "JSON";
            if (lower.EndsWith(".arsc")) return "资源表";
            if (lower.EndsWith(".apk")) return "内嵌 APK";
            if (lower.EndsWith(".jar")) return "Java 库";
            if (lower.EndsWith(".ttf") || lower.EndsWith(".otf")) return "字体";
            if (lower.EndsWith(".txt") || lower.EndsWith(".ini") || lower.EndsWith(".config")) return "文本";
            return "文件";
        }
    }
}
