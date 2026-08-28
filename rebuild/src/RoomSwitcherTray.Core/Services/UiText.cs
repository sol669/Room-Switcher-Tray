namespace RoomSwitcherTray.Core.Services;

public static class UiText
{
    public static string Get(AppSettings settings, string key) => (settings.Language == AppLanguage.English, key) switch
    {
        (true, "Next") => "Next scenario",
        (true, "Settings") => "Settings…",
        (true, "Configure") => "Configure scenarios…",
        (true, "Exit") => "Exit",
        (true, "Remote") => "Remote session (RDP)",
        (true, "Mute") => "Mute",
        (true, "NoDevices") => "No active device information",
        (true, "UnknownResolution") => "resolution unknown",
        (true, "Muted") => "muted",
        (true, "DisableHdr") => "Turn off HDR",
        (true, "EnableHdr") => "Turn on HDR",
        (false, "Next") => "Следующий сценарий",
        (false, "Settings") => "Настройки…",
        (false, "Configure") => "Настроить сценарии…",
        (false, "Exit") => "Выход",
        (false, "Remote") => "Удалённая сессия (RDP)",
        (false, "Mute") => "Без звука",
        (false, "NoDevices") => "Нет данных об активных устройствах",
        (false, "UnknownResolution") => "разрешение неизвестно",
        (false, "Muted") => "без звука",
        (false, "DisableHdr") => "Выключить HDR",
        (false, "EnableHdr") => "Включить HDR",
        _ => key
    };
}
