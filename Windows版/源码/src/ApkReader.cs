using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ApkSmfExtractor
{
    /// <summary>APK(ZIP) 里的一个条目（只存元数据，不读内容）</summary>
    public class ApkEntry
    {
        public string Name;             // 条目全名，例如 assets/init.rsb.smf
        public long Size;               // 未压缩大小
        public long CompressedSize;     // 压缩后大小
        public int Method;              // 0 = stored, 8 = deflate
        public long LocalHeaderOffset;  // 本地文件头偏移
        public int Flags;

        public bool IsDirectory
        {
            get { return Name.Length > 0 && Name[Name.Length - 1] == '/'; }
        }

        public string BaseName
        {
            get
            {
                int i = Name.LastIndexOf('/');
                return i < 0 ? Name : Name.Substring(i + 1);
            }
        }

        /// <summary>需求：「解压出来的时候仅支持 .smf」—— 只有 .smf 结尾的才算可导出</summary>
        public bool IsSmf
        {
            get { return Name.EndsWith(".smf", StringComparison.OrdinalIgnoreCase); }
        }
    }

    /// <summary>把一段字节范围包装成只读流（DeflateStream 需要它来知道"到哪儿停"）</summary>
    internal class SubStream : Stream
    {
        private Stream _base;
        private long _start;
        private long _length;
        private long _pos;

        public SubStream(Stream baseStream, long start, long length)
        {
            _base = baseStream;
            _start = start;
            _length = length;
            _pos = 0;
            _base.Seek(_start, SeekOrigin.Begin);
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _length; } }

        public override long Position
        {
            get { return _pos; }
            set { throw new NotSupportedException(); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long remain = _length - _pos;
            if (remain <= 0) return 0;
            if (count > remain) count = (int)remain;
            int n = _base.Read(buffer, offset, count);
            if (n > 0) _pos += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    /// <summary>虚拟目录树节点（APK 里没有目录条目，要自己按 '/' 拼出来）</summary>
    public class ApkNode
    {
        public string Name;             // 单段名
        public string FullPath;         // 完整路径
        public bool IsDir;
        public ApkEntry Entry;          // 文件节点才有
        public List<ApkNode> Children = new List<ApkNode>();
        public ApkNode Parent;

        public long Size
        {
            get { return Entry == null ? 0 : Entry.Size; }
        }

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// APK(ZIP) 读取器 —— 自己解析中央目录。
    /// 不用 System.IO.Compression.ZipArchive：那是 .NET 4.5 才有的，XP 上用不了。
    /// 解压用 .NET 2.0 就自带的 DeflateStream。
    /// </summary>
    public class ApkReader : IDisposable
    {
        private FileStream _fs;
        private string _path;
        private List<ApkEntry> _entries = new List<ApkEntry>();
        private ApkNode _root;

        public string Path { get { return _path; } }
        public List<ApkEntry> Entries { get { return _entries; } }
        public ApkNode Root { get { return _root; } }
        public long FileSize { get { return _fs == null ? 0 : _fs.Length; } }

        private const uint SIG_EOCD = 0x06054b50;
        private const uint SIG_EOCD64 = 0x06064b50;
        private const uint SIG_EOCD64_LOC = 0x07064b50;
        private const uint SIG_CEN = 0x02014b50;
        private const uint SIG_LOC = 0x04034b50;

        public void Open(string path)
        {
            Close();
            _path = path;
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

            long cenOffset;
            long cenSize;
            long entryCount;
            ReadEndRecords(out cenOffset, out cenSize, out entryCount);

            _fs.Seek(cenOffset, SeekOrigin.Begin);
            byte[] head = new byte[46];
            for (long i = 0; i < entryCount; i++)
            {
                ReadExactly(head, 0, 46);
                uint sig = BitConverter.ToUInt32(head, 0);
                if (sig != SIG_CEN) throw new InvalidDataException("中央目录第 " + i + " 条签名不对（可能不是 ZIP/APK）");

                ApkEntry e = new ApkEntry();
                e.Flags = BitConverter.ToUInt16(head, 8);
                e.Method = BitConverter.ToUInt16(head, 10);
                e.CompressedSize = BitConverter.ToUInt32(head, 20);
                e.Size = BitConverter.ToUInt32(head, 24);
                int nameLen = BitConverter.ToUInt16(head, 28);
                int extraLen = BitConverter.ToUInt16(head, 30);
                int commentLen = BitConverter.ToUInt16(head, 32);
                e.LocalHeaderOffset = BitConverter.ToUInt32(head, 42);

                byte[] nameRaw = new byte[nameLen];
                ReadExactly(nameRaw, 0, nameLen);
                e.Name = DecodeName(nameRaw, e.Flags);

                // Zip64 扩展字段：值会是 0xFFFFFFFF，真实值按顺序排在 extra 里
                if (e.Size == 0xFFFFFFFFL || e.CompressedSize == 0xFFFFFFFFL || e.LocalHeaderOffset == 0xFFFFFFFFL
                    || (uint)e.LocalHeaderOffset == 0xFFFFFFFF)
                {
                    byte[] extra = new byte[extraLen];
                    ReadExactly(extra, 0, extraLen);
                    ApplyZip64(e, extra);
                }
                else if (extraLen > 0)
                {
                    Skip(extraLen);
                }
                if (commentLen > 0) Skip(commentLen);

                if (!e.IsDirectory) _entries.Add(e);
            }

            BuildTree();
        }

        private void ApplyZip64(ApkEntry e, byte[] extra)
        {
            int p = 0;
            while (p + 4 <= extra.Length)
            {
                int id = BitConverter.ToUInt16(extra, p);
                int len = BitConverter.ToUInt16(extra, p + 2);
                int body = p + 4;
                if (id == 0x0001)
                {
                    int q = body;
                    if (e.Size == 0xFFFFFFFFL && q + 8 <= body + len) { e.Size = BitConverter.ToInt64(extra, q); q += 8; }
                    if (e.CompressedSize == 0xFFFFFFFFL && q + 8 <= body + len) { e.CompressedSize = BitConverter.ToInt64(extra, q); q += 8; }
                    if ((uint)e.LocalHeaderOffset == 0xFFFFFFFF && q + 8 <= body + len) { e.LocalHeaderOffset = BitConverter.ToInt64(extra, q); q += 8; }
                    return;
                }
                p = body + len;
            }
        }

        private void ReadEndRecords(out long cenOffset, out long cenSize, out long entryCount)
        {
            long len = _fs.Length;
            if (len < 22) throw new InvalidDataException("文件太小，不是 ZIP/APK");
            int maxScan = (int)Math.Min(len, 22 + 65535);
            byte[] buf = new byte[maxScan];
            _fs.Seek(len - maxScan, SeekOrigin.Begin);
            ReadExactly(buf, 0, maxScan);

            int at = -1;
            for (int i = maxScan - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(buf, i) == SIG_EOCD) { at = i; break; }
            }
            if (at < 0) throw new InvalidDataException("找不到 ZIP 结尾记录（EOCD）");

            entryCount = BitConverter.ToUInt16(buf, at + 10);
            cenSize = BitConverter.ToUInt32(buf, at + 12);
            cenOffset = BitConverter.ToUInt32(buf, at + 16);
            long eocdPos = len - maxScan + at;

            // Zip64：EOCD 里填 0xFFFF/0xFFFFFFFF 时要读 Zip64 记录
            bool need64 = entryCount == 0xFFFF || cenSize == 0xFFFFFFFFL || cenOffset == 0xFFFFFFFFL;
            if (need64 || eocdPos >= 20)
            {
                long locPos = eocdPos - 20;
                if (locPos >= 0)
                {
                    byte[] loc = new byte[20];
                    _fs.Seek(locPos, SeekOrigin.Begin);
                    ReadExactly(loc, 0, 20);
                    if (BitConverter.ToUInt32(loc, 0) == SIG_EOCD64_LOC)
                    {
                        long eocd64Pos = BitConverter.ToInt64(loc, 8);
                        byte[] b64 = new byte[56];
                        _fs.Seek(eocd64Pos, SeekOrigin.Begin);
                        ReadExactly(b64, 0, 56);
                        if (BitConverter.ToUInt32(b64, 0) == SIG_EOCD64)
                        {
                            entryCount = BitConverter.ToInt64(b64, 32);
                            cenSize = BitConverter.ToInt64(b64, 40);
                            cenOffset = BitConverter.ToInt64(b64, 48);
                        }
                    }
                }
            }
        }

        private void Skip(int n)
        {
            if (n <= 0) return;
            _fs.Seek(n, SeekOrigin.Current);
        }

        private void ReadExactly(byte[] buf, int off, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = _fs.Read(buf, off + got, count - got);
                if (n <= 0) throw new EndOfStreamException("读取 APK 时意外结束");
                got += n;
            }
        }

        private static string DecodeName(byte[] raw, int flags)
        {
            if ((flags & 0x800) != 0) return Encoding.UTF8.GetString(raw);
            string s = Encoding.UTF8.GetString(raw);
            if (s.IndexOf('\uFFFD') < 0) return s;          // 是合法 UTF-8（APK 里基本都是 ASCII）
            return Encoding.Default.GetString(raw);          // 退回系统 ANSI
        }

        /// <summary>把扁平的条目列表拼成一棵虚拟目录树</summary>
        private void BuildTree()
        {
            _root = new ApkNode();
            _root.Name = System.IO.Path.GetFileName(_path);
            _root.FullPath = "";
            _root.IsDir = true;

            Dictionary<string, ApkNode> dirs = new Dictionary<string, ApkNode>(StringComparer.OrdinalIgnoreCase);
            dirs[""] = _root;

            for (int i = 0; i < _entries.Count; i++)
            {
                ApkEntry e = _entries[i];
                string[] parts = e.Name.Split('/');
                string acc = "";
                ApkNode parent = _root;
                for (int k = 0; k < parts.Length; k++)
                {
                    if (parts[k].Length == 0) continue;
                    acc = acc.Length == 0 ? parts[k] : acc + "/" + parts[k];
                    bool last = (k == parts.Length - 1);
                    ApkNode node;
                    if (last)
                    {
                        node = new ApkNode();
                        node.Name = parts[k];
                        node.FullPath = acc;
                        node.IsDir = false;
                        node.Entry = e;
                        node.Parent = parent;
                        parent.Children.Add(node);
                    }
                    else
                    {
                        if (!dirs.TryGetValue(acc, out node))
                        {
                            node = new ApkNode();
                            node.Name = parts[k];
                            node.FullPath = acc;
                            node.IsDir = true;
                            node.Parent = parent;
                            parent.Children.Add(node);
                            dirs[acc] = node;
                        }
                        parent = node;
                    }
                }
            }
            SortTree(_root);
        }

        private static void SortTree(ApkNode n)
        {
            n.Children.Sort(CompareNode);
            for (int i = 0; i < n.Children.Count; i++) SortTree(n.Children[i]);
        }

        private static int CompareNode(ApkNode a, ApkNode b)
        {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;   // 目录排前面
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>打开一个条目，返回可顺序读取的流（stored 直接读，deflate 走 DeflateStream）</summary>
        public Stream OpenEntry(ApkEntry e)
        {
            _fs.Seek(e.LocalHeaderOffset, SeekOrigin.Begin);
            byte[] lh = new byte[30];
            ReadExactly(lh, 0, 30);
            if (BitConverter.ToUInt32(lh, 0) != SIG_LOC)
                throw new InvalidDataException("条目 " + e.Name + " 的本地头签名不对");
            int nameLen = BitConverter.ToUInt16(lh, 26);
            int extraLen = BitConverter.ToUInt16(lh, 28);
            long dataStart = e.LocalHeaderOffset + 30 + nameLen + extraLen;

            SubStream raw = new SubStream(_fs, dataStart, e.CompressedSize);
            if (e.Method == 0) return raw;                                  // stored
            if (e.Method == 8) return new DeflateStream(raw, CompressionMode.Decompress);
            throw new NotSupportedException("条目 " + e.Name + " 用了不支持的压缩方式 " + e.Method);
        }

        /// <summary>把条目内容全部读成 byte[]（供小的 .smf 用；RSLB 走流式）</summary>
        public byte[] ReadAll(ApkEntry e)
        {
            using (Stream s = OpenEntry(e))
            {
                MemoryStream ms = new MemoryStream(e.Size > 0 && e.Size < int.MaxValue ? (int)e.Size : 1 << 16);
                byte[] buf = new byte[1 << 16];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            }
        }

        /// <summary>把条目内容原样复制到目标流</summary>
        public void CopyTo(ApkEntry e, Stream dest)
        {
            using (Stream s = OpenEntry(e))
            {
                byte[] buf = new byte[1 << 20];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) dest.Write(buf, 0, n);
            }
        }

        public void Close()
        {
            if (_fs != null) { try { _fs.Close(); } catch { } _fs = null; }
            _entries.Clear();
            _root = null;
        }

        public void Dispose() { Close(); }
    }
}
