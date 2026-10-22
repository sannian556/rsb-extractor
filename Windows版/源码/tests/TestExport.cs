using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ApkSmfExtractor;

/// <summary>
/// 导出链路端到端测试：TestExport.exe &lt;apk&gt; &lt;输出目录&gt;
/// 覆盖三种情况：RSB 容器外壳（要解包）、明文 .smf（原样导出）、非 .smf（应失败）
/// </summary>
class TestExport
{
    static int fail = 0;

    static void Check(bool cond, string what)
    {
        Console.WriteLine((cond ? "  [OK]   " : "  [FAIL] ") + what);
        if (!cond) fail++;
    }

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        if (args.Length < 2) { Console.WriteLine("usage: TestExport <apk> <outdir>"); return 2; }
        string apkPath = args[0];
        string outDir = args[1];
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        using (ApkReader r = new ApkReader())
        {
            r.Open(apkPath);
            List<ApkEntry> picked = new List<ApkEntry>();
            picked.Add(Find(r, "assets/dino.rsb.smf"));            // RSB 容器，小
            picked.Add(Find(r, "assets/ConfigManifest.json.smf")); // 明文 smf
            picked.Add(Find(r, "assets/init.rsb.tag.smf"));        // 明文 smf（34 字节）
            picked.Add(Find(r, "assets/wechaticon.png"));          // 非 smf → 必须失败

            Console.WriteLine("picked " + picked.Count + " entries");
            ExportOutcome o = Exporter.Export(r, picked, outDir, null);

            Console.WriteLine("success=" + o.Success + "  failed=" + o.Failed);
            for (int i = 0; i < o.Detail.Count; i++) Console.WriteLine("  fail detail: " + o.Detail[i]);

            Check(o.Success == 3, "成功 3 个（dino + 两个明文 smf）");
            Check(o.Failed == 1, "失败 1 个（png 不是 smf）");
            Check(o.Detail.Count == 1 && o.Detail[0].IndexOf("png") >= 0, "失败明细指向 png");

            // dino：RSB 容器解出来后应当是 21,483,520 字节，头是 1bsr
            string dino = Path.Combine(outDir, "dino.rsb.smf");
            Check(File.Exists(dino), "dino.rsb.smf 已写出");
            if (File.Exists(dino))
            {
                FileInfo fi = new FileInfo(dino);
                Check(fi.Length == 21483520, "dino 解出 21,483,520 字节（实得 " + fi.Length.ToString("N0") + "）");
                byte[] h = new byte[4];
                using (FileStream fs = File.OpenRead(dino)) fs.Read(h, 0, 4);
                Check(h[0] == 0x31 && h[1] == 0x62 && h[2] == 0x73 && h[3] == 0x72, "dino 头是 1bsr(rsb1)");
            }

            // 明文 smf 应当与 APK 里的内容逐字节一致
            string cfg = Path.Combine(outDir, "ConfigManifest.json.smf");
            Check(File.Exists(cfg), "ConfigManifest.json.smf 已写出");
            if (File.Exists(cfg))
            {
                byte[] a = r.ReadAll(Find(r, "assets/ConfigManifest.json.smf"));
                byte[] b = File.ReadAllBytes(cfg);
                Check(a.Length == b.Length && Md5(a) == Md5(b), "明文 smf 原样导出（" + b.Length + " 字节，内容一致）");
            }

            // png 不该被写出
            Check(!File.Exists(Path.Combine(outDir, "wechaticon.png")), "非 .smf 没有被写出");

            // 再导出一次同名文件 → 应自动改名而不是覆盖
            Console.WriteLine("--- re-export to check rename ---");
            List<ApkEntry> again = new List<ApkEntry>();
            again.Add(Find(r, "assets/dino.rsb.smf"));
            ExportOutcome o2 = Exporter.Export(r, again, outDir, null);
            Check(o2.Success == 1 && File.Exists(Path.Combine(outDir, "dino_2.rsb.smf")),
                  "重名时自动写成 dino_2.rsb.smf，不覆盖");
        }

        Console.WriteLine(fail == 0 ? "ALL PASSED" : ("FAILED: " + fail));
        return fail == 0 ? 0 : 1;
    }

    static ApkEntry Find(ApkReader r, string name)
    {
        for (int i = 0; i < r.Entries.Count; i++)
            if (string.Equals(r.Entries[i].Name, name, StringComparison.OrdinalIgnoreCase)) return r.Entries[i];
        throw new FileNotFoundException(name);
    }

    static string Md5(byte[] data)
    {
        MD5 m = MD5.Create();
        byte[] h = m.ComputeHash(data);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
        return sb.ToString();
    }
}
