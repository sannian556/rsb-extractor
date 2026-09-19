package com.apksmf.extractor;

import SevenZip.Compression.LZMA.Decoder;

import java.io.ByteArrayInputStream;
import java.io.EOFException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * RSLB 解包器 —— PvZ2 中国版 APK 里 assets/*.rsb.smf 外面那层壳。
 *
 * 格式（实测自 植物大战僵尸2 4.2.4，33 个包全部通过校验）：
 *   头 32 字节: "RSLB" | u32 版本 | u64 未压缩总大小 | u64 压缩区大小 | u32 块大小 | u32 块数
 *   块表 N*36 : u64 未压缩偏移 | u64 未压缩大小 | u64 压缩偏移 | u32 压缩大小 | u32 props | u32 dict
 *   每块数据  : 5 字节 LZMA 头（props + 字典）+ raw LZMA1 流
 *
 * 两个要点：
 *   1) raw LZMA 流没有结束标记，必须把"未压缩大小"喂给解码器，让它精确停在那个位置；
 *   2) 字典字段实测有 0 的情况，为 0 时按 32MB 兜底（字典只是回溯窗口，放大不影响结果）。
 *
 * 内存：压缩数据不整块读进来，直接把"跳掉 5 字节头的受限流"交给解码器，
 * 峰值就是解码器自己那 32MB 窗口。
 */
public class RslbUnpacker {

    public interface Progress {
        void onBlock(int index, int total, long bytesSoFar);
    }

    public static final int HEADER_SIZE = 32;
    public static final int BLOCK_REC_SIZE = 36;

    public static boolean isRslb(byte[] head) {
        return head != null && head.length >= 4
                && head[0] == 'R' && head[1] == 'S' && head[2] == 'L' && head[3] == 'B';
    }

    /** 解 RSLB 写到 out，返回写出的字节数 */
    public static long unpack(InputStream in, OutputStream out, Progress cb) throws IOException {
        byte[] head = readFully(in, HEADER_SIZE);
        if (!isRslb(head)) throw new IOException("不是 RSLB 文件（开头不是 RSLB）");

        long uncompressedTotal = le64(head, 8);
        long blockCount = le32(head, 28);
        if (blockCount <= 0 || blockCount > 100000) {
            throw new IOException("块数异常：" + blockCount);
        }

        int n = (int) blockCount;
        byte[] tbl = readFully(in, BLOCK_REC_SIZE * n);
        long[] usizes = new long[n];
        long[] coffs = new long[n];
        int[] cszes = new int[n];
        for (int i = 0; i < n; i++) {
            int o = i * BLOCK_REC_SIZE;
            usizes[i] = le64(tbl, o + 8);
            coffs[i] = le64(tbl, o + 16);
            cszes[i] = (int) le32(tbl, o + 24);
        }

        long expectPos = HEADER_SIZE + (long) BLOCK_REC_SIZE * n;
        long total = 0;
        Decoder dec = new Decoder();
        byte[] props = new byte[5];

        for (int i = 0; i < n; i++) {
            if (coffs[i] != expectPos) {
                throw new IOException("第 " + i + " 块偏移不连续（表里 " + coffs[i] + "，应在 " + expectPos + "）");
            }
            if (cszes[i] < 5) throw new IOException("第 " + i + " 块数据太短");

            // 只读 5 字节 LZMA 头，剩下的直接流给解码器
            readFully(in, props, 5);
            long dict = le32(props, 1);
            if (dict < 4096) {
                props[1] = 0; props[2] = 0; props[3] = 0; props[4] = 0x02;   // 32MB
            }
            if (!dec.SetDecoderProperties(props)) {
                throw new IOException("第 " + i + " 块的 LZMA 参数无效");
            }

            LimitedStream body = new LimitedStream(in, cszes[i] - 5);
            // Java 版 SDK 的签名是 Code(in, out, outSize)，返回 boolean
            if (!dec.Code(body, out, usizes[i])) {
                throw new IOException("第 " + i + " 块解码失败");
            }
            body.drain();

            expectPos += cszes[i];
            total += usizes[i];
            if (cb != null) cb.onBlock(i + 1, n, total);
        }

        if (total != uncompressedTotal) {
            throw new IOException("解出总大小 " + total + " 与头部声明 " + uncompressedTotal + " 不符");
        }
        return total;
    }

    static byte[] readFully(InputStream in, int len) throws IOException {
        byte[] b = new byte[len];
        readFully(in, b, len);
        return b;
    }

    static void readFully(InputStream in, byte[] b, int len) throws IOException {
        int got = 0;
        while (got < len) {
            int r = in.read(b, got, len - got);
            if (r <= 0) throw new EOFException("读取 RSLB 数据时意外结束");
            got += r;
        }
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

    /** 限定读 n 个字节的子流；解码器不一定会把输入读完，所以要能主动 drain 掉 */
    static class LimitedStream extends InputStream {
        private final InputStream in;
        private long left;

        LimitedStream(InputStream in, long n) {
            this.in = in;
            this.left = n;
        }

        void drain() throws IOException {
            byte[] buf = new byte[8192];
            while (left > 0) {
                int n = read(buf, 0, buf.length);
                if (n <= 0) break;
            }
        }

        @Override
        public int read() throws IOException {
            if (left <= 0) return -1;
            int c = in.read();
            if (c >= 0) left--;
            return c;
        }

        @Override
        public int read(byte[] b, int off, int len) throws IOException {
            if (left <= 0) return -1;
            if (len > left) len = (int) left;
            int n = in.read(b, off, len);
            if (n > 0) left -= n;
            return n;
        }

        @Override
        public int available() {
            return left > 0 ? (int) Math.min(left, 1 << 16) : 0;
        }
    }

    /** 方便单测：从 byte[] 解 */
    public static byte[] unpackBytes(byte[] data) throws IOException {
        java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream();
        unpack(new ByteArrayInputStream(data), bos, null);
        return bos.toByteArray();
    }
}
