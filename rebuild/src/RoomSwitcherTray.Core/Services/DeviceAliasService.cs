namespace RoomSwitcherTray.Core.Services;

public static class DeviceAliasService
{
    public static string NameFor(AppSettings settings, string id, string systemName)
    {
        return settings.DeviceAliases.TryGetValue(id, out string? alias) && !string.IsNullOrWhiteSpace(alias)
            ? alias.Trim() : systemName;
    }

    public static void Set(AppSettings settings, string id, string? alias)
    {
        string value = alias?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) settings.DeviceAliases.Remove(id);
        else settings.DeviceAliases[id] = value;
    }
}
