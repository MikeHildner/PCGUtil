using PcgUtil.Core;

namespace PcgUtil.Core.Tests;

/// <summary>Locates and loads the checked-in sample PCG for tests.</summary>
internal static class Sample
{
    public static string Path { get; } = Find();

    public static byte[] Bytes() => File.ReadAllBytes(Path);

    public static PcgFile Parse() => PcgReader.Parse(Bytes());

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "files", "20260602.PCG");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Sample PCG (files/20260602.PCG) not found walking up from {AppContext.BaseDirectory}");
    }
}
