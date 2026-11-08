using System;
using System.Collections.Generic;
using System.IO;

namespace ApkSmfExtractor
{
    public class ExportOutcome
    {
        public int Success;
        public int Failed;
        public List<string> Detail = new List<string>();    // 失败明细
        public List<string> Files = new List<string>();     // 成功写出的文件全路径
    }

    public delegate void ExportProgress(int index, int total, string name);

    /// <summary>字节级进度：done/total 是"解出后"的字节数（RSB 容器按头部声明的大小算）</summary>
    public delegate void ExportBytes(long done, long total, string name);

    /// <summary>
    /// 导出逻辑（和界面分开，方便自动化测试直接调）。
    ///
    /// 规则（按需求）：
    ///   · 只导出 .smf 结尾的文件；其它类型一律记为失败并说明原因；
    ///   · .smf 里如果是 RSB 容器外壳 → 解包成明文 rsb1 再写；本来就是明文的 → 原样导出；
    ///   · 目标文件重名时自动加 _2 / _3 后缀，不覆盖已有文件。
    ///
    /// 1.0.2 起增加字节进度（ExportBytes）：进度条不再是"按文件数"，单个文件也能看到真实进度。
    /// </summary>
    public static class Exporter
    {
        public static ExportOutcome Export(ApkReader apk, List<ApkEntry> picked, string outDir,
                                           ExportProgress progress)
        {
            return Export(apk, picked, outDir, progress, null);
        }

        public static ExportOutcome Export(ApkReader apk, List<ApkEntry> picked, string outDir,
                                           ExportProgress progress, ExportBytes bytes)
        {
            ExportOutcome r = new ExportOutcome();
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            // 先算总字节：RSB 容器用头部声明的"解出后大小"，明文用未压缩大小
            long totalBytes = 0;
            long[] sizes = new long[picked.Count];
            for (int i = 0; i < picked.Count; i++)
            {
                ApkEntry e = picked[i];
                long sz = e.Size;
                try
                {
                    byte[] head = ReadHead(apk, e, RsbUnpacker.HeaderSize);
                    if (head.Length >= RsbUnpacker.HeaderSize && RsbUnpacker.IsRsbHead(head))
                    {
                        long u = BitConverter.ToInt64(head, 8);
                        if (u > 0) sz = u;
                    }
                }
                catch { }
                sizes[i] = sz;
                totalBytes += sz;
            }
            if (bytes != null) bytes(0, totalBytes, picked.Count > 0 ? picked[0].BaseName : null);

            long doneBase = 0;
            for (int i = 0; i < picked.Count; i++)
            {
                ApkEntry e = picked[i];
                if (progress != null) progress(i + 1, picked.Count, e.BaseName);
                if (bytes != null) bytes(doneBase, totalBytes, e.BaseName);

                string err;
                string path = ExportOne(apk, e, outDir, out err, bytes, doneBase, totalBytes);
                if (path != null)
                {
                    r.Success++;
                    r.Files.Add(path);
                }
                else
                {
                    r.Failed++;
                    r.Detail.Add(e.BaseName + "：" + err);
                }

                doneBase += sizes[i];
                if (bytes != null) bytes(Math.Min(doneBase, totalBytes), totalBytes, e.BaseName);
            }
            if (bytes != null) bytes(totalBytes, totalBytes, null);
            return r;
        }

        /// <summary>导出单个条目；成功返回写出的文件路径，失败返回 null 并给出 err</summary>
        public static string ExportOne(ApkReader apk, ApkEntry e, string outDir, out string err)
        {
            return ExportOne(apk, e, outDir, out err, null, 0, 0);
        }

        public static string ExportOne(ApkReader apk, ApkEntry e, string outDir, out string err,
                                       ExportBytes bytes, long doneBase, long totalBytes)
        {
            err = null;
            string outPath = null;
            try
            {
                if (!e.IsSmf)
                {
                    err = "不是 .smf 文件（本工具只导出 .smf）";
                    return null;
                }

                outPath = MakeUniquePath(outDir, e.BaseName);
                byte[] head = ReadHead(apk, e, RsbUnpacker.HeaderSize);

                if (RsbUnpacker.IsRsbHead(head))
                {
                    using (Stream s = apk.OpenEntry(e))
                    using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write,
                                                          FileShare.None, 1 << 20))
                    {
                        long written;
                        string uerr;
                        if (!RsbUnpacker.Unpack(s, fs, out written, out uerr,
                            delegate(long sofar, long tot)
                            {
                                if (bytes != null) bytes(doneBase + sofar, totalBytes, e.BaseName);
                            }))
                        {
                            fs.Close();
                            TryDelete(outPath);
                            err = "RSB 容器解包失败：" + uerr;
                            return null;
                        }
                    }
                    return outPath;
                }

                // 不是 RSB 容器（例如 ConfigManifest.json.smf 本来就是明文）→ 原样导出
                using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write,
                                                      FileShare.None, 1 << 20))
                {
                    using (Stream src = apk.OpenEntry(e))
                    {
                        byte[] buf = new byte[1 << 16];
                        long copied = 0;
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            copied += n;
                            if (bytes != null) bytes(doneBase + copied, totalBytes, e.BaseName);
                        }
                    }
                }
                return outPath;
            }
            catch (Exception ex)
            {
                if (outPath != null) TryDelete(outPath);
                err = ex.Message;
                return null;
            }
        }

        /// <summary>只读条目开头的 n 个字节（用来判断是不是 RSB 容器 / 取解出大小）</summary>
        private static byte[] ReadHead(ApkReader apk, ApkEntry e, int want)
        {
            byte[] b = new byte[want];
            int got = 0;
            using (Stream s = apk.OpenEntry(e))
            {
                while (got < want)
                {
                    int n = s.Read(b, got, want - got);
                    if (n <= 0) break;
                    got += n;
                }
            }
            if (got == want) return b;
            byte[] t = new byte[got];
            Array.Copy(b, t, got);
            return t;
        }

        private static void TryDelete(string p)
        {
            try { if (p != null && File.Exists(p)) File.Delete(p); }
            catch { }
        }

        public static string MakeUniquePath(string dir, string name)
        {
            string p = Path.Combine(dir, name);
            if (!File.Exists(p)) return p;

            // 把 .smf 当成"最后一层扩展名"，序号插在它前面：
            //   dino.rsb.smf            -> dino_2.rsb.smf
            //   ConfigManifest.json.smf -> ConfigManifest_2.json.smf
            string tail = "";
            string body = name;
            if (name.Length > 4 && name.EndsWith(".smf", StringComparison.OrdinalIgnoreCase))
            {
                tail = name.Substring(name.Length - 4);
                body = name.Substring(0, name.Length - 4);
            }
            string inner = Path.GetExtension(body);
            string stem = inner.Length > 0 ? body.Substring(0, body.Length - inner.Length) : body;

            for (int i = 2; i < 10000; i++)
            {
                string q = Path.Combine(dir, stem + "_" + i + inner + tail);
                if (!File.Exists(q)) return q;
            }
            return p;
        }
    }
}
