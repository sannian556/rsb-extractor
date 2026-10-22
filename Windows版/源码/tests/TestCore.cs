using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ApkSmfExtractor;

/// <summary>
/// 核心模块自测（不涉及界面）：
///   TestCore.exe &lt;apk&gt; list
///   TestCore.exe &lt;apk&gt; tree [层数]
///   TestCore.exe &lt;apk&gt; md5 &lt;条目名&gt;
///   TestCore.exe &lt;apk&gt; unpack &lt;条目名&gt; &lt;输出文件&gt;
/// </summary>
class TestCore
{
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { }

        if (args.Length < 2)
        {
            Console.WriteLine("usage: TestCore <apk> list|tree|md5|unpack ...");
            return 2;
        }
        string apk = args[0];
        string cmd = args[1].ToLowerInvariant();

        using (ApkReader r = new ApkReader())
        {
            r.Open(apk);
            Console.WriteLine("opened: " + apk);
            Console.WriteLine("file size : " + r.FileSize.ToString("N0"));
            Console.WriteLine("entries   : " + r.Entries.Count);

            if (cmd == "list")
            {
                int smf = 0;
                List<ApkEntry> big = new List<ApkEntry>(r.Entries);
                big.Sort(delegate(ApkEntry a, ApkEntry b) { return b.Size.CompareTo(a.Size); });
                for (int i = 0; i < r.Entries.Count; i++) if (r.Entries[i].IsSmf) smf++;
                Console.WriteLine("smf files : " + smf);
                Console.WriteLine("--- top 12 by size ---");
                for (int i = 0; i < 12 && i < big.Count; i++)
                    Console.WriteLine(string.Format("  {0,-46} {1,14:N0}  method={2}", big[i].Name, big[i].Size, big[i].Method));
                Console.WriteLine("--- root children ---");
                for (int i = 0; i < r.Root.Children.Count; i++)
                {
                    ApkNode n = r.Root.Children[i];
                    Console.WriteLine(string.Format("  {0} {1}  ({2} children)", n.IsDir ? "[D]" : "[F]", n.Name, n.Children.Count));
                }
                return 0;
            }

            if (cmd == "tree")
            {
                int depth = args.Length > 2 ? int.Parse(args[2]) : 2;
                Dump(r.Root, 0, depth);
                return 0;
            }

            if (cmd == "md5")
            {
                ApkEntry e = Find(r, args[2]);
                using (Stream s = r.OpenEntry(e))
                {
                    MD5 md5 = MD5.Create();
                    byte[] hash = md5.ComputeHash(s);
                    Console.WriteLine("entry : " + e.Name + "  size=" + e.Size.ToString("N0") + " method=" + e.Method);
                    Console.WriteLine("md5   : " + ToHex(hash));
                }
                return 0;
            }

            if (cmd == "unpack")
            {
                ApkEntry e = Find(r, args[2]);
                string outp = args[3];
                byte[] head4 = new byte[4];
                using (Stream s = r.OpenEntry(e))
                {
                    int n = 0;
                    while (n < 4) { int k = s.Read(head4, n, 4 - n); if (k <= 0) break; n += k; }
                }
                Console.WriteLine("entry     : " + e.Name + "  size=" + e.Size.ToString("N0") + " method=" + e.Method);
                Console.WriteLine("head4     : " + ToHex(head4) + "   (" + Encoding.ASCII.GetString(head4) + ")");

                DateTime t0 = DateTime.Now;
                if (RsbUnpacker.IsRsbHead(head4))
                {
                    using (Stream s = r.OpenEntry(e))
                    using (FileStream fs = new FileStream(outp, FileMode.Create, FileAccess.Write))
                    {
                        long written; string err;
                        bool ok = RsbUnpacker.Unpack(s, fs, out written, out err);
                        Console.WriteLine("rslb      : ok=" + ok + "  written=" + written.ToString("N0") + (ok ? "" : "  err=" + err));
                        if (!ok) return 1;
                    }
                }
                else
                {
                    using (Stream s = r.OpenEntry(e))
                    using (FileStream fs = new FileStream(outp, FileMode.Create, FileAccess.Write))
                        r.CopyTo(e, fs);
                    Console.WriteLine("copied    : " + new FileInfo(outp).Length.ToString("N0"));
                }
                Console.WriteLine("elapsed   : " + (DateTime.Now - t0).TotalSeconds.ToString("F1") + " s");
                using (FileStream fs = File.OpenRead(outp))
                {
                    MD5 md5 = MD5.Create();
                    Console.WriteLine("out md5   : " + ToHex(md5.ComputeHash(fs)));
                }
                return 0;
            }

            Console.WriteLine("unknown command: " + cmd);
            return 2;
        }
    }

    static ApkEntry Find(ApkReader r, string name)
    {
        for (int i = 0; i < r.Entries.Count; i++)
            if (string.Equals(r.Entries[i].Name, name, StringComparison.OrdinalIgnoreCase)) return r.Entries[i];
        for (int i = 0; i < r.Entries.Count; i++)
            if (string.Equals(r.Entries[i].BaseName, name, StringComparison.OrdinalIgnoreCase)) return r.Entries[i];
        throw new FileNotFoundException("APK 里找不到条目: " + name);
    }

    static void Dump(ApkNode n, int level, int maxDepth)
    {
        if (level > maxDepth) return;
        string pad = new string(' ', level * 2);
        Console.WriteLine(pad + (n.IsDir ? "[D] " : "[F] ") + n.Name + (n.IsDir ? "" : "   " + n.Size.ToString("N0")));
        if (!n.IsDir) return;
        for (int i = 0; i < n.Children.Count; i++)
        {
            if (level + 1 > maxDepth && n.Children.Count > 6)
            {
                Console.WriteLine(pad + "  ... (" + n.Children.Count + " items)");
                break;
            }
            Dump(n.Children[i], level + 1, maxDepth);
        }
    }

    static string ToHex(byte[] b)
    {
        StringBuilder sb = new StringBuilder(b.Length * 2);
        for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
        return sb.ToString();
    }
}
