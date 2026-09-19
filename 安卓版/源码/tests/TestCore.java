import com.apksmf.extractor.ApkEntry;
import com.apksmf.extractor.ApkIndex;
import com.apksmf.extractor.Exporter;
import com.apksmf.extractor.RslbUnpacker;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.OutputStream;
import java.nio.channels.FileChannel;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.List;

/**
 * PC 端核心逻辑测试（不需要 Android）：
 *   java TestCore <apk> <临时输出目录>
 *
 * 验证点：
 *   1) ZIP 中央目录解析 —— 条目数和 Windows 版一致（1353）
 *   2) RSLB 解包 —— init.rsb.smf 解出 573,800,448 字节且 MD5 与 Windows 版一模一样
 *   3) 小包 dino.rsb.smf 解出 21,483,520 字节
 *   4) Exporter 走通（含非 .smf 计失败）
 */
public class TestCore {

    static int fail = 0;

    static void check(boolean ok, String what) {
        System.out.println((ok ? "  [OK]   " : "  [FAIL] ") + what);
        if (!ok) fail++;
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 2) {
            System.out.println("usage: TestCore <apk> <tmpdir>");
            System.exit(2);
        }
        File apkFile = new File(args[0]);
        File outDir = new File(args[1]);
        if (!outDir.exists()) outDir.mkdirs();

        FileChannel ch = new FileInputStream(apkFile).getChannel();
        ApkIndex apk = new ApkIndex();
        apk.open(ch, apkFile.length());

        System.out.println("opened: " + apkFile.getName() + "  " + apkFile.length() + " bytes");
        System.out.println("entries: " + apk.entries().size());
        check(apk.entries().size() == 1353, "条目数 = 1353（实得 " + apk.entries().size() + "）");

        int smf = 0;
        for (ApkEntry e : apk.entries()) if (e.isSmf()) smf++;
        check(smf == 89, ".smf 文件数 = 89（实得 " + smf + "）");

        ApkEntry init = find(apk, "assets/init.rsb.smf");
        check(init != null, "找得到 assets/init.rsb.smf");
        if (init != null) {
            byte[] h = apk.peek(init, 4);
            check(RslbUnpacker.isRslb(h), "init 开头是 RSLB");

            long t0 = System.currentTimeMillis();
            File out = new File(outDir, "init.rsb.smf");
            OutputStream os = new FileOutputStream(out);
            long written;
            try {
                written = RslbUnpacker.unpack(apk.openEntry(init), os, null);
            } finally {
                os.close();
            }
            long ms = System.currentTimeMillis() - t0;
            check(written == 573800448L, "init 解出 573,800,448 字节（实得 " + written + "），用时 " + ms + " ms");
            String md5 = md5Of(out);
            check("0bfef422ff98059562f1c9f514c1eedf".equals(md5), "init 的 MD5 对上（实得 " + md5 + "）");
            out.delete();
        }

        ApkEntry dino = find(apk, "assets/dino.rsb.smf");
        check(dino != null, "找得到 assets/dino.rsb.smf");
        if (dino != null) {
            File out = new File(outDir, "dino.rsb.smf");
            OutputStream os = new FileOutputStream(out);
            long written;
            try {
                written = RslbUnpacker.unpack(apk.openEntry(dino), os, null);
            } finally {
                os.close();
            }
            check(written == 21483520L, "dino 解出 21,483,520 字节（实得 " + written + "）");
            out.delete();
        }

        // Exporter 走一遍：2 个 smf + 1 个非 smf
        List<ApkEntry> picked = new ArrayList<ApkEntry>();
        picked.add(find(apk, "assets/dino.rsb.smf"));
        picked.add(find(apk, "assets/ConfigManifest.json.smf"));
        picked.add(find(apk, "assets/wechaticon.png"));

        Exporter.Outcome o = Exporter.export(apk, picked, new Exporter.Sink() {
            public OutputStream create(String fileName) throws java.io.IOException {
                return new FileOutputStream(new File(outDir, fileName));
            }
        }, null);

        System.out.println("export: success=" + o.success + " failed=" + o.failed);
        for (String d : o.details) System.out.println("   fail: " + d);
        check(o.success == 2, "导出成功 2 个（实得 " + o.success + "）");
        check(o.failed == 1, "导出失败 1 个（png 不是 smf，实得 " + o.failed + "）");
        check(new File(outDir, "dino.rsb.smf").exists(), "dino.rsb.smf 已写出");
        check(new File(outDir, "ConfigManifest.json.smf").exists(), "明文 smf 已写出");

        System.out.println(fail == 0 ? "ALL PASSED" : ("FAILED: " + fail));
        System.exit(fail == 0 ? 0 : 1);
    }

    static ApkEntry find(ApkIndex apk, String name) {
        for (ApkEntry e : apk.entries()) {
            if (e.name.equalsIgnoreCase(name)) return e;
        }
        return null;
    }

    static String md5Of(File f) throws Exception {
        MessageDigest md = MessageDigest.getInstance("MD5");
        FileInputStream in = new FileInputStream(f);
        try {
            byte[] buf = new byte[1 << 20];
            int n;
            while ((n = in.read(buf)) > 0) md.update(buf, 0, n);
        } finally {
            in.close();
        }
        byte[] h = md.digest();
        StringBuilder sb = new StringBuilder();
        for (byte b : h) sb.append(String.format("%02x", b));
        return sb.toString();
    }
}
