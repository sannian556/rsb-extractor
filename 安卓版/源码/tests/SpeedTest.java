import com.apksmf.extractor.*;
import java.io.*;
import java.nio.channels.FileChannel;

public class SpeedTest {
    public static void main(String[] a) throws Exception {
        File f = new File(a[0]);
        FileChannel ch = new FileInputStream(f).getChannel();
        ApkIndex apk = new ApkIndex();
        apk.open(ch, f.length());
        ApkEntry e = null;
        for (ApkEntry x : apk.entries()) if (x.name.equals(a[1])) e = x;
        if (e == null) { System.out.println("not found"); return; }
        long t0 = System.currentTimeMillis();
        OutputStream os = new FileOutputStream(new File(a[2], "out.bin"));
        long n;
        try { n = RslbUnpacker.unpack(apk.openEntry(e), os, null); } finally { os.close(); }
        long ms = System.currentTimeMillis() - t0;
        System.out.println(e.baseName() + " -> " + n + " bytes in " + ms + " ms  (" + (n/1048576.0/(ms/1000.0)) + " MB/s)");
    }
}