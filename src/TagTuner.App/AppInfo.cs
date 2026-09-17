using System.Reflection;

namespace TagTuner.App;

/// <summary>Was über diese Ausgabe bekannt ist.</summary>
public static class AppInfo
{
    public const string Name = "TagTuner";

    /// <summary>Die Version aus der Assembly, ohne Build-Zusatz.</summary>
    public static string Version { get; } = Read();

    private static string Read()
    {
        try
        {
            var raw = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "0.0.0";

            var plus = raw.IndexOf('+');
            return plus > 0 ? raw[..plus] : raw;
        }
        catch { return "0.0.0"; }
    }
}
