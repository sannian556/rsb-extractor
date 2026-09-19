package com.apksmf.extractor;

import java.io.BufferedInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.nio.channels.FileChannel;
import java.util.ArrayList;
import java.util.List;
import java.util.zip.Inflater;
import java.util.zip.InflaterInputStream;

/**
 * 读 APK(ZIP) 的中央目录，并按条目读内容。
 *
 * 用 FileChannel 做随机访问（绝对读，不动 channel 自己的 position）——
 * Android 的 SAF 给出来的 fd 对本地文件是可 seek 的，所以拿得到中央目录就不用把整个包复制一份。
 * minSdk 24 起 java.nio.channels 是齐的，没必要引第三方 zip 库。
 */
public class ApkIndex {

    private static final long SIG_EOCD = 0x06054b50L;
    private static final long SIG_EOCD64 = 0x06064b50L;
    private static final long SIG_EOCD64_LOC = 0x07064b50L;
    private static final long SIG_CEN = 0x02014b50L;
    private static final long SIG_LOC = 0x04034b50L;

    private FileChannel ch;
    private long fileSize;
    private final List<ApkEntry> entries = new ArrayList<ApkEntry>();

    public List<ApkEntry> entries() { return entries; }
    public long fileSize() { return fileSize; }

    /** 关掉底层的 channel 并清空条目 */
    public void close() {
        if (ch != null) {
            try { ch.close(); } catch (IOException ignored) { }
            ch = null;
        }
        entries.clear();
    }

    /** 人类可读的大小 */
    public static String formatSize(long n) {
        if (n < 1024) return n + " B";
        if (n < 1024L * 1024) return String.format("%.1f KB", n / 1024.0);
        if (n < 1024L * 1024 * 1024) return String.format("%.2f MB", n / 1048576.0);
        return String.format("%.2f GB", n / 1073741824.0);
    }

    public void open(FileChannel channel, long size) throws IOException {
        ch = channel;
        fileSize = size;
        entries.clear();

        long[] out = new long[3];
        readEndRecords(out);
        long cenOffset = out[0];
        long count = out[2];

        long pos = cenOffset;
        for (long i = 0; i < count; i++) {
            byte[] head = readAt(pos, 46);
            if (le32(head, 0) != SIG_CEN) {
                throw new IOException("中央目录签名不对（第 " + i + " 条）");
            }
            ApkEntry e = new ApkEntry();
            e.flags = le16(head, 8);
            e.method = le16(head, 10);
            e.compressedSize = le32(head, 20);
            e.size = le32(head, 24);
            int nameLen = le16(head, 28);
            int extraLen = le16(head, 30);
            int commentLen = le16(head, 32);
            e.localHeaderOffset = le32(head, 42);

            e.name = decodeName(readAt(pos + 46, nameLen), e.flags);

            if (e.size == 0xFFFFFFFFL || e.compressedSize == 0xFFFFFFFFL
                    || e.localHeaderOffset == 0xFFFFFFFFL) {
                applyZip64(e, readAt(pos + 46 + nameLen, extraLen));
            }
            pos += 46 + nameLen + extraLen + commentLen;
            if (!e.isDirectory()) entries.add(e);
        }
    }

    /** 从文件尾往前找 EOCD；有 Zip64 记录就接着读 */
    private void readEndRecords(long[] out) throws IOException {
        if (fileSize < 22) throw new IOException("文件太小，不是 ZIP/APK");
        int scan = (int) Math.min(fileSize, 22 + 65535);
        long base = fileSize - scan;
        byte[] tail = readAt(base, scan);

        int at = -1;
        for (int i = scan - 22; i >= 0; i--) {
            if (le32(tail, i) == SIG_EOCD) { at = i; break; }
        }
        if (at < 0) throw new IOException("找不到 ZIP 结尾记录（EOCD）—— 这个包不是标准 zip 结构");

        long count = le16(tail, at + 10);
        long cenSize = le32(tail, at + 12);
        long cenOffset = le32(tail, at + 16);
        long eocdPos = base + at;

        if (eocdPos >= 20) {
            byte[] loc = readAt(eocdPos - 20, 20);
            if (le32(loc, 0) == SIG_EOCD64_LOC) {
                long p64 = le64(loc, 8);
                if (p64 > 0 && p64 + 56 <= fileSize) {
                    byte[] b64 = readAt(p64, 56);
                    if (le32(b64, 0) == SIG_EOCD64) {
                        count = le64(b64, 32);
                        cenSize = le64(b64, 40);
                        cenOffset = le64(b64, 48);
                    }
                }
            }
        }
        out[0] = cenOffset;
        out[1] = cenSize;
        out[2] = count;
    }

