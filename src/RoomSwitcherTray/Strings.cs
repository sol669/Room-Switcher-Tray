using RoomSwitcherTray.Models;

namespace RoomSwitcherTray;

public static class Strings
{
    public static bool Ru => App.Settings.Current.Language == AppLanguage.Russian;
    public static string Scenarios => Ru ? "Сценарии" : "Scenarios";
    public static string NoScenarios => Ru ? "(нет сценариев)" : "(no scenarios)";
    public static string SwitchTo(string name) => Ru
        ? $"Переключить на «{name}»"
        : $"Switch to “{name}”";
    public static string DoubleClick => Ru ? "Двойной клик" : "Double-click";
    public static string DisplaySettings => Ru ? "Параметры экрана Windows" : "Windows display settings";
    public static string Settings => Ru ? "Настройки..." : "Settings...";
    public static string Exit => Ru ? "Выход" : "Exit";
    public static string MonitorOff => Ru ? "Выключить монитор" : "Turn off monitor";
    public static string ToggleHdr => Ru ? "HDR вкл./выкл." : "Toggle HDR";
    public static string EnableHdr => Ru ? "Включить HDR" : "Enable HDR";
    public static string DisableHdr => Ru ? "Выключить HDR" : "Disable HDR";
    public static string SettingsOpenFailed => Ru
        ? "Не удалось открыть настройки. Подробности записаны в error.log."
        : "Could not open Settings. Details were written to error.log.";
    public static string DisplayUnavailable(int count) => Ru
        ? $"Недоступны сохранённые дисплеи: {count}."
        : $"Saved displays unavailable: {count}.";
    public static string AudioUnavailable => Ru ? "Сохранённое аудиоустройство недоступно." : "The saved audio device is unavailable.";
    public static string AudioSwitchFailed => Ru ? "Не удалось переключить звук." : "Could not switch audio.";
    public static string PrimaryMustBeSelected => Ru ? "Основной экран должен входить в сценарий." : "The primary display must be selected.";
    public static string ScenarioApplied(string name) => Ru ? $"Сценарий «{name}» применён." : $"Scenario “{name}” applied.";
}

