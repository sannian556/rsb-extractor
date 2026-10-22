package com.apksmf.extractor;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.res.Configuration;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.provider.Settings;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.ProgressBar;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.TextView;
import android.widget.Toast;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.channels.FileChannel;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * RSB 提取器（安卓版）
 *
 * 页面：
 *   1) 导入      —— 按钮 + 可输入的地址栏（粘贴路径回车即导入）+ 设置
 *   2) 选 APK    —— 自己写的文件浏览器（像 MT 管理器那样直接浏览手机目录，不走系统 SAF）
 *   3) APK 内容  —— 浏览包里的文件夹 / 文件，多选 .smf
 *   4) 选输出    —— 同样的浏览器，底部一个「选择此文件夹」
 *   5) 导出      —— 进度 + 结果
 *   6) 设置      —— 浅色 / 深色 / 跟随系统
 *
 * 为什么自己做文件浏览器：系统那个 SAF 选择器要先点"最近"→侧栏→下载→选文件，
 * 好几步；自己写一个直接看目录，一步到位。
 */
public class MainActivity extends Activity {

    // ---------------- 主题 ----------------
    private static final String PREFS = "apksmf";
    private static final String KEY_THEME = "theme";     // system / light / dark
    private static final int THEME_SYSTEM = 0, THEME_LIGHT = 1, THEME_DARK = 2;
    private int themeMode = THEME_SYSTEM;
    private boolean dark = false;

    // ---------------- 页面 ----------------
    private View pageImport, pageFsApk, pageBrowse, pageFsDir, pageExport, pageSettings;
    private final List<View> pageStack = new ArrayList<View>();

    private Button btnPickApk, btnSettings;
    private EditText edtApkPath;

    private Button btnFsApkBack;
    private EditText edtFsApkPath;
    private ListView listFsApk;
    private TextView txtFsApkInfo;
    private View rowFsApkTop;
    private Button btnFsApkCancel, btnFsApkUp;

    private Button btnGoExport;
    private TextView txtCurPath, txtSelInfo;
    private ListView listFiles;
    private ArrayAdapter<FsNode> listFilesAdapter;   // 留着它：只改选中状态时用 notifyDataSetChanged，重建 adapter 会把列表滚回顶部

    private Button btnFsDirBack, btnUseDir;
    private EditText edtFsDirPath;
    private ListView listFsDir;
    private TextView txtFsDirInfo;
    private View rowFsDirTop;
    private Button btnFsDirCancel, btnFsDirUp;

    private TextView txtExportHead, txtOutDir, txtExportStatus;
    private Button btnPickDir, btnDoExport, btnBack;
    private ProgressBar progressBar;

    private RadioGroup rgTheme;
    private RadioButton rbThemeSystem, rbThemeLight, rbThemeDark;
    private Button btnGrant, btnSettingsBack;
    private TextView txtVersion;

    // ---------------- 数据 ----------------
    private final ApkIndex apk = new ApkIndex();
    private String apkDisplayName = "";
    private FsNode root, cur;
    private final List<FsNode> shown = new ArrayList<FsNode>();
    private final Set<FsNode> picked = new HashSet<FsNode>();
    private File outDir;
    private File fsCur;                 // 文件浏览器当前目录
    private boolean lastDirUnreadable;  // 上一次 readDir 的目录是不是根本打不开
    private int pendingAction = 0;      // 1=想选 apk，2=想选输出目录（等权限中）
    private final Handler ui = new Handler();

    /** APK 内部虚拟目录节点 */
    static class FsNode {
        String name, full;
        boolean isDir;
        ApkEntry entry;
        FsNode parent;
        FsNode link;                  // 非 null = 点它是"跳去某个目录"（就是列表第一行的「返回上级」）
        final List<FsNode> children = new ArrayList<FsNode>();
    }

    /** 手机文件系统的列表项 */
    static class FsItem {
        File file;
        boolean isDir;
        long size;
        boolean isUp;                  // true = 列表第一行的「↑ 返回上级」
        FsItem(File f) {
            file = f;
            isDir = f.isDirectory();
            size = isDir ? 0 : f.length();
        }
        static FsItem up(File parent) {
            FsItem it = new FsItem(parent);
            it.isDir = true;
            it.isUp = true;
            return it;
        }
    }

    // ============================================================ 生命周期
    @Override
    protected void onCreate(Bundle b) {
        SharedPreferences sp = getSharedPreferences(PREFS, MODE_PRIVATE);
        themeMode = sp.getInt(KEY_THEME, THEME_SYSTEM);
        dark = computeDark();
        setTheme(dark ? R.style.AppThemeDark : R.style.AppThemeLight);   // 必须在 super 之前
        super.onCreate(b);

        setContentView(R.layout.activity_main);
        applySystemBarInsets();     // Android 15+ 强制"边到边"，得自己给状态栏留位
        bindViews();
        wireEvents();

        rbThemeSystem.setChecked(themeMode == THEME_SYSTEM);
        rbThemeLight.setChecked(themeMode == THEME_LIGHT);
        rbThemeDark.setChecked(themeMode == THEME_DARK);
        txtVersion.setText("版本 " + versionText());

        showPage(pageImport, false);
    }

