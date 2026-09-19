using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ApkSmfExtractor
{
    /// <summary>
    /// 选文件夹对话框：
    ///   · Vista 及以上 → IFileDialog + FOS_PICKFOLDERS，就是资源管理器那个"选择文件夹"（图1 那种样式）；
    ///   · XP           → 回退到 FolderBrowserDialog（XP 根本没有 IFileDialog，只能给老式树形框）。
    ///
    /// 下面这些 COM 声明的坑是在本机实测过的，别改：
    ///   · IFileDialog 的 vtable 顺序必须严格按 shobjidl_core.h，一个都不能少、不能换位；
    ///   · 用 [ComImport] 的 coclass 声明 + new 直接建对象，比 Activator.CreateInstance 干净；
    ///   · 路径用 SIGDN_FILESYSPATH 取，拿到的是真实文件系统路径。
    /// </summary>
    internal static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close([MarshalAs(UnmanagedType.Error)] int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        public static string LastError = "";

        /// <summary>弹出选文件夹对话框；用户取消返回 null。</summary>
        public static string Pick(IntPtr owner, string initialDir)
        {
            LastError = "";
            if (Environment.OSVersion.Version.Major >= 6)
            {
                try { return PickVista(owner, initialDir); }
                catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; }
            }
            return PickLegacy(initialDir);   // XP 或 Vista 接口不可用时
        }

        private static string PickVista(IntPtr owner, string initialDir)
        {
            IFileDialog dlg = null;
            IShellItem item = null;
            try
            {
                dlg = (IFileDialog)new FileOpenDialogRCW();
                uint opts;
                dlg.GetOptions(out opts);
                opts |= FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
                dlg.SetOptions(opts);
                dlg.SetTitle("选择文件夹");
                dlg.SetOkButtonLabel("选择此文件夹");

                if (!string.IsNullOrEmpty(initialDir))
                {
                    try
                    {
                        Guid iid = typeof(IShellItem).GUID;
                        IShellItem start;
                        if (SHCreateItemFromParsingName(initialDir, IntPtr.Zero, ref iid, out start) == 0 && start != null)
                        {
                            dlg.SetFolder(start);
                            Marshal.ReleaseComObject(start);
                        }
                    }
                    catch { }
                }

                int hr = dlg.Show(owner);
                if (hr != 0) return null;            // 用户取消
                dlg.GetResult(out item);
                if (item == null) return null;
                string path;
                item.GetDisplayName(SIGDN_FILESYSPATH, out path);
                return path;
            }
            finally
            {
                if (item != null) { try { Marshal.ReleaseComObject(item); } catch { } }
                if (dlg != null) { try { Marshal.ReleaseComObject(dlg); } catch { } }
            }
        }

        private static string PickLegacy(string initialDir)
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = "选择文件夹";
            d.ShowNewFolderButton = true;
            if (!string.IsNullOrEmpty(initialDir) && System.IO.Directory.Exists(initialDir))
                d.SelectedPath = initialDir;
            return d.ShowDialog() == DialogResult.OK ? d.SelectedPath : null;
        }
    }
}
