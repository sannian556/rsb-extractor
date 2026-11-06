# RSB 提取器 —— 从 PvZ2 安装包里取出 .smf 的小工具

> **RSB 提取器 = RSB（资源包）+ 提取器**（名字来自作者给的那个图标）。
> 做两件事：① 像文件管理器一样翻手机/电脑里的 APK；② 把包里的 `.smf` 取出来，
> 如果它是 `RSB 容器` 外壳就自动解开，还原成工具箱和游戏都能直接认的 `rsb1` 文件。
>
> 本仓库是**本地仓库**（不发布到网上）。两个平台的成品和源码都在这儿。

---

## 目录

```
RSB提取器本地仓库\
├─ 安卓版\
│   ├─ RSB提取器.apk                  ← 成品（165 KB / 168,878 字节，v1.0.2，直接装手机）
│   ├─ RSB提取器-使用说明.txt
│   ├─ 源码\                     ← 完整工程：java / res / AndroidManifest.xml / build.ps1 / tests
│   └─ 截图\
└─ Windows版\
    ├─ RSB提取器.exe            ← 成品（466,944 字节，XP ~ Win11）
    ├─ RSB提取器.exe.config     ← 跟 exe 放一起（让 XP 也能跑）
    ├─ RSB提取器(单文件免配置版).exe
    ├─ RSB提取器-使用说明.txt
    └─ 源码\                     ← src（含 Theme.cs 深色模式与 Lzma）/ assets（图标）/ tools / tests / build.ps1
```

## 怎么用

**安卓（RSB提取器.apk）**：装上 → 给"所有文件访问"权限 → 点「导入 apk 文件」翻目录选包
→ 点一行 `.smf` 选中（可多选，选的时候列表不会跳回顶部）→ 右下角「导出 .smf」
→ 选输出文件夹 → 开始导出。

**Windows（RSB提取器.exe）**：双击运行（或把 apk 拖进去）→ 左侧目录树翻包
→ 右侧选 `.smf` → 导出。

## 怎么自己构建

**安卓**：不用 Gradle、不用 AndroidX。装好 JDK 17 + Android SDK（build-tools / platform-35）后：

```powershell
$env:JAVA_HOME='...\jdk-17'
.\安卓版\源码\build.ps1            # aapt2 compile → aapt2 link → javac → d8 → 打包 → zipalign → apksigner
```

产物在 `build\RSB提取器.apk`。签名用的 `apksmf.jks`（口令都是 `android`，仅供本工具自己用）。

**Windows**：零第三方依赖，用系统自带的 csc 编译（不需要装任何东西）。工程里带了 `build.ps1`，
直接跑它就出两个 exe（CLR 2.0 的 XP~Win11 版 + CLR 4.0 的单文件免配置版）：

```powershell
.\Windows版\源码\build.ps1          # 产物在 Windows版\源码\build\ 下
```

