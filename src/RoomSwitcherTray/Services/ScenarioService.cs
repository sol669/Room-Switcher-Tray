using RoomSwitcherTray.Models;

namespace RoomSwitcherTray.Services;

public sealed class ScenarioService(
    SettingsStore settings,
    DisplayService displays,
    AudioDeviceService audio)
{
    public async Task<ScenarioApplyResult> ApplyAsync(Scenario scenario)
    {
        return await Task.Run(() =>
        {
            try
            {
                var availableDisplays = displays.GetDisplays();
                string[] missingDisplays = scenario.DisplayIds
                    .Where(id => availableDisplays.All(d => !d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (missingDisplays.Length > 0)
                    return ScenarioApplyResult.Error(Strings.DisplayUnavailable(missingDisplays.Length));

                if (!scenario.DisplayIds.Contains(scenario.PrimaryDisplayId, StringComparer.OrdinalIgnoreCase))
                    return ScenarioApplyResult.Error(Strings.PrimaryMustBeSelected);

                AudioDevice? selectedAudio = null;
                if (!string.IsNullOrWhiteSpace(scenario.AudioDeviceId))
                {
                    selectedAudio = audio.GetRenderDevices().FirstOrDefault(d =>
                        d.Id.Equals(scenario.AudioDeviceId, StringComparison.OrdinalIgnoreCase));
                    if (selectedAudio is null)
                        return ScenarioApplyResult.Error(Strings.AudioUnavailable);
                }

                displays.Apply(scenario.DisplayIds, scenario.PrimaryDisplayId);

                if (selectedAudio is not null &&
                    !audio.SetDefault(selectedAudio.Id, out string? audioError))
                    return ScenarioApplyResult.Error($"{Strings.AudioSwitchFailed} {audioError}");

                settings.Current.ActiveScenarioId = scenario.Id;
                settings.Save();
                return ScenarioApplyResult.Ok(Strings.ScenarioApplied(scenario.Name));
            }
            catch (Exception ex)
            {
                SettingsStore.Log(ex);
                return ScenarioApplyResult.Error(ex.Message);
            }
        });
    }
}
