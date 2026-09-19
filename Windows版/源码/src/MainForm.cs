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

        public MainForm() : this(null, false, null, 0, null, null) { }

        public MainForm(string autoOpen, bool gotoExport, string autoOut, int autoMulti, string autoView, string autoDir)
        {
            using (Graphics g = CreateGraphics()) _scale = g.DpiX / 96f;
            if (_scale < 1f) _scale = 1f;

            Text = "APK 提取工具";
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(940), S(600));
            MinimumSize = new Size(S(720), S(460));
            // XP 上没有"微软雅黑"，字体族不存在时 new Font 可能抛异常，兜一下
            try { Font = new Font("Microsoft YaHei UI", 9f); }
            catch { Font = SystemFonts.DefaultFont; }
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildImportPanel();
            BuildBrowsePanel();
            BuildExportPanel();
            ShowImport();

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
            _pImport.BackColor = Color.White;
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
            if (ok) _pImport.BackColor = Color.FromArgb(236, 244, 255);   // 拖进来时给点视觉反馈
        }

        private void OnImportDragLeave(object sender, EventArgs e)
        {
            _pImport.BackColor = Color.White;
        }

        private void OnImportDragDrop(object sender, DragEventArgs e)
        {
            _pImport.BackColor = Color.White;
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
            _pBrowse.BackColor = Color.White;
            _pBrowse.Visible = false;

            // ---- 底部状态条（先加，Dock 顺序才对）----
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = S(50);
            bottom.BackColor = Color.FromArgb(247, 248, 250);
            bottom.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Color.FromArgb(224, 227, 232)))
                    e.Graphics.DrawLine(p, 0, 0, bottom.ClientSize.Width, 0);
            };

            _lblCount = new Label();
            _lblCount.AutoSize = true;
            _lblCount.Location = new Point(S(14), S(17));
            _lblCount.ForeColor = Color.FromArgb(100, 106, 116);
            bottom.Controls.Add(_lblCount);

            _lblSelInfo = new Label();
            _lblSelInfo.AutoSize = true;
            _lblSelInfo.ForeColor = Color.FromArgb(40, 90, 175);
            bottom.Controls.Add(_lblSelInfo);

            _btnGoExport = new Button();
            _btnGoExport.Text = "导出smf文件";
            _btnGoExport.Size = new Size(S(130), S(32));
            _btnGoExport.FlatStyle = FlatStyle.System;
            _btnGoExport.Enabled = false;                       // 平时灰着
            _btnGoExport.Click += delegate { OnGoExportClick(); };
            bottom.Controls.Add(_btnGoExport);
            bottom.Resize += delegate
            {
                _btnGoExport.Left = bottom.ClientSize.Width - _btnGoExport.Width - S(14);
                _btnGoExport.Top = (bottom.ClientSize.Height - _btnGoExport.Height) / 2;
                _lblSelInfo.Left = _btnGoExport.Left - _lblSelInfo.Width - S(16);
                _lblSelInfo.Top = (bottom.ClientSize.Height - _lblSelInfo.Height) / 2;
            };

            // ---- 顶部导航栏 ----
            _navBar = new ToolStrip();
            _navBar.Dock = DockStyle.Top;
            _navBar.AutoSize = false;
            _navBar.Height = S(38);
            _navBar.GripStyle = ToolStripGripStyle.Hidden;
            _navBar.Padding = new Padding(S(8), S(5), S(8), S(5));
            _navBar.RenderMode = ToolStripRenderMode.System;
            _navBar.BackColor = Color.FromArgb(250, 251, 252);
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
            _list.Columns.Add("名称", S(340), HorizontalAlignment.Left);
            _list.Columns.Add("大小", S(110), HorizontalAlignment.Right);
            _list.Columns.Add("类型", S(150), HorizontalAlignment.Left);
            _list.ItemSelectionChanged += delegate { UpdateExportButton(); };
            _list.DoubleClick += delegate { OnListDoubleClick(); };
            _list.Resize += delegate { AdjustColumns(); };
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
            _pExport.BackColor = Color.White;
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
            _lblStatus.ForeColor = Color.FromArgb(90, 96, 106);
            _pExport.Controls.Add(_lblStatus);

            Controls.Add(_pExport);
        }

        // ============================================================ 切换
        private void ShowImport()
        {
            _pImport.Visible = true;
            _pBrowse.Visible = false;
            _pExport.Visible = false;
        }

        private void ShowBrowse()
        {
            _pImport.Visible = false;
            _pBrowse.Visible = true;
            _pExport.Visible = false;
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
                    if (!c.Entry.IsSmf) it.ForeColor = Color.FromArgb(170, 175, 182);   // 非 .smf 淡显
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
                    sep.ForeColor = Color.FromArgb(150, 156, 166);
                    sep.Margin = new Padding(S(2), S(7), S(2), 0);
                    _navBar.Items.Insert(at++, sep);
                    _crumbCount++;
                }
                ApkNode target = chain[i];
                string tip = target.FullPath.Length == 0 ? "APK 根目录" : target.FullPath;
                ToolStripButton b = NavButton(target.Name, tip, delegate { NavigateTo(target); });
                bool last = (i == chain.Count - 1);
                b.ForeColor = last ? Color.FromArgb(25, 30, 38) : Color.FromArgb(50, 100, 185);
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
                try { _txtOut.Text = Path.GetDirectoryName(_apk.Path); }
                catch { }
            }
            _lblStatus.Text = "";
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
            _lblStatus.Text = "正在导出，请稍候…";
            Application.DoEvents();

            string outDir = dir;
            Thread t = new Thread(new ThreadStart(delegate
            {
                List<ApkEntry> list = new List<ApkEntry>(_picked);
                ExportOutcome outcome = Exporter.Export(_apk, list, outDir,
                    delegate(int idx, int total, string name)
                    {
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            _lblStatus.Text = "正在导出… " + idx + " / " + total + "　" + name;
                        }));
                    });

                BeginInvoke(new MethodInvoker(delegate
                {
                    _btnDoExport.Enabled = true;
                    _btnPickDir.Enabled = true;
                    _lblStatus.Text = "";

                    string msg = "导出完成\n\n成功 " + outcome.Success + " 个，失败 " + outcome.Failed + " 个";
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
