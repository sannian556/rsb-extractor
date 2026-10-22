# -*- coding: utf-8 -*-
import io, os
ROOT = r'D:\AI deekseek'
fails = []
def read(p): return io.open(p, 'r', encoding='utf-8-sig').read().replace('\r\n', '\n')
def write(p, t): io.open(p, 'w', encoding='utf-8-sig', newline='\r\n').write(t)
def rep(t, old, new, label, must=1):
    c = t.count(old)
    if c != must:
        fails.append('%s x%d（期望 %d）' % (label, c, must)); return t
    return t.replace(old, new)

# ---------- 1) 操作日志：追加第 42 条到第一部分末尾 ----------
log = read(os.path.join(ROOT, '操作日志.md'))
entry = u'''
### 42. 给两个 exe 加「方案 B」的深色模式 —— 已执行（2026-09-21）

**需求**：用户问「exe 程序支不支持深色模式」→ 发现 `APK提取工具.exe`（Windows 版）**固定浅色**（配色全写死），
而 `PvZ 2 SHUTTLE Toolkit—GUI.exe` 有完整主题系统但有个 bug；用户要求**两个都做**，并选定
**方案 B**：三态（跟随系统 / 浅色 / 深色），**系统不支持深色时「跟随系统」变灰 + 说明原因**。

**关键设计（两条，都是这次讨论出来的）**：
- **「系统支不支持」不能看版本号**：没清单的进程读 `Environment.OSVersion` 会被骗成 6.2。
  判据改用 **`HKCU\\...\\Themes\\Personalize\\AppsUseLightTheme` 这个键在不在**（Win10 1809+ 才有）+ 非高对比度。
- **读不到一律按「浅色」**：这个值 1=浅色 0=深色，PvZ GUI 原来 `value == null → return false`、
  `catch → return false`，等于"读不到就按深色" → **老系统（XP/Win7）一勾「跟随系统主题」界面反而变深，正好反了**。
- **「深色」开关不跟着变灰**：系统不支持只影响"跟随"，深色是自绘的、XP 上照样能用。

**① `APK提取工具.exe`（Windows 版，从零加的主题）**：
- 新增 `src\\Theme.cs`：深浅两套调色板 + 系统探测 + 三态持久化（`%APPDATA%\\APK提取工具\\设置.txt`，
  `theme=auto|light|dark`）+ uxtheme 滚动条 + DWM 标题栏（全部 try/catch，老系统上静默降级）。
- 浅色下**一切照旧**（按钮仍是 `FlatStyle.System`、ToolStrip 仍用系统渲染器、ListView 不自绘）；
  切到深色才换：按钮改自绘扁平、ToolStrip 换 `DarkToolStripRenderer`、ListView 开 `OwnerDraw`
  （表头/行/子项/大图标全自绘）、TreeView 换底色/线色、分隔线/状态条/面包屑换主题色。
- 导航栏右侧加「外观：xxx」下拉（三态），系统不支持时「跟随系统」`Enabled=false` + Tooltip 写原因。
- 响应 `WM_SETTINGCHANGE`：跟随模式下你在 Windows 里换主题，它实时跟着变。
- 新增 `build.ps1`（**这两个 exe 以前是手敲 csc 编的**）：CLR 2.0 出 XP~Win11 版 + 213 字节 `.exe.config`，
  CLR 4.0 出单文件免配置版；**中文源码必须 `/codepage:65001`**（源文件是 UTF-8 无 BOM）。
- 新增自动化开关：`--theme <auto|light|dark>` / `--no-sys-theme`（假装系统不支持）/ `--dump-theme <文件>` /
  `--dump-ui <文件>`（把控件树真实颜色写出来）/ `--show-theme-menu`。

**② `PvZ 2 SHUTTLE Toolkit—GUI.exe`**：修 `SystemPrefersLight()` 的两处 `return false` → `return true`；
新增 `SystemSupportsTheme()`；不支持时「跟随系统主题」那一格**变灰 + 点了没反应**（`SettingRow.LeadingEnabled`，
新加的字段，方框与文字一起用 `Theme.Dim`）、已保存的 Auto 落成浅色、标题栏那圈循环按钮在三态里跳过「跟随系统」。

**实测证据（全部可复现，不是"看着像"）**：
- `--dump-theme` 五种情形：默认/Auto→light ✓ 强制 dark→`bg=14171C` ✓ 强制 light→`bg=FFFFFF` ✓
  **假装不支持 + Auto → `sysSupport=no`、`autoEnabled=no`、`effective=light`** ✓ 不支持 + dark 仍能深色 ✓。
- `--dump-ui` 控件树：深色下 Panel `14171C` / Button `flat=Flat` `222730` / TextBox `1F232B` /
  TreeView `14171C`+`line=2C313A` / ListView `ownerDraw=True` / ToolStrip `render=Custom`；
  浅色下对应 `FFFFFF` / `flat=System` / `ownerDraw=False` / `render=System`（**原来的样子一点没动**）✓。
- 下拉三项：支持时「跟随系统」`enabled=True check=True`；假装不支持时 **`enabled=False`** 且 Tooltip 说明原因 ✓。
- 真截图（置顶 + `CopyFromScreen`）：深色浏览界面中心/左上采样 `(20,23,28)=#14171C` ✓、浅色 `(255,255,255)` ✓；
  工具栏、树、列表、表头、状态栏全深色，右上角显示「外观：深色」✓。
- PvZ GUI：重新编译（473.5 KB）→ 部署到发布目录 → 启动 → 点进设置页截图：**「深色外观」行正常、
  「跟随系统主题」勾选态、开关照旧变灰、无崩溃** ✓。**不支持的灰态在本机测不了**（本机 Windows 支持深色），
  但那段判据与 APK 工具里 `--no-sys-theme` 验证过的是同一套逻辑 ✓。

**部署**：`APK提取工具.exe`（448 KB）+ config + 单文件版 → `APK提取工具\\`、桌面、`APVK本地仓库\\Windows版\\` 三处
（哈希一致 ✓，提交 `28366e3`）；PvZ GUI（473.5 KB）→ `PvZ 2 SHUTTLE Toolkit Release V.2.4\\`。

**这次踩的坑**：
① `FindWindow(null, 标题)` 抓窗口抓不到 —— PowerShell 给 P/Invoke 的 `string` 传 `$null` 会变**空字符串**，
   等于去找"类名叫空字符串"的窗口（自己笔记里记过的坑，又踩一次）→ 改用进程的 `MainWindowHandle` ✓。
② **`PrintWindow` 对这种窗口拍出来是空白客户区**（只有边框 + 白底）→ 白忙一场；
   改成"`SetWindowPos` 置顶 + `CopyFromScreen` + 拍完还原"才拿到真图 ✓。
③ `Start-Process -ArgumentList` **不给含空格的参数加引号**（又是笔记里的老坑）：apk 路径里的 `D:\\AI deekseek`
   被拆成两半，程序报"找不到这个文件：D:\\AI" → 手动加引号 ✓。
④ `csc` 报 `CS0117 ListViewItem 没有 Enabled`、`CS0246 找不到 Color` —— 自绘/工具代码里顺手写的属性
   .NET 2.0 没有、`using System.Drawing` 没引，编译期就拦住了（这类错误一定要用 **CLR 2.0 的 csc** 编一遍才算数）✓。

'''
anchor = u'\n---\n\n# 第二部分 —— 原 `AGENTS.md` 的操作记录'
log = rep(log, anchor, u'\n' + entry + u'\n---\n\n# 第二部分 —— 原 `AGENTS.md` 的操作记录', '日志 #42')
write(os.path.join(ROOT, '操作日志.md'), log)

