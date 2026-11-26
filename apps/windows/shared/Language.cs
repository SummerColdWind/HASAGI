using System;
using System.Globalization;
using System.IO;
using System.Reflection;
[assembly: AssemblyVersion("0.1.0.2")]
[assembly: AssemblyFileVersion("0.1.0.2")]
[assembly: AssemblyInformationalVersion("0.1.0-beta.2")]

internal static class AppLanguage
{
    internal static readonly string[] Codes = { "zh-CN", "zh-TW", "en" };
    private static readonly string Preference = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HASAGI", "language.txt");
    internal static int Detect(CultureInfo culture)
    {
        for (; culture.Name.Length > 0; culture = culture.Parent) {
            switch (culture.Name.ToLowerInvariant()) {
                case "zh-hant": case "zh-cht": return 1;
                case "zh-hans": case "zh-chs": case "zh": return 0;
            }
        }
        return 2;
    }

    internal static int Load()
    {
        try {
            int index = Array.IndexOf(Codes, File.ReadAllText(Preference).Trim());
            if (index >= 0) return index;
        } catch (IOException) {} catch (UnauthorizedAccessException) {}
        return Detect(CultureInfo.CurrentUICulture);
    }

    internal static void Save(int index)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Preference));
        string temp = Preference + "." + Guid.NewGuid().ToString("N");
        try {
            File.WriteAllText(temp,Codes[index]);
            if (!File.Exists(Preference)) {
                try { File.Move(temp,Preference); return; }
                catch (IOException) { if (!File.Exists(Preference)) throw; }
            }
            File.Replace(temp,Preference,null);
        } finally { File.Delete(temp); }
    }
}
