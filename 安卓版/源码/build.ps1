param(
    [string]$SdkDir = 'D:\AndroidDev\sdk',
    [string]$OutApk = '',
    [switch]$SkipSign
)
# 构建 APK —— 不用 Gradle，直接调 Android SDK 的命令行工具链
#   aapt2 compile -> aapt2 link -> javac -> d8 -> 塞进 apk -> zipalign -> apksigner
$ErrorActionPreference = 'Continue'
$proj = Split-Path -Parent $PSScriptRoot
if (-not $proj) { $proj = $PSScriptRoot }
$proj = $PSScriptRoot
$work = Join-Path $proj 'build'
if (-not $OutApk) { $OutApk = Join-Path $work 'APVK.apk' }

function Find-Exe([string]$dir, [string]$name) {
    $p = Join-Path $dir $name
    if (Test-Path $p) { return $p }
    throw "找不到 $name （在 $dir）"
}

# ---- 找工具 ----
$buildToolsRoot = Join-Path $SdkDir 'build-tools'
if (-not (Test-Path $buildToolsRoot)) { throw "没装 build-tools：$buildToolsRoot" }
$bt = Get-ChildItem $buildToolsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
$btDir = $bt.FullName
Write-Output ("build-tools : " + $bt.Name)

$platformsRoot = Join-Path $SdkDir 'platforms'
$plat = Get-ChildItem $platformsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
$androidJar = Join-Path $plat.FullName 'android.jar'
Write-Output ("platform    : " + $plat.Name)

$aapt2 = Find-Exe $btDir 'aapt2.exe'
$zipalign = Find-Exe $btDir 'zipalign.exe'
$apksigner = Find-Exe $btDir 'apksigner.bat'
$d8 = Find-Exe $btDir 'd8.bat'

$javac = (Get-Command javac -ErrorAction SilentlyContinue)
if (-not $javac) { throw '找不到 javac —— JAVA_HOME/bin 要加进 PATH' }