# ---------- 2) 踩坑与注意事项 ----------
pit = read(os.path.join(ROOT, '踩坑与注意事项.md'))
add4 = u'''- **经典 WinForms 没有「深色模式」这回事**（.NET 9 的 `Application.SetColorMode` 在这里用不上）：
  面板/按钮/文本框要自己刷 `BackColor`/`ForeColor`，`TreeView`/`ListView`/`ToolStrip` 要自绘，
  滚动条和标题栏只能靠 `SetWindowTheme` / `DwmSetWindowAttribute` 这两下 —— 而且**调用要 try/catch**
  （`dwmapi.dll` 在 XP 上不存在，`DllImport` 是**调用时**才抛 `DllNotFoundException`）（第一部分 #42）。
- **「系统支不支持深色」别看版本号**：没清单的进程读 `Environment.OSVersion` 会被骗成 6.2；
  看 `HKCU\\...\\Themes\\Personalize\\AppsUseLightTheme` 这个键**在不在**（Win10 1809+ 才有）+ 非高对比度（#42）。
- **读不到系统主题一律按「浅色」**：该值 1=浅色 0=深色，写成"读不到＝深色"会让老系统一勾「跟随系统」**反而变深**（#42）。
- **`SetWindowTheme` 要等句柄有了才有效**：`Control.IsHandleCreated == false` 时调用白调
  （隐藏的页面就是这种情况）→ 等它真正显示时再补一次（#42）。
- **`FindWindow($null, 标题)` 永远找不到窗口**：PowerShell 把 `$null` 传给 P/Invoke 的 `string` 会变**空字符串**
  → 改用进程的 `MainWindowHandle`（#42；同类坑第一部分 #38、#41）。
- **`PrintWindow` 拍 WinForms 窗口可能整块客户区空白**（只剩边框）→ 改用 `SetWindowPos(HWND_TOPMOST)` +
  `CopyFromScreen` + 拍完还原 `HWND_NOTOPMOST`（#42）。
'''
pit = rep(pit, u'- **包装控制台程序**', add4 + u'- **包装控制台程序**', '踩坑 · WinForms 深色')
add7 = u'''- **UI 主题这种"看不见的逻辑"，让程序自己 dump 出来最省事**：给 exe 加 `--dump-theme <文件>` /
  `--dump-ui <文件>`（递归写控件的 `BackColor/ForeColor/OwnerDraw/RenderMode` 和菜单项的 `Enabled`），
  验收脚本直接断言文本 —— 比截图可靠得多（截图要处理遮挡、DPI、PrintWindow 各种幺蛾子）（#42）。
'''
pit = rep(pit, u'## 七、交付与分发', add7 + u'\n## 七、交付与分发', '踩坑 · dump 验证')
write(os.path.join(ROOT, '踩坑与注意事项.md'), pit)

