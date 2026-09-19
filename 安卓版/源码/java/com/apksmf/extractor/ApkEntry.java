package com.apksmf.extractor;

/** APK(ZIP) 里的一个条目 */
public class ApkEntry {
    public String name;
    public long size;              // 未压缩大小
    public long compressedSize;    // 压缩后大小
    public int method;             // 0 = stored, 8 = deflate
    public long localHeaderOffset;
    public int flags;

    public boolean isDirectory() {
        return name.length() > 0 && name.charAt(name.length() - 1) == '/';
    }

    public String baseName() {
        int i = name.lastIndexOf('/');
        return i < 0 ? name : name.substring(i + 1);
    }

    /** 需求：只有 .smf 才能导出 */
    public boolean isSmf() {
        return name.toLowerCase().endsWith(".smf");
    }
}
