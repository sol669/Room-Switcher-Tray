using RoomSwitcherTray.Core;
using RoomSwitcherTray.Core.Services;
using System.Text.Json;

internal static class AudioMigrationTests
{
    public static async Task Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        Guid container = Guid.NewGuid();
        var display = new DisplayDevice("screen", "TV", true, true, container);
        var old = new AudioDevice("old", "Old TV", false, AudioDeviceState.NotPresent, container, AudioDeviceKind.Display);
        var fresh = old with { Id = "new", Name = "New TV", State = AudioDeviceState.Active, IsDefault = true };
        var snapshot = new DeviceSnapshot([display], [old, fresh], [], new("New TV", 15, false));
        ScenarioDefinition Scenario() => new() { Name = "Room", DisplayIds = ["screen"], AudioDeviceId = "old",
            AudioDeviceContainerId = container.ToString(), VolumePercent = 100 };
        AppSettings Settings() => new() { Scenarios = [Scenario(), Scenario()], DeviceAliases = new() { ["old"] = "My TV" },
            KnownDeviceNames = new() { ["old"] = "Old TV" } };
        foreach (bool includeOld in new[] { true, false })
        {
            var state = includeOld ? snapshot : snapshot with { Audio = [fresh] };
            var settings = Settings();
            var original = settings.Scenarios[0].Clone();
            Check(AudioEndpointMigration.Reconcile(settings, state), "Missing/NotPresent HDMI should migrate");
            Check(settings.Scenarios.All(s => s.AudioDeviceId == "new" && s.VolumePercent == 100), "All bindings, no preset changes");
            Check(settings.DeviceAliases.GetValueOrDefault("new") == "My TV" && !settings.DeviceAliases.ContainsKey("old"), "Alias transferred once");
            Check(!settings.KnownDeviceNames.ContainsKey("old") && settings.KnownDeviceNames["new"] == fresh.Name, "Retired name removed");
            Check(settings.RetiredAudioDeviceIds.SequenceEqual(["old"]), "Verified retired endpoint is remembered for the picker");
            Check(!AudioEndpointMigration.Reconcile(settings, state), "Migration is idempotent");
            Check(ScenarioPolicy.FindAudio(original, state)?.Id == "new", "In-flight clone resolves same successor");
        }
        var conflictingAlias = Settings(); conflictingAlias.DeviceAliases["new"] = "New custom name";
        AudioEndpointMigration.Reconcile(conflictingAlias, snapshot);
        Check(conflictingAlias.DeviceAliases["new"] == "New custom name", "Existing destination alias wins");
        var crossScenario = Settings();
        crossScenario.Scenarios[1].DisplayIds = ["other-screen"];
        AudioEndpointMigration.Reconcile(crossScenario, snapshot);
        Check(crossScenario.Scenarios.All(s => s.AudioDeviceId == "new"),
            "one verified endpoint successor repairs every matching scenario binding");
        var blocked = new List<DeviceSnapshot>
        {
            snapshot with { AudioReadFailed = true },
            snapshot with { Audio = [old, fresh with { State = AudioDeviceState.Unplugged }] },
            snapshot with { Audio = [old, fresh, fresh with { Id = "other-active" }] },
            snapshot with { Audio = [old, fresh, old with { Id = "analog", Kind = AudioDeviceKind.Other }] },
            snapshot with { Audio = [old with { Kind = AudioDeviceKind.Other }, fresh] },
            snapshot with { Displays = [] },
            snapshot with { Displays = [display with { IsAvailable = false }] },
            snapshot with { Displays = [display, display with { Id = "other-screen" }] },
            snapshot with { Displays = [display with { Id = "unrelated" }] },
            snapshot with { Audio = [old, fresh with { ContainerId = Guid.NewGuid() }] }
        };
        foreach (var state in new[] { AudioDeviceState.Active, AudioDeviceState.Disabled, AudioDeviceState.Unplugged })
            blocked.Add(snapshot with { Audio = [old with { State = state }, fresh] });
        foreach (var state in blocked)
        {
            var settings = Settings(); string before = JsonSerializer.Serialize(settings);
            Check(!AudioEndpointMigration.Reconcile(settings, state) && before == JsonSerializer.Serialize(settings),
                "Ambiguous/offline/disabled/unplugged/non-display identity must be untouched");
        }
        foreach (var id in new[] { Guid.Empty, new Guid("00000000-0000-0000-ffff-ffffffffffff") })
        {
            var settings = Settings(); settings.Scenarios.ForEach(s => s.AudioDeviceContainerId = id.ToString());
            Check(!AudioEndpointMigration.Reconcile(settings, snapshot with { Displays = [display with { ContainerId = id }],
                Audio = [old with { ContainerId = id }, fresh with { ContainerId = id }] }), "System container is not hardware identity");
        }
        var draftSettings = Settings();
        var draft = draftSettings.Scenarios[0].Clone(); draft.Name = "Unsaved name";
        var baseline = draftSettings.Scenarios[0].Clone();
        draftSettings.Scenarios.AddRange([draft, baseline]); draftSettings.DeviceAliases["old"] = "Unsaved alias";
        AudioEndpointMigration.Reconcile(draftSettings, snapshot);
        Check(draft.Name == "Unsaved name" && draft.AudioDeviceId == baseline.AudioDeviceId &&
            draftSettings.DeviceAliases["new"] == "Unsaved alias", "Open editor migration preserves unsaved edits and baseline");
        var persisted = Settings(); var devices = new FakeDevices(snapshot);
        using (var coordinator = new ScenarioCoordinator(() => persisted, () => devices.Saves++, devices.Errors.Add, devices))
        {
            await coordinator.RefreshAsync();
            int saves = devices.Saves;
            await coordinator.RefreshAsync();
            Check(devices.VideoCalls.Count == 0 && devices.AudioCalls.Count == 0, "Refresh migration never writes hardware");
            Check(devices.Saves == saves && !persisted.KnownDeviceNames.ContainsKey("old"), "Repeated capture cannot resurrect retired cache");
            Check(persisted.Scenarios.All(s => s.VolumePercent == 100), "Live volume 15 never overwrites preset 100");
        }
        persisted = Settings(); devices = new FakeDevices(snapshot with { Audio = [old] });
        ScenarioCoordinator? pendingCoordinator = null;
        pendingCoordinator = new ScenarioCoordinator(() => persisted, () => { devices.Saves++; pendingCoordinator!.SettingsChanged(); },
            devices.Errors.Add, devices, TimeSpan.FromSeconds(3));
        using (pendingCoordinator)
        {
            await pendingCoordinator.ApplyAsync(persisted.Scenarios[0].Id);
            Check(pendingCoordinator.IsWaitingForAudio, "HDMI may arrive after display activation");
            devices.Current = snapshot;
            await pendingCoordinator.RefreshAsync();
            for (int i=0; i<100 && pendingCoordinator.IsWaitingForAudio; i++) await Task.Delay(10);
            Check(devices.AudioCalls.Count == 1 && devices.AudioCalls[0] == ("new", 100), "Migration keeps original one-shot HDMI apply intent");
        }
        Console.WriteLine($"PASS: {checks} HDMI migration regressions. No real device writes.");
    }
}
