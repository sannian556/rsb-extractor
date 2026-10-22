using System;
using System.Reflection;
using System.Runtime.InteropServices;

// 版本信息要填全 —— 空版本信息是杀软启发式扣分的典型项（这个坑之前踩过）
[assembly: AssemblyTitle("RSB 提取器")]
[assembly: AssemblyDescription("从 APK 中浏览并提取 .smf 文件（RSB 容器外壳自动解包）")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("sannian556")]
[assembly: AssemblyProduct("RSB 提取器")]
[assembly: AssemblyCopyright("© 2026 sannian556")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("8f3a1c62-5b47-4d19-9e28-7c6d0b2a4f51")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
// 作者水印：InformationalVersion 在「文件属性 → 详细信息」里能看到；
// AuthorStamp 是自定义属性，只有 ildasm / strings 才看得到。
[assembly: AssemblyInformationalVersion("1.0 + sannian556 | https://github.com/sannian556 | https://b23.tv/pm5cHXP")]
[assembly: AuthorStamp("sannian556 | https://github.com/sannian556 | https://b23.tv/pm5cHXP")]

/// <summary>作者水印（自定义程序集属性；ildasm / strings 可见，界面不显示）</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class AuthorStampAttribute : System.Attribute
{
    public readonly string Value;
    public AuthorStampAttribute(string value) { Value = value; }
}
