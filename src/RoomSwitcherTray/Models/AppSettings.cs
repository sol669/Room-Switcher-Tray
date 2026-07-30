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
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppLanguage Language { get; set; } = DetectLanguage();
    public bool StartWithWindows { get; set; }

    private static AppLanguage DetectLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Russian
            : AppLanguage.English;
}
