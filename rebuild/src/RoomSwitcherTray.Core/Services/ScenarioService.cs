namespace RoomSwitcherTray.Core.Services;

public sealed class ScenarioService(
    SettingsStore settings,
    DisplayService displays,
    AudioService audio)
{
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    public async Task<ApplyResult> ApplyAsync(int slot)
    {
        ScenarioDefinition? scenario = slot == 1
            ? settings.Current.Scenario1
            : settings.Current.Scenario2;
        if (scenario?.IsComplete != true)
            return new ApplyResult(false, "Сценарий не настроен.");

        if (!await _switchLock.WaitAsync(0))
            return new ApplyResult(false, "Переключение уже выполняется.");

        try
        {
            await Task.Run(() => displays.ApplyDisplays(scenario.DisplayIds));
            // HDMI/DisplayPort audio endpoints often appear a few seconds after
            // Windows has activated the corresponding display path.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
            await audio.SetDefaultWhenAvailableAsync(scenario.AudioDeviceId, timeout.Token);
            settings.Current.ActiveScenario = slot;
            settings.Save();
            return new ApplyResult(true, $"Сценарий «{scenario.Name}» применён.");
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            return new ApplyResult(false, ex.Message);
        }
        finally
        {
            _switchLock.Release();
        }
    }
}