# ---------- 3) 项目档案：更新两个体积数字 ----------
proj = read(os.path.join(ROOT, '项目档案.md'))
proj = rep(proj, u'`APK提取工具\\\\APK提取工具.exe`（432 KB，XP~Win11）',
                 u'`APK提取工具\\\\APK提取工具.exe`（**448 KB**，XP~Win11；已支持深色模式）', '档案 · Win 体积')
proj = rep(proj, u'`PvZ工具箱工程\\\\build\\\\PvZ 2 SHUTTLE Toolkit—GUI.exe`（151 KB）',
                 u'`PvZ工具箱工程\\\\build\\\\PvZ 2 SHUTTLE Toolkit—GUI.exe`（**473 KB**）', '档案 · GUI 体积')
write(os.path.join(ROOT, '项目档案.md'), proj)

# ---------- 4) 环境与工具链：Windows 构建改成走 build.ps1 ----------
env = read(os.path.join(ROOT, '环境与工具链.md'))
env = rep(env, u'**Windows（APK提取工具 / 文件比较器原生版）** —— 系统自带 csc，零依赖：',
                u'**Windows（APK提取工具）** —— 直接跑现成的构建脚本（系统自带 csc，零依赖）：\n'
                u'```powershell\npowershell -ExecutionPolicy Bypass -File "D:\\AI deekseek\\APK提取工具工程\\build.ps1"\n'
                u'# 出两个：build\\APK提取工具.exe（CLR 2.0 + exe.config，XP~Win11）与单文件免配置版（CLR 4.0）\n'
                u'```\n\n**手敲 csc 的话（文件比较器原生版等）**：', '环境 · Win 构建')
write(os.path.join(ROOT, '环境与工具链.md'), env)

# ---------- 5) 仓库 README：Windows 构建那一节 ----------
rd = read(os.path.join(ROOT, 'APVK本地仓库', 'README.md'))
rd = rep(rd, u'**Windows**：零第三方依赖，用系统自带的 csc 编译（不需要装任何东西）：',
              u'**Windows**：零第三方依赖，用系统自带的 csc 编译（不需要装任何东西）；工程里带了 `build.ps1`，\n'
              u'直接跑它就出两个 exe（CLR 2.0 的 XP~Win11 版 + CLR 4.0 的单文件免配置版）：', 'README · Win 构建')
write(os.path.join(ROOT, 'APVK本地仓库', 'README.md'), rd)

print('失败：', fails if fails else '无')
