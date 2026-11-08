using System;
using System.Collections.Generic;
using System.IO;
using SevenZip.Compression.LZMA;

namespace ApkSmfExtractor
{
    public class RsbBlock
    {
        public long UncompressedOffset;
        public long UncompressedSize;
        public long CompressedOffset;
        public int CompressedSize;
    }

    public class RsbInfo
    {
        public int Version;
        public long UncompressedTotal;
        public long CompressedTotal;
        public int BlockSize;
        public int BlockCount;
        public List<RsbBlock> Blocks = new List<RsbBlock>();

        public string Describe()
        {
            return string.Format("RSB 容器 v{0}  未压缩总量={1:N0}  压缩区={2:N0}  块={3} x {4:N0}",
                Version, UncompressedTotal, CompressedTotal, BlockCount, BlockSize);
        }
    }

    /// <summary>
    /// RSB 容器解包器 —— PvZ2 中国版 APK 里 assets/*.rsb.smf 的那层壳。
    ///
    /// 格式（实测自 植物大战僵尸2 4.2.4，33 个包全部通过校验）：
    ///   头 32 字节: "RSLB" | u32 版本=1 | u64 未压缩总大小 | u64 压缩区大小 | u32 块大小 | u32 块数
    ///   块表 N*36 : u64 未压缩偏移 | u64 未压缩大小 | u64 压缩偏移 | u32 压缩大小 | u32 props | u32 dict
    ///   每块数据  : 5 字节 LZMA 头(props + 字典大小) + raw LZMA1 流
    ///
    /// 注意两点：
    ///   1) raw LZMA 流没有结束标记，所以必须把"未压缩大小"喂给解码器，让它精确停在那个位置；
    ///   2) 字典字段实测有 0 的情况，为 0 时按 32MB 兜底（字典只影响回溯窗口，放大是安全的）。
    /// </summary>
    public static class RsbUnpacker
    {
        public const int HeaderSize = 32;
        public const int BlockRecSize = 36;

        /// <summary>字节进度回调：bytesSoFar / 头部声明的解出总量</summary>
        public delegate void UnpackProgress(long bytesSoFar, long total);

        public static bool IsRsbHead(byte[] head4)
        {
            return head4 != null && head4.Length >= 4
                && head4[0] == (byte)'R' && head4[1] == (byte)'S'
                && head4[2] == (byte)'L' && head4[3] == (byte)'B';
        }

        public static bool IsPlainRsbHead(byte[] head4)
        {
            return head4 != null && head4.Length >= 4
                && head4[0] == 0x31 && head4[1] == 0x62 && head4[2] == 0x73 && head4[3] == 0x72;
        }

        /// <summary>只读头部和块表，用于预览/诊断</summary>
        public static RsbInfo ReadInfo(Stream input)
        {
            byte[] head = new byte[HeaderSize];
            ReadExactly(input, head, 0, HeaderSize);
            if (!IsRsbHead(head)) throw new InvalidDataException("不是 RSB 容器文件");

            RsbInfo info = new RsbInfo();
            info.Version = BitConverter.ToInt32(head, 4);
            info.UncompressedTotal = BitConverter.ToInt64(head, 8);
            info.CompressedTotal = BitConverter.ToInt64(head, 16);
            info.BlockSize = BitConverter.ToInt32(head, 24);
            info.BlockCount = BitConverter.ToInt32(head, 28);
            if (info.BlockCount < 0 || info.BlockCount > 100000) throw new InvalidDataException("块数异常：" + info.BlockCount);

            byte[] tbl = new byte[BlockRecSize * info.BlockCount];
            ReadExactly(input, tbl, 0, tbl.Length);
            for (int i = 0; i < info.BlockCount; i++)
            {
                int o = i * BlockRecSize;
                RsbBlock b = new RsbBlock();
                b.UncompressedOffset = BitConverter.ToInt64(tbl, o);
                b.UncompressedSize = BitConverter.ToInt64(tbl, o + 8);
                b.CompressedOffset = BitConverter.ToInt64(tbl, o + 16);
                b.CompressedSize = BitConverter.ToInt32(tbl, o + 24);
                info.Blocks.Add(b);
            }
            return info;
        }

        public static bool Unpack(Stream input, Stream output, out long written, out string error)
        {
            return Unpack(input, output, out written, out error, null);
        }

        /// <summary>
        /// 把 RSB 容器流解成明文 rsb1 写到 output。
        /// 全程只做顺序读取，所以 APK 里那种不可 Seek 的流也能直接用。
        /// </summary>
        public static bool Unpack(Stream input, Stream output, out long written, out string error,
                                  UnpackProgress progress)
        {
            written = 0;
            error = null;
            try
            {
                RsbInfo info = ReadInfo(input);

                long expectPos = HeaderSize + (long)BlockRecSize * info.BlockCount;
                long total = 0;
                Decoder dec = new Decoder();
                byte[] props = new byte[5];

                for (int i = 0; i < info.BlockCount; i++)
                {
                    RsbBlock b = info.Blocks[i];
                    if (b.CompressedOffset != expectPos)
                        throw new InvalidDataException(string.Format(
                            "第 {0} 块偏移不连续（表里 {1}，实际应在 {2}）", i, b.CompressedOffset, expectPos));
                    if (b.CompressedSize < 5)
                        throw new InvalidDataException("第 " + i + " 块数据太短");

                    byte[] comp = new byte[b.CompressedSize];
                    ReadExactly(input, comp, 0, comp.Length);
                    expectPos += b.CompressedSize;

                    props[0] = comp[0];
                    props[1] = comp[1];
                    props[2] = comp[2];
                    props[3] = comp[3];
                    props[4] = comp[4];
                    uint dict = (uint)(comp[1] | (comp[2] << 8) | (comp[3] << 16) | (comp[4] << 24));
                    if (dict < 4096)
                    {
                        // 实测有包的字典字段是 0；字典只是回溯窗口，放大不影响结果
                        props[1] = 0; props[2] = 0; props[3] = 0; props[4] = 0x02;   // 32 MB
                    }

                    dec.SetDecoderProperties(props);
                    MemoryStream ms = new MemoryStream(comp, 5, comp.Length - 5, false, false);
                    dec.Code(ms, output, comp.Length - 5, b.UncompressedSize, null);

                    total += b.UncompressedSize;
                    written = total;
                    if (progress != null) progress(total, info.UncompressedTotal);
                }

                if (total != info.UncompressedTotal)
                    throw new InvalidDataException(string.Format(
                        "解出总大小 {0:N0} 与头部声明 {1:N0} 不符", total, info.UncompressedTotal));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void ReadExactly(Stream s, byte[] buf, int off, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, off + got, count - got);
                if (n <= 0) throw new EndOfStreamException("读取 RSB 容器数据时意外结束");
                got += n;
            }
        }
    }
}