# ---- 清理 ----
foreach ($d in @('gen', 'classes', 'dex', 'res')) {
    $p = Join-Path $work $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $work | Out-Null
foreach ($d in @('gen', 'classes', 'dex', 'res')) { New-Item -ItemType Directory -Force -Path (Join-Path $work $d) | Out-Null }

$minSdk = '24'
$targetSdk = '35'

# ---- 1. 编译资源 ----
Write-Output '--- 1/6 aapt2 compile ---'
$resZip = Join-Path $work 'res\res.zip'
& $aapt2 compile --dir (Join-Path $proj 'res') -o $resZip
if ($LASTEXITCODE -ne 0) { throw 'aapt2 compile 失败' }

# ---- 2. 链接资源 + 生成 R.java ----
Write-Output '--- 2/6 aapt2 link ---'
$baseApk = Join-Path $work 'base.apk'
& $aapt2 link -o $baseApk -I $androidJar --manifest (Join-Path $proj 'AndroidManifest.xml') `
    -R $resZip --java (Join-Path $work 'gen') --min-sdk-version $minSdk --target-sdk-version $targetSdk `
    --auto-add-overlay
if ($LASTEXITCODE -ne 0) { throw 'aapt2 link 失败' }

# ---- 3. javac ----
Write-Output '--- 3/6 javac ---'
$javaFiles = @(Get-ChildItem (Join-Path $proj 'java') -Recurse -Filter *.java | ForEach-Object { $_.FullName })
$javaFiles += @(Get-ChildItem (Join-Path $work 'gen') -Recurse -Filter *.java | ForEach-Object { $_.FullName })
Write-Output ("  源文件 " + $javaFiles.Count + " 个")
$argFile = Join-Path $work 'javac-args.txt'
$lines = @('-source', '8', '-target', '8', '-encoding', 'UTF-8', '-nowarn',
           '-classpath', $androidJar, '-d', (Join-Path $work 'classes'))
$lines += $javaFiles
[System.IO.File]::WriteAllLines($argFile, $lines, (New-Object System.Text.UTF8Encoding($false)))
& $javac "@$argFile"
if ($LASTEXITCODE -ne 0) { throw 'javac 失败' }

# ---- 4. d8 转 dex ----
Write-Output '--- 4/6 d8 ---'
$classFiles = @(Get-ChildItem (Join-Path $work 'classes') -Recurse -Filter *.class | ForEach-Object { $_.FullName })
Write-Output ("  class 文件 " + $classFiles.Count + " 个")
$argFile2 = Join-Path $work 'd8-args.txt'
$lines2 = @('--release', '--min-api', $minSdk, '--lib', $androidJar,
            '--output', (Join-Path $work 'dex'), '--no-desugaring')
$lines2 += $classFiles
[System.IO.File]::WriteAllLines($argFile2, $lines2, (New-Object System.Text.UTF8Encoding($false)))
& $d8 "@$argFile2"
if ($LASTEXITCODE -ne 0) { throw 'd8 失败' }
$dexFile = Join-Path $work 'dex\classes.dex'
if (-not (Test-Path $dexFile)) { throw "d8 没产出 classes.dex" }
Write-Output ("  classes.dex = " + (Get-Item $dexFile).Length + " bytes")

# ---- 5. 把 dex 塞进 apk ----
Write-Output '--- 5/6 打包 ---'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$unaligned = Join-Path $work 'unaligned.apk'
if (Test-Path $unaligned) { Remove-Item $unaligned -Force }
Copy-Item $baseApk $unaligned -Force
$zip = [System.IO.Compression.ZipFile]::Open($unaligned, 'Update')
try {
    $old = $zip.GetEntry('classes.dex')
    if ($old) { $old.Delete() }
    $entry = $zip.CreateEntry('classes.dex', [System.IO.Compression.CompressionLevel]::NoCompression)
    $out = $entry.Open()
    try {
        $bytes = [System.IO.File]::ReadAllBytes($dexFile)
        $out.Write($bytes, 0, $bytes.Length)
    } finally { $out.Close() }
} finally { $zip.Dispose() }
Write-Output ("  unaligned.apk = " + (Get-Item $unaligned).Length + " bytes")

# ---- 6. 对齐 + 签名 ----
Write-Output '--- 6/6 zipalign + apksigner ---'
$aligned = Join-Path $work 'aligned.apk'
if (Test-Path $aligned) { Remove-Item $aligned -Force }
& $zipalign -f -p 4 $unaligned $aligned
if ($LASTEXITCODE -ne 0) { throw 'zipalign 失败' }

if ($SkipSign) {
    Copy-Item $aligned $OutApk -Force
    Write-Output ("未签名 APK: " + $OutApk)
    return
}

$ks = Join-Path $proj 'apksmf.jks'
if (-not (Test-Path $ks)) {
    Write-Output '  生成签名用 keystore…'
    $keytool = Join-Path $env:JAVA_HOME 'bin\keytool.exe'
    if (-not (Test-Path $keytool)) { $keytool = 'keytool' }
    & $keytool -genkeypair -v -keystore $ks -alias apksmf -keyalg RSA -keysize 2048 `
        -validity 10000 -storepass android -keypass android `
        -dname "CN=APK Smf Extractor, OU=Dev, O=Dev, L=Beijing, ST=Beijing, C=CN"
    if ($LASTEXITCODE -ne 0) { throw 'keytool 失败' }
}
if (Test-Path $OutApk) { Remove-Item $OutApk -Force }
& $apksigner sign --ks $ks --ks-pass pass:android --key-pass pass:android --out $OutApk $aligned
if ($LASTEXITCODE -ne 0) { throw 'apksigner 失败' }

Write-Output ''
Write-Output ("APK 已生成: " + $OutApk + "   (" + [math]::Round((Get-Item $OutApk).Length / 1KB, 1) + " KB)")
& $apksigner verify --print-certs $OutApk
