using System.Reflection;

namespace CheapShotcutRandomizer;

public static class AppVersion
{
    private const string FallbackVersion = "0.0.0";

    private static readonly Lazy<string> _version = new(() =>
    {
        var assembly = Assembly.GetEntryAssembly();
        var assemblyVersion = assembly?.GetName().Version;
        return assemblyVersion is not null ? assemblyVersion.ToString(3) : FallbackVersion;
    });

    public static string Current => _version.Value;

    public static string Display => $"v{Current}";
}