    /**
     * Android 15（API 35）起，targetSdk 35 的应用被强制"边到边"显示：内容会一直画到状态栏底下，
     * 框架自带的 ActionBar 还会压在内容上面 —— 用户手机上顶部那一条（← + 地址栏）整条看不见，
     * 就是这么来的。现在 ActionBar 已经去掉（主题改成 NoActionBar），这里再按 window insets
     * 自己留出状态栏/导航栏的占位。**API 35 以下系统本来就会让开，这里什么都不做。**
     */
    private void applySystemBarInsets() {
        if (Build.VERSION.SDK_INT < 35) return;
        final View root = findViewById(R.id.rootLayout);
        if (root == null) return;
        final int sb = statusBarHeightPx();
        root.setPadding(0, sb, 0, 0);          // 先垫上状态栏高度，保证顶栏一定露出来
        root.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            public WindowInsets onApplyWindowInsets(View v, WindowInsets ins) {
                int top = ins.getSystemWindowInsetTop();
                v.setPadding(0, top > 0 ? top : sb, 0, ins.getSystemWindowInsetBottom());
                return ins;
            }
        });
    }

    private int statusBarHeightPx() {
        int id = getResources().getIdentifier("status_bar_height", "dimen", "android");
        return id > 0 ? getResources().getDimensionPixelSize(id) : 0;
    }

    /** 版本号从包信息里读，改 manifest 就会跟着变 */
    private String versionText() {
        try {
            android.content.pm.PackageInfo pi = getPackageManager().getPackageInfo(getPackageName(), 0);
            return pi.versionName;
        } catch (Throwable t) {
            return "1.0";
        }
    }

    private boolean computeDark() {
        if (themeMode == THEME_LIGHT) return false;
        if (themeMode == THEME_DARK) return true;
        int mode = getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
        return mode == Configuration.UI_MODE_NIGHT_YES;
    }

    private void bindViews() {
        pageImport = findViewById(R.id.pageImport);
        pageFsApk = findViewById(R.id.pageFsApk);
        pageBrowse = findViewById(R.id.pageBrowse);
        pageFsDir = findViewById(R.id.pageFsDir);
        pageExport = findViewById(R.id.pageExport);
        pageSettings = findViewById(R.id.pageSettings);

        btnPickApk = (Button) findViewById(R.id.btnPickApk);
        edtApkPath = (EditText) findViewById(R.id.edtApkPath);
        btnSettings = (Button) findViewById(R.id.btnSettings);

        btnFsApkBack = (Button) findViewById(R.id.btnFsApkBack);
        edtFsApkPath = (EditText) findViewById(R.id.edtFsApkPath);
        listFsApk = (ListView) findViewById(R.id.listFsApk);
        txtFsApkInfo = (TextView) findViewById(R.id.txtFsApkInfo);
        rowFsApkTop = findViewById(R.id.rowFsApkTop);
        btnFsApkCancel = (Button) findViewById(R.id.btnFsApkCancel);
        btnFsApkUp = (Button) findViewById(R.id.btnFsApkUp);

        txtCurPath = (TextView) findViewById(R.id.txtCurPath);
        listFiles = (ListView) findViewById(R.id.listFiles);
        txtSelInfo = (TextView) findViewById(R.id.txtSelInfo);
        btnGoExport = (Button) findViewById(R.id.btnGoExport);

        btnFsDirBack = (Button) findViewById(R.id.btnFsDirBack);
        edtFsDirPath = (EditText) findViewById(R.id.edtFsDirPath);
        listFsDir = (ListView) findViewById(R.id.listFsDir);
        txtFsDirInfo = (TextView) findViewById(R.id.txtFsDirInfo);
        btnUseDir = (Button) findViewById(R.id.btnUseDir);
        rowFsDirTop = findViewById(R.id.rowFsDirTop);
        btnFsDirCancel = (Button) findViewById(R.id.btnFsDirCancel);
        btnFsDirUp = (Button) findViewById(R.id.btnFsDirUp);

        txtExportHead = (TextView) findViewById(R.id.txtExportHead);
        btnPickDir = (Button) findViewById(R.id.btnPickDir);
        txtOutDir = (TextView) findViewById(R.id.txtOutDir);
        progressBar = (ProgressBar) findViewById(R.id.progressBar);
        txtExportStatus = (TextView) findViewById(R.id.txtExportStatus);
        btnDoExport = (Button) findViewById(R.id.btnDoExport);
        btnBack = (Button) findViewById(R.id.btnBackToImport);

        rgTheme = (RadioGroup) findViewById(R.id.rgTheme);
        rbThemeSystem = (RadioButton) findViewById(R.id.rbThemeSystem);
        rbThemeLight = (RadioButton) findViewById(R.id.rbThemeLight);
        rbThemeDark = (RadioButton) findViewById(R.id.rbThemeDark);
        btnGrant = (Button) findViewById(R.id.btnGrant);
        btnSettingsBack = (Button) findViewById(R.id.btnSettingsBack);
        txtVersion = (TextView) findViewById(R.id.txtVersion);
    }

    private void wireEvents() {
        btnPickApk.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { openApkPicker(); }
        });
        btnSettings.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { showPage(pageSettings, true); }
        });
        btnSettingsBack.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { back(); }
        });
        edtApkPath.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            public boolean onEditorAction(TextView v, int actionId, android.view.KeyEvent e) {
                String p = edtApkPath.getText().toString().trim();
                if (p.length() > 0) { tryOpenApk(p); return true; }
                return false;
            }
        });

        // ---- 选 APK 的文件浏览器 ----
        btnFsApkBack.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { back(); }
        });
        btnFsApkCancel.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { back(); }
        });
        btnFsApkUp.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { goParent(); }
        });
        edtFsApkPath.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            public boolean onEditorAction(TextView v, int actionId, android.view.KeyEvent e) {
                gotoFsDir(edtFsApkPath.getText().toString().trim());
                return true;
            }
        });
        listFsApk.setOnItemClickListener(new AdapterView.OnItemClickListener() {
            public void onItemClick(AdapterView<?> p, View v, int pos, long id) {
                Object o = listFsApk.getTag();
                List<FsItem> items = (List<FsItem>) o;
                if (pos < 0 || pos >= items.size()) return;
                FsItem it = items.get(pos);
                if (it.isDir) {
                    fsCur = it.file;
                    loadFsApkList();
                } else {
                    if (it.file.getName().toLowerCase().endsWith(".apk")) {
                        tryOpenApk(it.file.getAbsolutePath());
                    } else {
                        toast("这不是 .apk 文件");
                    }
                }
            }
        });

        // ---- 选输出目录的文件浏览器 ----
        btnFsDirBack.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { back(); }
        });
        btnFsDirCancel.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { back(); }
        });
        btnFsDirUp.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { goParent(); }
        });
        edtFsDirPath.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            public boolean onEditorAction(TextView v, int actionId, android.view.KeyEvent e) {
                gotoFsDir(edtFsDirPath.getText().toString().trim());
                return true;
            }
        });
        listFsDir.setOnItemClickListener(new AdapterView.OnItemClickListener() {
            public void onItemClick(AdapterView<?> p, View v, int pos, long id) {
                Object o = listFsDir.getTag();
                List<FsItem> items = (List<FsItem>) o;
                if (pos < 0 || pos >= items.size()) return;
                FsItem it = items.get(pos);
                if (it.isDir) {
                    fsCur = it.file;
                    loadFsDirList();
                }
            }
        });
        btnUseDir.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                if (fsCur == null) return;
                outDir = fsCur;
                txtOutDir.setText(outDir.getAbsolutePath());
                back();
            }
        });

        // ---- APK 内容浏览 ----
        btnGoExport.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { gotoExport(); }
        });
        listFiles.setOnItemClickListener(new AdapterView.OnItemClickListener() {
            public void onItemClick(AdapterView<?> p, View v, int pos, long id) {
                if (pos < 0 || pos >= shown.size()) return;
                FsNode n = shown.get(pos);
                if (n.link != null) {                    // 第一行的「↑ 返回上级」
                    cur = n.link;
                    refreshList();
                } else if (n.isDir) {
                    cur = n;
                    refreshList();
                } else if (n.entry != null && n.entry.isSmf()) {
                    if (picked.contains(n)) picked.remove(n); else picked.add(n);
                    // ★ 只刷新这一个列表的内容，**不要重建 adapter** ——
                    //   重建会让 ListView 滚回顶部，用户选完第 1 个文件就得重新往下划
                    if (listFilesAdapter != null) listFilesAdapter.notifyDataSetChanged();
                    else refreshList();
                    updateSelInfo();
                } else {
                    toast("这个不是 .smf 文件，不能导出");
                }
            }
        });

        // ---- 导出 ----
        btnPickDir.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { openDirPicker(); }
        });
        btnDoExport.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { doExport(); }
        });
        btnBack.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { backToImport(); }
        });

        // ---- 设置 ----
        rgTheme.setOnCheckedChangeListener(new RadioGroup.OnCheckedChangeListener() {
            public void onCheckedChanged(RadioGroup g, int id) {
                int m = id == R.id.rbThemeLight ? THEME_LIGHT
                        : id == R.id.rbThemeDark ? THEME_DARK : THEME_SYSTEM;
                if (m != themeMode) {
                    themeMode = m;
                    getSharedPreferences(PREFS, MODE_PRIVATE).edit().putInt(KEY_THEME, m).commit();
                    recreate();     // 换主题要重建界面
                }
            }
        });
        btnGrant.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) { ensureStorage(); updateGrantButton(); }
        });
    }

    @Override
    public void onBackPressed() {
        // 包内容页里：返回键 = 回上一层文件夹（跟 MT 管理器一样），在包根目录才真的退出
        if (pageBrowse.getVisibility() == View.VISIBLE && cur != null && cur.parent != null) {
            cur = cur.parent;
            refreshList();
            return;
        }
        if (!back()) super.onBackPressed();
    }

    // ============================================================ 页面切换
    private void showPage(View target, boolean push) {
        View[] all = new View[] { pageImport, pageFsApk, pageBrowse, pageFsDir, pageExport, pageSettings };
        for (int i = 0; i < all.length; i++) all[i].setVisibility(all[i] == target ? View.VISIBLE : View.GONE);
        if (push) pageStack.add(target);
    }

    private boolean back() {
        if (pageStack.isEmpty()) { return false; }
        pageStack.remove(pageStack.size() - 1);
        View target = pageStack.isEmpty() ? pageImport : pageStack.get(pageStack.size() - 1);
        showPage(target, false);
        return true;
    }

    private void backToImport() {
        closeApk();
        picked.clear();
        shown.clear();
        root = null;
        cur = null;
        outDir = null;
        pageStack.clear();
        edtApkPath.setText("");
        txtOutDir.setText("");
        txtExportStatus.setText("");
        txtSelInfo.setText("");
        btnGoExport.setEnabled(false);
        progressBar.setProgress(0);
        showPage(pageImport, false);
    }

    // ============================================================ 存储权限
    private boolean hasStorage() {
        if (Build.VERSION.SDK_INT >= 30) {
            return Environment.isExternalStorageManager();
        }
        return checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED;
    }

    private boolean ensureStorage() {
        if (hasStorage()) return true;
        if (Build.VERSION.SDK_INT >= 30) {
            new AlertDialog.Builder(this)
                    .setTitle("需要存储权限")
                    .setMessage("要像文件管理器那样浏览手机里的 apk，需要授予「所有文件访问」权限。\n\n" +
                            "点「去授权」后，在系统设置里找到本应用，把「允许访问所有文件」打开。")
                    .setPositiveButton("去授权", new DialogInterface.OnClickListener() {
                        public void onClick(DialogInterface d, int w) {
                            try {
                                Intent i = new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION);
                                i.setData(Uri.parse("package:" + getPackageName()));
                                startActivity(i);
                            } catch (Exception ex) {
                                try {
                                    startActivity(new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION));
                                } catch (Exception ex2) {
                                    toast("打不开系统设置，请手动去 设置→应用→" + getString(R.string.app_name));
                                }
                            }
                        }
                    })
                    .setNegativeButton("取消", null)
                    .show();
        } else {
            requestPermissions(new String[] {
                    Manifest.permission.READ_EXTERNAL_STORAGE,
                    Manifest.permission.WRITE_EXTERNAL_STORAGE }, 1001);
        }
        return false;
    }

    @Override
    public void onRequestPermissionsResult(int req, String[] perms, int[] res) {
        super.onRequestPermissionsResult(req, perms, res);
        boolean ok = res.length > 0 && res[0] == PackageManager.PERMISSION_GRANTED;
        if (ok) {
            resumePending();
        } else {
            toast("没有存储权限，就用不了文件浏览器");
        }
        updateGrantButton();
    }

    /** 用户刚才想做但被权限挡住的事，拿到权限后自动接着做（省得再点一次） */
    private void resumePending() {
        if (pendingAction == 0 || !hasStorage()) return;
        int a = pendingAction;
        pendingAction = 0;
        if (a == 1) openApkPickerNow();
        else if (a == 2) openDirPickerNow();
    }

    private void updateGrantButton() {
        if (btnGrant == null) return;
        boolean ok = hasStorage();
        btnGrant.setText(ok ? "存储权限：已授予 ✓" : "存储权限：未授予（点这里授权）");
    }

    // ============================================================ 选 APK
    private void openApkPicker() {
        if (!hasStorage()) { pendingAction = 1; ensureStorage(); return; }
        openApkPickerNow();
    }

    private void openApkPickerNow() {
        fsCur = Environment.getExternalStorageDirectory();
        showPage(pageFsApk, true);
        loadFsApkList();
    }

    private void openDirPicker() {
        if (!hasStorage()) { pendingAction = 2; ensureStorage(); return; }
        openDirPickerNow();
    }

    private void openDirPickerNow() {
        fsCur = (outDir != null && outDir.isDirectory()) ? outDir : Environment.getExternalStorageDirectory();
        showPage(pageFsDir, true);
        loadFsDirList();
    }

    /** 「↑ 上级」按钮：去上一层目录（两个浏览器共用） */
    private void goParent() {
        File up = (fsCur == null) ? null : fsCur.getParentFile();
        if (up == null) { toast("已经在最上层了"); return; }
        fsCur = up;
        if (pageFsApk.getVisibility() == View.VISIBLE) loadFsApkList();
        else if (pageFsDir.getVisibility() == View.VISIBLE) loadFsDirList();
    }

    private void gotoFsDir(String path) {
        if (path == null || path.length() == 0) return;
        File f = new File(path);
        if (!f.exists()) { toast("路径不存在：" + path); return; }
        if (f.isFile()) f = f.getParentFile();
        if (f == null || !f.isDirectory()) { toast("这不是一个文件夹"); return; }
        fsCur = f;
        if (pageFsApk.getVisibility() == View.VISIBLE) loadFsApkList();
        else if (pageFsDir.getVisibility() == View.VISIBLE) loadFsDirList();
    }

    /**
     * 读一个目录。返回的列表里，**第 0 项是「↑ 返回上级」**（只要它还有上一级），
     * 这样"去上一层"永远看得见、点得到 —— 像 MT 管理器那样，
     * 不依赖顶部那一条地址栏（有的机型/版本上那一行可能被挤掉）。
     */
    private List<FsItem> readDir(File dir, boolean dirsOnly) {
        List<FsItem> out = new ArrayList<FsItem>();
        File[] fs = dir.listFiles();
        lastDirUnreadable = (fs == null);            // 打不开（没权限）和"空文件夹"要分清楚
        if (fs != null) {
            for (int i = 0; i < fs.length; i++) {
                File f = fs[i];
                String n = f.getName();
                if (n.startsWith(".")) continue;              // 不显示隐藏文件
                if (dirsOnly && !f.isDirectory()) continue;
                if (!f.canRead()) continue;
                out.add(new FsItem(f));
            }
            Collections.sort(out, new Comparator<FsItem>() {
                public int compare(FsItem a, FsItem b) {
                    if (a.isDir != b.isDir) return a.isDir ? -1 : 1;
                    return a.file.getName().compareToIgnoreCase(b.file.getName());
                }
            });
        }
        File parent = dir.getParentFile();
        if (parent != null) out.add(0, FsItem.up(parent));    // 排序完再插，免得被排到中间
        return out;
    }

    /** 顶部那一条（返回按钮 + 地址栏）按当前主题上色，保证任何主题下都看得清 */
    private void paintFsTop(View row, Button back, EditText path) {
        if (row == null) return;
        row.setBackgroundColor(dark ? 0xFF1B1E24 : 0xFFEDF0F4);
        back.setTextColor(dark ? 0xFFE8EAEE : 0xFF1A1F28);
        path.setTextColor(dark ? 0xFFE8EAEE : 0xFF1A1F28);
        path.setHintTextColor(dark ? 0xFF6E747E : 0xFFAAB0B8);
    }

    /** 列表里实际的条目数（不算那条「↑ 返回上级」） */
    private static int realCount(List<FsItem> items) {
        int n = items.size();
        if (n > 0 && items.get(0).isUp) n--;
        return n;
    }

    private void loadFsApkList() {
        if (fsCur == null) fsCur = Environment.getExternalStorageDirectory();
        final List<FsItem> items = readDir(fsCur, false);
        edtFsApkPath.setText(fsCur.getAbsolutePath());
        listFsApk.setTag(items);

        int apks = 0, dirs = 0;
        for (int i = 0; i < items.size(); i++) {
            FsItem it = items.get(i);
            if (it.isUp) continue;                       // 「↑ 返回上级」不算进统计
            if (it.isDir) dirs++;
            else if (it.file.getName().toLowerCase().endsWith(".apk")) apks++;
        }
        String info = fsCur.getAbsolutePath() + "\n" + dirs + " 个文件夹，" + realCount(items)
                + " 项（其中 apk " + apks + " 个）";
        if (realCount(items) == 0) {
            info += "\n" + (lastDirUnreadable
                    ? "（这个文件夹读不到内容 —— 可能没权限，返回上一级换一个）"
                    : "（这个文件夹里没有东西）");
        }
        txtFsApkInfo.setText(info);
        paintFsTop(rowFsApkTop, btnFsApkBack, edtFsApkPath);
        btnFsApkUp.setEnabled(fsCur.getParentFile() != null);

        final int cDir = dark ? 0xFFE8EAEE : 0xFF1A1F28;
        final int cGray = dark ? 0xFF6E747E : 0xFFAAB0B8;
        final int cApk = dark ? 0xFF7FB2FF : 0xFF1B5FD9;
        final int cBg = dark ? 0xFF14161A : 0xFFFFFFFF;
        final int cUp = dark ? 0xFF8FC0FF : 0xFF0B57D0;
        final int cUpBg = dark ? 0xFF1B2430 : 0xFFE8F1FF;

        listFsApk.setAdapter(new ArrayAdapter<FsItem>(this, android.R.layout.simple_list_item_2,
                android.R.id.text1, items) {
            @Override
            public View getView(int position, View convertView, ViewGroup parent) {
                View v = super.getView(position, convertView, parent);
                TextView t1 = (TextView) v.findViewById(android.R.id.text1);
                TextView t2 = (TextView) v.findViewById(android.R.id.text2);
                FsItem it = items.get(position);
                if (it.isUp) {
                    t1.setText("↑  返回上级");
                    t1.setTextColor(cUp);
                    t2.setText(it.file.getAbsolutePath());
                    t2.setTextColor(cUp);
                    v.setBackgroundColor(cUpBg);
                    return v;
                }
                v.setBackgroundColor(cBg);
                if (it.isDir) {
                    t1.setText("📁  " + it.file.getName());
                    t1.setTextColor(cDir);
                    t2.setText("文件夹");
                    t2.setTextColor(cGray);
                } else {
                    boolean isApk = it.file.getName().toLowerCase().endsWith(".apk");
                    t1.setText("📄  " + it.file.getName());
                    t1.setTextColor(isApk ? cApk : cGray);
                    t2.setText(ApkIndex.formatSize(it.size) + (isApk ? "    点一下就导入" : ""));
                    t2.setTextColor(cGray);
                }
                return v;
            }
        });
    }

    private void loadFsDirList() {
        if (fsCur == null) fsCur = Environment.getExternalStorageDirectory();
        final List<FsItem> items = readDir(fsCur, true);
        edtFsDirPath.setText(fsCur.getAbsolutePath());
        listFsDir.setTag(items);
        String dinfo = "将导出到：" + fsCur.getAbsolutePath() + "\n" + realCount(items) + " 个文件夹";
        if (realCount(items) == 0) {
            dinfo += lastDirUnreadable ? "（读不到内容，返回上一级换一个）"
                                       : "（没有子文件夹，可以直接点右下角「选择此文件夹」）";
        }
        txtFsDirInfo.setText(dinfo);
        paintFsTop(rowFsDirTop, btnFsDirBack, edtFsDirPath);
        btnFsDirUp.setEnabled(fsCur.getParentFile() != null);

        final int cDir = dark ? 0xFFE8EAEE : 0xFF1A1F28;
        final int cGray = dark ? 0xFF6E747E : 0xFFAAB0B8;
        final int cBg = dark ? 0xFF14161A : 0xFFFFFFFF;
        final int cUp = dark ? 0xFF8FC0FF : 0xFF0B57D0;
        final int cUpBg = dark ? 0xFF1B2430 : 0xFFE8F1FF;

        listFsDir.setAdapter(new ArrayAdapter<FsItem>(this, android.R.layout.simple_list_item_2,
                android.R.id.text1, items) {
            @Override
            public View getView(int position, View convertView, ViewGroup parent) {
                View v = super.getView(position, convertView, parent);
                TextView t1 = (TextView) v.findViewById(android.R.id.text1);
                TextView t2 = (TextView) v.findViewById(android.R.id.text2);
                FsItem it = items.get(position);
                if (it.isUp) {
                    t1.setText("↑  返回上级");
                    t1.setTextColor(cUp);
                    t2.setText(it.file.getAbsolutePath());
                    t2.setTextColor(cUp);
                    v.setBackgroundColor(cUpBg);
                    return v;
                }
                v.setBackgroundColor(cBg);
                t1.setText("📁  " + it.file.getName());
                t1.setTextColor(cDir);
                t2.setText("文件夹");
                t2.setTextColor(cGray);
                return v;
            }
        });
    }

    // ============================================================ 打开 APK
    private void tryOpenApk(final String path) {
        File f = new File(path);
        if (!f.exists() || !f.isFile()) {
            toast("找不到这个文件：" + path);
            return;
        }
        edtApkPath.setText(path);
        new Thread(new Runnable() {
            public void run() {
                String err = null;
                try {
                    closeApk();
                    java.io.FileInputStream fis = new java.io.FileInputStream(path);
                    FileChannel ch = fis.getChannel();      // ApkIndex 会一直持着它读数据，不能关
                    apk.open(ch, new File(path).length());
                    apkDisplayName = new File(path).getName();
                    buildTree();
                } catch (final Throwable ex) {
                    err = (ex.getMessage() == null) ? ex.getClass().getSimpleName() : ex.getMessage();
                }
                final String e2 = err;
                ui.post(new Runnable() {
                    public void run() {
                        if (e2 != null) {
                            new AlertDialog.Builder(MainActivity.this)
                                    .setTitle("打开失败").setMessage(e2)
                                    .setPositiveButton("知道了", null).show();
                            return;
                        }
                        cur = root;
                        picked.clear();
                        pageStack.clear();
                        pageStack.add(pageImport);
                        showPage(pageBrowse, true);
                        refreshList();
                        updateSelInfo();
                    }
                });
            }
        }).start();
    }

    private void buildTree() throws IOException {
        root = new FsNode();
        root.name = apkDisplayName;
        root.full = "";
        root.isDir = true;

        java.util.Map<String, FsNode> dirs = new java.util.HashMap<String, FsNode>();
        dirs.put("", root);

        List<ApkEntry> list = apk.entries();
        for (int i = 0; i < list.size(); i++) {
            ApkEntry e = list.get(i);
            String[] parts = e.name.split("/");
            String acc = "";
            FsNode parent = root;
            for (int k = 0; k < parts.length; k++) {
                if (parts[k].length() == 0) continue;
                acc = acc.length() == 0 ? parts[k] : (acc + "/" + parts[k]);
                boolean last = (k == parts.length - 1);
                if (last) {
                    FsNode n = new FsNode();
                    n.name = parts[k];
                    n.full = acc;
                    n.isDir = false;
                    n.entry = e;
                    n.parent = parent;
                    parent.children.add(n);
                } else {
                    FsNode d = dirs.get(acc);
                    if (d == null) {
                        d = new FsNode();
                        d.name = parts[k];
                        d.full = acc;
                        d.isDir = true;
                        d.parent = parent;
                        parent.children.add(d);
                        dirs.put(acc, d);
                    }
                    parent = d;
                }
            }
        }
        sortTree(root);
    }

    private static void sortTree(FsNode n) {
        Collections.sort(n.children, new Comparator<FsNode>() {
            public int compare(FsNode a, FsNode b) {
                if (a.isDir != b.isDir) return a.isDir ? -1 : 1;
                return a.name.compareToIgnoreCase(b.name);
            }
        });
        for (int i = 0; i < n.children.size(); i++) sortTree(n.children.get(i));
    }

    private void refreshList() {
        if (cur == null) return;
        shown.clear();
        if (cur.parent != null) {                 // 列表第一行永远是「↑ 返回上级」
            FsNode u = new FsNode();
            u.name = "返回上级";
            u.full = "";
            u.isDir = true;
            u.link = cur.parent;
            shown.add(u);
        }
        shown.addAll(cur.children);

        final int cDir = dark ? 0xFFE8EAEE : 0xFF1A1F28;
        final int cGray = dark ? 0xFF6E747E : 0xFFAAB0B8;
        final int cSel = dark ? 0xFF1E3A66 : 0xFFDCE9FF;
        final int cBg = dark ? 0xFF14161A : 0xFFFFFFFF;
        final int cBgGray = dark ? 0xFF1A1D22 : 0xFFF7F8FA;
        final int cUp = dark ? 0xFF8FC0FF : 0xFF0B57D0;
        final int cUpBg = dark ? 0xFF1B2430 : 0xFFE8F1FF;

        listFilesAdapter = new ArrayAdapter<FsNode>(this, android.R.layout.simple_list_item_2,
                android.R.id.text1, shown) {
            @Override
            public View getView(int position, View convertView, ViewGroup parent) {
                View v = super.getView(position, convertView, parent);
                TextView t1 = (TextView) v.findViewById(android.R.id.text1);
                TextView t2 = (TextView) v.findViewById(android.R.id.text2);
                FsNode n = shown.get(position);
                if (n.link != null) {
                    String up = n.link.full.length() == 0 ? apkDisplayName : n.link.name;
                    t1.setText("↑  返回上级：" + up);
                    t1.setTextColor(cUp);
                    t2.setText(n.link.full.length() == 0 ? "（根目录）" : ("/" + n.link.full));
                    t2.setTextColor(cUp);
                    v.setBackgroundColor(cUpBg);
                    return v;
                }
                if (n.isDir) {
                    t1.setText("📁  " + n.name);
                    t1.setTextColor(cDir);
                    t2.setText("文件夹    " + n.children.size() + " 项");
                    t2.setTextColor(cGray);
                    v.setBackgroundColor(picked.contains(n) ? cSel : cBg);
                } else {
                    boolean smf = n.entry != null && n.entry.isSmf();
                    t1.setText("📄  " + n.name);
                    t1.setTextColor(smf ? cDir : cGray);
                    t2.setText(ApkIndex.formatSize(n.entry == null ? 0 : n.entry.size)
                            + (smf ? "    可以导出" : "    不是 .smf，不可导出"));
                    t2.setTextColor(smf ? cGray : cGray);
                    if (picked.contains(n)) v.setBackgroundColor(cSel);
                    else if (!smf) v.setBackgroundColor(cBgGray);
                    else v.setBackgroundColor(cBg);
                }
                return v;
            }
        };
        listFiles.setAdapter(listFilesAdapter);
        txtCurPath.setText("根目录 > " + (cur.full.length() == 0 ? apkDisplayName : cur.full.replace("/", " > ")));
    }

    private void updateSelInfo() {
        int smf = 0;
        for (FsNode n : picked) if (n.entry != null && n.entry.isSmf()) smf++;
        if (smf == 0) txtSelInfo.setText("还没选文件（点一行 .smf 就选中）");
        else txtSelInfo.setText("已选 " + smf + " 个 .smf 文件");
        btnGoExport.setEnabled(smf > 0);
    }

    // ============================================================ 导出
    private class FileSink implements Exporter.Sink {
        private final File dir;
        FileSink(File d) { dir = d; }
        public OutputStream create(String fileName) throws IOException {
            File f = new File(dir, fileName);
            if (f.exists()) f = new File(dir, Exporter.uniqueName(dir, fileName));
            return new FileOutputStream(f);
        }
    }

    private void gotoExport() {
        int smf = 0;
        for (FsNode n : picked) if (n.entry != null && n.entry.isSmf()) smf++;
        if (smf == 0) { toast("先选几个 .smf 文件"); return; }
        txtExportHead.setText("准备导出 " + smf + " 个 .smf 文件");
        if (outDir == null) {
            File dl = new File(Environment.getExternalStorageDirectory(), "Download");
            outDir = dl.isDirectory() ? dl : Environment.getExternalStorageDirectory();
            txtOutDir.setText(outDir.getAbsolutePath() + "（默认，可改）");
        }
        progressBar.setProgress(0);
        txtExportStatus.setText("");
        showPage(pageExport, true);
    }

    private void doExport() {
        if (outDir == null) { toast("请先选择输出文件夹"); return; }
        if (!outDir.isDirectory() && !outDir.mkdirs()) { toast("输出文件夹不可用：" + outDir); return; }

        final List<ApkEntry> list = new ArrayList<ApkEntry>();
        for (FsNode n : picked) if (n.entry != null && n.entry.isSmf()) list.add(n.entry);
        if (list.isEmpty()) { toast("没有可导出的 .smf"); return; }

        btnDoExport.setEnabled(false);
        btnPickDir.setEnabled(false);
        btnBack.setEnabled(false);
        progressBar.setMax(list.size());
        progressBar.setProgress(0);

        final Exporter.Sink sink = new FileSink(outDir);
        new Thread(new Runnable() {
            public void run() {
                final Exporter.Outcome o = Exporter.export(apk, list, sink, new Exporter.Progress() {
                    public void onFile(final int idx, final int total, final String name) {
                        ui.post(new Runnable() {
                            public void run() {
                                progressBar.setProgress(idx);
                                txtExportStatus.setText("正在导出… " + idx + " / " + total + "\n" + name);
                            }
                        });
                    }
                });
                ui.post(new Runnable() {
                    public void run() {
                        btnDoExport.setEnabled(true);
                        btnPickDir.setEnabled(true);
                        btnBack.setEnabled(true);
                        progressBar.setProgress(list.size());
                        txtExportStatus.setText("完成：" + o.success + " 个成功，" + o.failed + " 个失败\n"
                                + "输出目录：" + outDir.getAbsolutePath());
                        showResult(o);
                    }
                });
            }
        }).start();
    }

    private void showResult(Exporter.Outcome o) {
        final StringBuilder sb = new StringBuilder();
        sb.append("成功 ").append(o.success).append(" 个，失败 ").append(o.failed).append(" 个");
        sb.append("\n\n输出到：\n").append(outDir.getAbsolutePath());
        if (!o.details.isEmpty()) {
            sb.append("\n\n失败明细：\n");
            int n = Math.min(o.details.size(), 10);
            for (int i = 0; i < n; i++) sb.append("· ").append(o.details.get(i)).append("\n");
            if (o.details.size() > n) sb.append("… 还有 ").append(o.details.size() - n).append(" 条\n");
        }
        new AlertDialog.Builder(this)
                .setTitle("导出结果")
                .setMessage(sb.toString())
                .setCancelable(false)
                .setPositiveButton("确定", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface d, int w) { backToImport(); }
                })
                .show();
    }

    // ============================================================ 杂项
    private void closeApk() {
        apk.close();
    }

    private void toast(String s) {
        Toast.makeText(this, s, Toast.LENGTH_SHORT).show();
    }

    @Override
    protected void onResume() {
        super.onResume();
        updateGrantButton();
        resumePending();   // 从系统"所有文件访问"设置页回来时，把刚才想做的事接着做完
    }
}
