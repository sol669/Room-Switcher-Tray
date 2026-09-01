namespace RoomSwitcherTray.Core.Services;

/// <summary>Repairs saved HDMI identity only. Never changes Windows audio or volume.</summary>
public static class AudioEndpointMigration
{
    public static AudioDevice? FindReplacement(ScenarioDefinition scenario, DeviceSnapshot snapshot)
    {
        if (snapshot.AudioReadFailed || string.IsNullOrWhiteSpace(scenario.AudioDeviceId) ||
            !Guid.TryParse(scenario.AudioDeviceContainerId, out Guid container) ||
            !DeviceIdentity.IsDeviceContainer(container)) return null;
        AudioDevice? old = snapshot.Audio.FirstOrDefault(item => ScenarioPolicy.Same(item.Id, scenario.AudioDeviceId));
        // Disabled and unplugged are real, deliberately selectable endpoints, not retired IDs.
        if (old is not null && (old.State != AudioDeviceState.NotPresent ||
            old.Kind != AudioDeviceKind.Display || old.ContainerId != container)) return null;
        AudioDevice? active = FindActiveDisplayEndpoint(scenario, snapshot, container);
        return active is not null && !ScenarioPolicy.Same(active.Id, scenario.AudioDeviceId) ? active : null;
    }

    private static AudioDevice? FindActiveDisplayEndpoint(ScenarioDefinition scenario, DeviceSnapshot snapshot, Guid container)
    {
        var screens = snapshot.Displays.Where(item => item.ContainerId == container).ToList();
        if (screens.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
            !screens.Any(item => item.IsAvailable && scenario.DisplayIds.Any(id => ScenarioPolicy.Same(id, item.Id)))) return null;
        var endpoints = snapshot.Audio.Where(item => item.ContainerId == container).ToList();
        if (endpoints.Count == 0 || endpoints.Any(item => item.Kind != AudioDeviceKind.Display)) return null;
        var active = endpoints.Where(item => item.IsActive).ToList();
        return active.Count == 1 ? active[0] : null;
    }

    public static bool Reconcile(AppSettings settings, DeviceSnapshot snapshot)
    {
        bool changed = false;
        var retired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, AudioDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (ScenarioDefinition scenario in settings.Scenarios)
        {
            AudioDevice? replacement = FindReplacement(scenario, snapshot);
            if (replacement is not null)
                replacements.TryAdd(scenario.AudioDeviceId, replacement);

            // A clean installation may already be bound to the new endpoint, leaving no old
            // scenario binding to repair. Once this scenario proves one active HDMI endpoint
            // for its physical display, its unplugged/not-present siblings are old identities.
            if (snapshot.AudioReadFailed || !Guid.TryParse(scenario.AudioDeviceContainerId, out Guid container) ||
                !DeviceIdentity.IsDeviceContainer(container)) continue;
            AudioDevice? active = FindActiveDisplayEndpoint(scenario, snapshot, container);
            if (active is null || !ScenarioPolicy.Same(active.Id, scenario.AudioDeviceId)) continue;
            foreach (AudioDevice stale in snapshot.Audio.Where(item => item.ContainerId == container &&
                !ScenarioPolicy.Same(item.Id, active.Id) &&
                !settings.RetiredAudioDeviceIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase) &&
                (item.State == AudioDeviceState.NotPresent || item.State == AudioDeviceState.Unplugged)))
                replacements.TryAdd(stale.Id, active);
        }
        // An endpoint ID identifies the physical Windows audio endpoint, not a scenario.
        // Once one scenario has safely proved its successor, repair every matching binding.
        foreach ((string oldId, AudioDevice replacement) in replacements)
        {
            if (settings.DeviceAliases.TryGetValue(oldId, out string? alias) &&
                !settings.DeviceAliases.ContainsKey(replacement.Id)) settings.DeviceAliases[replacement.Id] = alias;
            settings.KnownDeviceNames[replacement.Id] = replacement.Name;
            foreach (ScenarioDefinition scenario in settings.Scenarios.Where(item => ScenarioPolicy.Same(item.AudioDeviceId, oldId)))
            {
                scenario.AudioDeviceId = replacement.Id;
                scenario.AudioDeviceContainerId = replacement.ContainerId!.Value.ToString("D");
            }
            if (!settings.RetiredAudioDeviceIds.Contains(oldId, StringComparer.OrdinalIgnoreCase))
                settings.RetiredAudioDeviceIds.Add(oldId);
            retired.Add(oldId);
            changed = true;
        }
        foreach (string id in retired)
        {
            if (settings.Scenarios.Any(item => ScenarioPolicy.Same(item.AudioDeviceId, id))) continue;
            settings.DeviceAliases.Remove(id);
            settings.KnownDeviceNames.Remove(id);
        }
        return changed;
    }
}
