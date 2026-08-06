namespace RoomSwitcherTray.Models;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public enum AppLanguage
{
    Russian,
    English
}

public sealed class AppSettings
{
    public List<Scenario> Scenarios { get; set; } = [];
    public Guid? ActiveScenarioId { get; set; }
    public Dictionary<string, string> DisplayAliases { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> AudioAliases { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppLanguage Language { get; set; } = DetectLanguage();
    public bool StartWithWindows { get; set; }

    private static AppLanguage DetectLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Russian
            : AppLanguage.English;
}

