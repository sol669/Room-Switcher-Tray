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
        var screens = snapshot.Displays.Where(item => item.ContainerId == container).ToList();
        if (screens.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
            !screens.Any(item => item.IsAvailable && scenario.DisplayIds.Any(id => ScenarioPolicy.Same(id, item.Id)))) return null;
        var endpoints = snapshot.Audio.Where(item => item.ContainerId == container).ToList();
        if (endpoints.Any(item => item.Kind != AudioDeviceKind.Display)) return null;
        var active = endpoints.Where(item => item.IsActive).ToList();
        return active.Count == 1 && !ScenarioPolicy.Same(active[0].Id, scenario.AudioDeviceId) ? active[0] : null;
    }

    public static bool Reconcile(AppSettings settings, DeviceSnapshot snapshot)
    {
        bool changed = false;
        var retired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, AudioDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (ScenarioDefinition scenario in settings.Scenarios)
        {
            AudioDevice? replacement = FindReplacement(scenario, snapshot);
            if (replacement is null) continue;
            string oldId = scenario.AudioDeviceId;
            replacements.TryAdd(oldId, replacement);
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
