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

    /// <summary>
    /// 导出逻辑（和界面分开，方便自动化测试直接调）。
    ///
    /// 规则（按需求）：
    ///   · 只导出 .smf 结尾的文件；其它类型一律记为失败并说明原因；
    ///   · .smf 里如果是 RSB 容器外壳 → 解包成明文 rsb1 再写；本来就是明文的 → 原样导出；
    ///   · 目标文件重名时自动加 _2 / _3 后缀，不覆盖已有文件。
    /// </summary>
    public static class Exporter
    {
        public static ExportOutcome Export(ApkReader apk, List<ApkEntry> picked, string outDir,
                                           ExportProgress progress)
        {
            ExportOutcome r = new ExportOutcome();
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            for (int i = 0; i < picked.Count; i++)
            {
                ApkEntry e = picked[i];
                if (progress != null) progress(i + 1, picked.Count, e.BaseName);

                string err;
                string path = ExportOne(apk, e, outDir, out err);
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
            }
            return r;
        }

        /// <summary>导出单个条目；成功返回写出的文件路径，失败返回 null 并给出 err</summary>
        public static string ExportOne(ApkReader apk, ApkEntry e, string outDir, out string err)
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

                // 只看头 4 字节判断要不要解壳
                byte[] head4 = new byte[4];
                using (Stream s = apk.OpenEntry(e))
                {
                    int n = 0;
                    while (n < 4)
                    {
                        int k = s.Read(head4, n, 4 - n);
                        if (k <= 0) break;
                        n += k;
                    }
                }

                if (RsbUnpacker.IsRsbHead(head4))
                {
                    using (Stream s = apk.OpenEntry(e))
                    using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write,
                                                          FileShare.None, 1 << 20))
                    {
                        long written;
                        string uerr;
                        if (!RsbUnpacker.Unpack(s, fs, out written, out uerr))
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
                    apk.CopyTo(e, fs);
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