    private static void applyZip64(ApkEntry e, byte[] extra) {
        int p = 0;
        while (p + 4 <= extra.length) {
            int id = le16(extra, p);
            int len = le16(extra, p + 2);
            int body = p + 4;
            if (id == 0x0001) {
                int q = body;
                if (e.size == 0xFFFFFFFFL && q + 8 <= body + len) { e.size = le64(extra, q); q += 8; }
                if (e.compressedSize == 0xFFFFFFFFL && q + 8 <= body + len) { e.compressedSize = le64(extra, q); q += 8; }
                if (e.localHeaderOffset == 0xFFFFFFFFL && q + 8 <= body + len) { e.localHeaderOffset = le64(extra, q); }
                return;
            }
            p = body + len;
        }
    }

    /** 绝对读，不改变 channel 的 position */
    public byte[] readAt(long pos, int len) throws IOException {
        byte[] b = new byte[len];
        ByteBuffer buf = ByteBuffer.wrap(b);
        long p = pos;
        while (buf.hasRemaining()) {
            int n = ch.read(buf, p);
            if (n <= 0) throw new IOException("读取越过文件末尾（offset=" + p + "）");
            p += n;
        }
        return b;
    }

    /** 打开一个条目 —— stored 直接读，deflate 走 Inflater(true) 的裸 deflate */
    public InputStream openEntry(ApkEntry e) throws IOException {
        byte[] lh = readAt(e.localHeaderOffset, 30);
        if (le32(lh, 0) != SIG_LOC) {
            throw new IOException("条目 " + e.name + " 的本地头签名不对");
        }
        int nameLen = le16(lh, 26);
        int extraLen = le16(lh, 28);
        long dataStart = e.localHeaderOffset + 30 + nameLen + extraLen;

        // ★ 必须套缓冲：LZMA 解码器是**逐字节**读输入的，直接读 FileChannel 会变成
        //   "一次 read 一次系统调用" —— 实测解 init(416MB) 要 988 秒，
        //   套上 64KB 缓冲后只要几十秒。这是本工程最大的性能坑。
        InputStream raw = new BufferedInputStream(new RangeStream(ch, dataStart, e.compressedSize), 1 << 16);
        if (e.method == 0) return raw;
        if (e.method == 8) {
            return new BufferedInputStream(
                    new InflaterInputStream(raw, new Inflater(true), 1 << 16), 1 << 16);
        }
        throw new IOException("条目 " + e.name + " 用了不支持的压缩方式 " + e.method);
    }

    /** 只看开头几个字节（判断要不要解 RSLB 壳） */
    public byte[] peek(ApkEntry e, int n) throws IOException {
        InputStream in = openEntry(e);
        try {
            byte[] b = new byte[n];
            int got = 0;
            while (got < n) {
                int r = in.read(b, got, n - got);
                if (r <= 0) break;
                got += r;
            }
            return b;
        } finally {
            try { in.close(); } catch (IOException ignored) { }
        }
    }

    public void copyEntry(ApkEntry e, OutputStream out) throws IOException {
        InputStream in = openEntry(e);
        try {
            byte[] buf = new byte[1 << 16];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        } finally {
            try { in.close(); } catch (IOException ignored) { }
        }
    }

    static String decodeName(byte[] raw, int flags) {
        try {
            if ((flags & 0x800) != 0) return new String(raw, "UTF-8");
            String s = new String(raw, "UTF-8");
            if (s.indexOf('\uFFFD') < 0) return s;
            return new String(raw, "GBK");
        } catch (Exception ex) {
            return new String(raw);
        }
    }

    static int le16(byte[] b, int o) {
        return (b[o] & 0xFF) | ((b[o + 1] & 0xFF) << 8);
    }

    static long le32(byte[] b, int o) {
        return ((long) (b[o] & 0xFF))
                | ((long) (b[o + 1] & 0xFF) << 8)
                | ((long) (b[o + 2] & 0xFF) << 16)
                | ((long) (b[o + 3] & 0xFF) << 24);
    }

    static long le64(byte[] b, int o) {
        return le32(b, o) | (le32(b, o + 4) << 32);
    }

    /** 把文件里的一段范围包装成顺序读的 InputStream */
    static class RangeStream extends InputStream {
        private final FileChannel ch;
        private long pos;
        private final long end;

        RangeStream(FileChannel c, long start, long len) {
            ch = c;
            pos = start;
            end = start + len;
        }

        @Override
        public int read() throws IOException {
            byte[] one = new byte[1];
            int n = read(one, 0, 1);
            return n <= 0 ? -1 : (one[0] & 0xFF);
        }

        @Override
        public int read(byte[] b, int off, int len) throws IOException {
            if (pos >= end) return -1;
            if (len > end - pos) len = (int) (end - pos);
            if (len <= 0) return -1;
            ByteBuffer buf = ByteBuffer.wrap(b, off, len);
            int n = ch.read(buf, pos);
            if (n > 0) pos += n;
            return n;
        }

        @Override
        public int available() {
            long r = end - pos;
            return r > 0 ? (int) Math.min(r, 1 << 16) : 0;
        }

        @Override
        public long skip(long n) {
            if (n > end - pos) n = end - pos;
            if (n < 0) n = 0;
            pos += n;
            return n;
        }
    }
}
