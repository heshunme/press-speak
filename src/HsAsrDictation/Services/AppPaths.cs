using System.IO;

namespace HsAsrDictation.Services;

public static class AppPaths
{
    private static readonly string BasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HsAsrDictation");

    public static string SettingsDirectory => BasePath;

    public static string SettingsFilePath => Path.Combine(BasePath, "settings.json");

    public static string PostProcessingUserRulesPath =>
        Path.Combine(SettingsDirectory, "postprocessing-rules.user.json");

    public static string LogsDirectory => Path.Combine(BasePath, "logs");

    public static string DefaultModelRootPath => Path.Combine(BasePath, "models");
}
