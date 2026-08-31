using System.Globalization;

namespace RoomSwitcherTray.Core;

// Editing state is separate from the persisted one-shot startup value and live Windows volume.
public sealed class StartupVolumeInput(int? savedValue)
{
    public bool Enabled { get; set; } = savedValue.HasValue;
    public string Text { get; set; } = savedValue?.ToString(CultureInfo.InvariantCulture) ?? "";
    public bool IsValid => !Enabled || TryParse(Text, out _);
    public int? Value => !Enabled ? null : TryParse(Text, out int value) ? value :
        throw new InvalidOperationException("Enter a whole percentage from 0 to 100.");

    public static bool TryParse(string? text, out int value)
    {
        value = 0;
        return !string.IsNullOrEmpty(text) && text.All(c => c is >= '0' and <= '9') &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= 100;
    }
}
