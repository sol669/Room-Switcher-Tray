namespace RoomSwitcherTray.Core.Services;

public sealed class ScenarioService(
    SettingsStore settings,
    DisplayService displays,
    AudioService audio)
{
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    public async Task<ApplyResult> ApplyAsync(Guid scenarioId)
    {
        ScenarioDefinition? scenario = settings.Current.Scenarios
            .FirstOrDefault(item => item.Id == scenarioId);
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
            AudioDevice selectedAudio = await audio.SetDefaultWhenAvailableAsync(
                scenario.AudioDeviceId, scenario.AudioDeviceContainerId, timeout.Token);
            scenario.AudioDeviceId = selectedAudio.Id;
            if (selectedAudio.ContainerId.HasValue)
                scenario.AudioDeviceContainerId = selectedAudio.ContainerId.Value.ToString("D");
            settings.Current.ActiveScenarioId = scenario.Id;
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
