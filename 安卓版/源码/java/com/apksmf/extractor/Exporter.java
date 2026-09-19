package com.apksmf.extractor;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.List;

/**
 * 导出逻辑（和界面分开，方便在 PC 上跑单测）。
 *
 * 规则（按需求）：
 *   · 只导出 .smf 结尾的文件；其它类型记为失败并说明原因；
 *   · .smf 里如果是 RSLB 外壳 → 解包成明文 rsb1 再写；本来就是明文的 → 原样导出；
 *   · 写文件走 Sink 接口 —— Android 端用 SAF 的 DocumentsContract 实现，PC 测试用 FileOutputStream。
 */
public class Exporter {

    public static class Outcome {
        public int success;
        public int failed;
        public final List<String> details = new ArrayList<String>();
    }

    /** 目标文件的创建方式 */
    public interface Sink {
        OutputStream create(String fileName) throws IOException;
    }

    public interface Progress {
        void onFile(int index, int total, String name);
    }

    public static Outcome export(ApkIndex apk, List<ApkEntry> picked, Sink sink, Progress cb) {
        Outcome r = new Outcome();
        for (int i = 0; i < picked.size(); i++) {
            ApkEntry e = picked.get(i);
            if (cb != null) cb.onFile(i + 1, picked.size(), e.baseName());
            String err = exportOne(apk, e, sink);
            if (err == null) {
                r.success++;
            } else {
                r.failed++;
                r.details.add(e.baseName() + "：" + err);
            }
        }
        return r;
    }

    /** 成功返回 null，失败返回原因 */
    public static String exportOne(ApkIndex apk, ApkEntry e, Sink sink) {
        OutputStream out = null;
        try {
            if (!e.isSmf()) return "不是 .smf 文件（本工具只导出 .smf）";

            byte[] head4 = apk.peek(e, 4);
            out = sink.create(e.baseName());

            if (RslbUnpacker.isRslb(head4)) {
                InputStream in = apk.openEntry(e);
                try {
                    RslbUnpacker.unpack(in, out, null);
                } finally {
                    try { in.close(); } catch (IOException ignored) { }
                }
            } else {
                apk.copyEntry(e, out);
            }
            out.flush();
            return null;
        } catch (Throwable ex) {
            String m = ex.getMessage();
            return (m == null || m.length() == 0) ? ex.getClass().getSimpleName() : m;
        } finally {
            if (out != null) {
                try { out.close(); } catch (IOException ignored) { }
            }
        }
    }

    /** 同一目录里重名时自动加序号：dino.rsb.smf -> dino_2.rsb.smf（PC 端测试用） */
    public static String uniqueName(java.io.File dir, String name) {
        java.io.File f = new java.io.File(dir, name);
        if (!f.exists()) return name;
        String tail = "";
        String body = name;
        if (name.length() > 4 && name.toLowerCase().endsWith(".smf")) {
            tail = name.substring(name.length() - 4);
            body = name.substring(0, name.length() - 4);
        }
        int dot = body.lastIndexOf('.');
        String stem = dot > 0 ? body.substring(0, dot) : body;
        String inner = dot > 0 ? body.substring(dot) : "";
        for (int i = 2; i < 10000; i++) {
            String cand = stem + "_" + i + inner + tail;
            if (!new java.io.File(dir, cand).exists()) return cand;
        }
        return name;
    }
}
