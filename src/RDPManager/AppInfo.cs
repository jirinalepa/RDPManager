using System.Diagnostics;
using System.Reflection;

namespace RDPManager;

/// <summary>Version, author and repository taken from the assembly attributes (set in the .csproj).</summary>
public static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string Product => Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "RDP Manager";

    public static string Version =>
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetName().Version?.ToString(3) ?? "";

    public static string Author => Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";

    public static string Copyright => Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    public static string RepositoryUrl => Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value ?? "";

    public static string ProductWithVersion => $"{Product} {Version}";

    public static void OpenRepository()
    {
        if (RepositoryUrl.Length > 0)
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
    }
}
