using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ApkSmfExtractor
{
    /// <summary>
    /// 按扩展名向 Windows 要图标 —— 和资源管理器里显示的是同一套。
    ///
    /// APK 里的文件并不在磁盘上，所以走 SHGFI_USEFILEATTRIBUTES：只把"扩展名"告诉系统，
    /// 让它按文件关联返回图标（.png 给图片图标、.xml 给 XML 图标、没关联的给白纸图标）。
    ///
    /// 两个必须记住的点：
    ///   · SHGetFileInfo 返回的是 HICON，**必须先 Icon.FromHandle(...).Clone() 变成托管图标，
    ///     再 DestroyIcon**；顺序反了图标会失效或泄漏；
    ///   · ImageList 一旦加过图片就不能再改 ImageSize，所以尺寸要在建表时定好。
    /// </summary>
    internal static class ShellIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        private const int SMALL = 16;
        private const int LARGE = 32;

        private class Bucket
        {
            public ImageList List;
        }

        private static Bucket _small;
        private static Bucket _large;

        /// <summary>小图标表（16×16）—— 详细信息视图和左边目录树共用</summary>
        public static ImageList SmallList
        {
            get { Ensure(); return _small.List; }
        }

        /// <summary>大图标表（32×32）—— 大图标视图用</summary>
        public static ImageList LargeList
        {
            get { Ensure(); return _large.List; }
        }

        private static void Ensure()
        {
            if (_small != null) return;
            _small = new Bucket();
            _small.List = MakeList(SMALL);
            _large = new Bucket();
            _large.List = MakeList(LARGE);
        }

        private static ImageList MakeList(int size)
        {
            ImageList l = new ImageList();
            l.ImageSize = new Size(size, size);
            l.ColorDepth = ColorDepth.Depth32Bit;
            l.TransparentColor = Color.Transparent;
            return l;
        }

        private static string ExtOf(string name)
        {
            int i = name.LastIndexOf('.');
            if (i < 0 || i == name.Length - 1) return "";
            return name.Substring(i + 1).ToLowerInvariant();
        }

        /// <summary>
        /// 确保两种尺寸的 ImageList 里都有这个类型（文件夹 / 某个扩展名）的图标，返回 ImageKey。
        /// 用 key 而不是下标：ListView 的 SmallImageList 与 LargeImageList 是两张表，
        /// 只有 key 才能保证"详细信息"和"大图标"两种视图取到的是同一个类型的图标。
        /// 取不到图标时返回空串（ListView 对空 Key 就是"不显示图标"）。
        /// </summary>
        public static string GetKey(string fileName, bool isDir)
        {
            Ensure();
            string key = isDir ? "<dir>" : ("." + ExtOf(fileName));
            if (_small.List.Images.ContainsKey(key)) return key;

            if (!isDir && key == ".smf")
            {
                // .smf 在系统里没有关联（默认给白纸图标），自己画一个"资源包"样式的
                _small.List.Images.Add(key, MakeSmfIcon(SMALL));
                _large.List.Images.Add(key, MakeSmfIcon(LARGE));
                return key;
            }

            AddOne(_small, key, isDir, SMALL);
            AddOne(_large, key, isDir, LARGE);

            return _small.List.Images.ContainsKey(key) ? key : "";
        }

        /// <summary>给 .smf 画的图标：蓝色圆角方块 + 两道白杠（一个"包"的样子）</summary>
        private static Bitmap MakeSmfIcon(int size)
        {
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size / 16f;

                RectangleF r = new RectangleF(1f * s, 1f * s, 14f * s, 14f * s);
                float rad = 3f * s;
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddArc(r.Left, r.Top, rad, rad, 180, 90);
                    p.AddArc(r.Right - rad, r.Top, rad, rad, 270, 90);
                    p.AddArc(r.Right - rad, r.Bottom - rad, rad, rad, 0, 90);
                    p.AddArc(r.Left, r.Bottom - rad, rad, rad, 90, 90);
                    p.CloseFigure();
                    using (LinearGradientBrush br = new LinearGradientBrush(r,
                        Color.FromArgb(255, 92, 152, 242), Color.FromArgb(255, 38, 92, 186), 60f))
                        g.FillPath(br, p);
                }

                using (Pen pen = new Pen(Color.White, Math.Max(1f, 1.5f * s)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, 4f * s, 6f * s, 12f * s, 6f * s);
                    g.DrawLine(pen, 4f * s, 10f * s, 12f * s, 10f * s);
                }
            }
            return bmp;
        }

        private static void AddOne(Bucket b, string key, bool isDir, int size)
        {
            Icon ic = Fetch(isDir, key, size > SMALL);
            Bitmap bmp = Resize(ic, size);
            if (ic != null) ic.Dispose();
            if (bmp == null) return;

            b.List.Images.Add(key, bmp);
            // ★ 这里绝对不能 Dispose(bmp)：ImageList 要到 CreateHandle() 时才真正去读像素，
            //   提前释放会让它抛 ArgumentException("参数无效", 来自 Image.get_Width)，
            //   而 TreeView/ListView 建句柄时就炸 —— 整个界面直接空白。交给 ImageList 自己管。
        }

        private static Icon Fetch(bool isDir, string key, bool large)
        {
            SHFILEINFO fi = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
            uint attr = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            string probe = isDir ? "folder" : ("probe" + key);   // 系统只看扩展名

            IntPtr r;
            try { r = SHGetFileInfo(probe, attr, ref fi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), flags); }
            catch { return null; }

            if (r == IntPtr.Zero || fi.hIcon == IntPtr.Zero) return null;
            try
            {
                // 一定要 Clone：克隆出来的是托管图标，之后才能安全 DestroyIcon
                using (Icon tmp = Icon.FromHandle(fi.hIcon))
                    return (Icon)tmp.Clone();
            }
            catch { return null; }
            finally { DestroyIcon(fi.hIcon); }
        }

        private static Bitmap Resize(Icon ic, int size)
        {
            if (ic == null) return null;
            Bitmap outBmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(outBmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                using (Bitmap src = ic.ToBitmap())
                    g.DrawImage(src, new Rectangle(0, 0, size, size));
            }
            return outBmp;
        }
    }
}
