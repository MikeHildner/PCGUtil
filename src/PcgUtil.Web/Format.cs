namespace PcgUtil.Web;

/// <summary>Small display helpers shared by the Razor components.</summary>
public static class Format
{
    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.##} KB",
        _ => $"{bytes} B",
    };

    public static string Hex(long offset) => "0x" + offset.ToString("X");
}