也可以手敲 csc —— **中文源码必须 `/codepage:65001`**，图标在 `assets\`：

```bat
csc /target:winexe /codepage:65001 /out:RSB提取器.exe /win32icon:assets\app.ico src\*.cs src\Lzma\*.cs
```

要 XP 也能跑就用 `%WINDIR%\Microsoft.NET\Framework\v2.0.50727\csc.exe` 编译，并放一个
219 字节的 `RSB提取器.exe.config`，同时声明 `supportedRuntime v4.0 / v2.0.50727`
（这样一份 exe 从 XP 到 Win11 都能起来）。把 `源码\`（`src` / `assets` / `build.ps1`）整体拷走后
跑 `build.ps1`，能重建出与仓库里一致的成品（466,944 字节）。

## 核心代码是自己写的，还是参考了别人的？（重要，据实说明）

### 一、自己实测逆出来的：RSB 容器格式

`.rsb.smf` 外面那层 `RSB 容器` 外壳**没有任何公开文档**，也没有可以照抄的实现。做法是拿
《植物大战僵尸2》4.2.4 的**33 个** `.rsb.smf` 做差分试错，一条一条验证出来的：

- 先确认它**不是加密**，而是**分块 LZMA**：头 32 字节（`RSB 容器` + 版本 + 未压缩总大小 +
  压缩区大小 + 块大小 + 块数）后面跟着 N×36 字节的块表（未压缩偏移/大小、压缩偏移、
  压缩大小、props、字典），再往后每块是 **5 字节 LZMA 属性头 + raw LZMA1 流**；
- 两个必须踩过的坑：**raw LZMA 没有结束标记**，必须把表里声明的未压缩大小喂给解码器
  （Python 的 `max_length`、C# 的 `Code(..., outSize, ...)`、Java 的 `Code(in, out, outSize)`）；
  **字典字段为 0 时按 32 MB 兜底**；
- 每块的参数都从**该块自己的头**里读，不写死；
- 33 个包逐个解包后**全部通过完整性校验**，导出文件与 Windows 版工具算出的 MD5 完全一致。

这套逻辑先写成 Python 验证脚本，再自己移植到 C#（Windows 版）和 Java（安卓版），
三份实现是同一套推导出来的规则。

### 二、自己写的（除下面第三节那一处外，全部）

APK/ZIP 解析（Android 上自己用 `FileChannel` 绝对读中央目录，没用 `ZipArchive`/`ZipFile`）、
RSB 容器解包器、导出器（重名自动加序号不覆盖）、
安卓端三层文件浏览器（目录列表 + 可输入地址栏 + 列表第一行「↑ 返回上级」）、
三档主题（自己 `setTheme()` + `recreate()`，没引 AppCompat）、
存储权限分段申请与"授权后自动继续"、Android 15 边到边（edge-to-edge）适配（去掉 ActionBar +
自己按 window insets 留系统栏位置）、
Windows 端的 WinForms 界面（资源管理器样式、系统图标、面包屑、拖放、XP~Win11 兼容）、
以及两边的构建脚本。

### 三、用了第三方开源代码的地方（只有一处）

- **7-Zip LZMA SDK（public domain）**：**LZMA 解码算法本身不是我写的**。
  Windows 端是 `源码\src\Lzma\*.cs`，安卓端是 `源码\java\SevenZip\**`（这套 Java 源码就是
  LZMA SDK 的 Java 版）。我做的只是**把它接到 RSB 容器的分块流上**：按每块自己的属性头构造解码器、
  把声明的未压缩大小喂进去、处理字典兜底，以及给它套一层 64 KB 缓冲
  （不套缓冲时 Python/Java 逐字节读输入，解一个 416 MB 的包要 16 分钟，套上后 43 秒）。
- **图标**：是作者自己提供的 PNG（`源码\icon-512.png`，已派生成各分辨率 mipmap）。

### 四、参考过但没抄代码的

- Android 官方文档：`aapt2` / `d8` / `apksigner` 的用法，以及 Android 15 "边到边"与
  window insets 的处理方式。遇到"顶栏被系统标题栏盖住"那一次，查了官方边到边文档和
  StackOverflow 上的同症状提问 —— **只取了思路（去掉 ActionBar + 自己留系统栏位置），没抄代码**。
- 同类项目（如 PopStudio）**没有参考它的 RSB 容器实现**。之前分析过 `PvZ 2 SHUTTLE Toolkit`
  那个 exe 的类型名，那是为了搞清它的依赖构成，不是拿它的代码。

## 已知限制

- 安卓版只在 **Android 9** 模拟器上完整实测过（导出的 `.smf` 与 Windows 版 MD5 一致）；
  Android 15 的"边到边"适配是按官方行为变更改的，逻辑上任何版本都不该再被系统栏盖住。
- 只处理**标准 ZIP 结构**的 apk；加固过的包会提示"找不到 ZIP 结尾记录"。
- 只导出 `.smf`；其它类型一律跳过并计入失败。
- 程序**不联网、不写注册表、不设自启**，只读你选中的 apk、只写你选的输出目录。

## 说明

仅供学习与自用：请只处理你自己拥有或有权处理的安装包。游戏资源版权归原公司所有。
