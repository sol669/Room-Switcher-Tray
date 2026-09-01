using RoomSwitcherTray.Core;
using RoomSwitcherTray.Core.Services;

internal static class AudioNamingTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL audio naming: " + name);
            checks++;
        }
        Guid common = new("00000000-0000-0000-ffff-ffffffffffff");
        var speakers = new AudioDevice("speakers", "Динамики", true, AudioDeviceState.Active, common, AudioDeviceKind.Other);
        var headphones = new AudioDevice("headphones", "Наушники", false, AudioDeviceState.Unplugged, common, AudioDeviceKind.Other);
        var panel = new DisplayDevice("panel", "ANX7530 U", true, true, common);
        var scenario = new ScenarioDefinition { Name = "Deck", AudioDeviceId = speakers.Id, AudioDeviceContainerId = common.ToString() };
        string before = System.Text.Json.JsonSerializer.Serialize(scenario);
        var visible = AudioService.GetVisibleRenderDevices([speakers, headphones], [panel], scenario);
        Check(visible.Count == 2, "two different endpoint IDs remain separate");
        Check(visible[0].Id == "speakers" && visible[0].Name == "Динамики" && visible[0].DisplayName is null,
            "default speakers retain native audio name");
        Check(visible[1].Name == "Наушники" && visible[1].DisplayName is null && visible[1].State == AudioDeviceState.Unplugged,
            "unplugged headphones retain native name and state");
        Check(System.Text.Json.JsonSerializer.Serialize(scenario) == before, "saved audio binding is unchanged");
        Guid physical = Guid.NewGuid();
        foreach (Guid container in new[] { common, Guid.Empty, physical })
        {
            var result = AudioService.GetVisibleRenderDevices([speakers with { ContainerId = container }],
                [panel with { ContainerId = container }]);
            Check(result.Single().DisplayName is null, "non-display audio cannot borrow a display name even with a physical container");
        }
        var hdmi = speakers with { Id = "hdmi", Name = "Digital output", Kind = AudioDeviceKind.Display, ContainerId = physical };
        var screen = panel with { ContainerId = physical, Name = "External monitor" };
        visible = AudioService.GetVisibleRenderDevices([hdmi], [screen]);
        Check(visible.Single().DisplayName == "External monitor" && visible.Single().Name == "Digital output",
            "unambiguous HDMI receives secondary monitor label without replacing native Name");
        visible = AudioService.GetVisibleRenderDevices([hdmi], [screen, screen with { Id = "second", Name = "Other monitor" }]);
        Check(visible.Single().DisplayName is null, "two screens in one container cannot be chosen arbitrarily");
        visible = AudioService.GetVisibleRenderDevices([hdmi, speakers with { ContainerId = physical }], [screen]);
        Check(visible.All(item => item.DisplayName is null), "mixed audio kinds in one container are not an unambiguous HDMI mapping");
        visible = AudioService.GetVisibleRenderDevices([hdmi with { ContainerId = common }], [panel]);
        Check(visible.Single().DisplayName is null, "shared system container cannot label even display-kind audio");
        visible = AudioService.GetVisibleRenderDevices([speakers with { DisplayName = "Wrong cached monitor" }], [panel]);
        Check(visible.Single().DisplayName is null, "stale derived monitor label is cleared");
        var old = hdmi with { Id = "old", IsDefault = false, State = AudioDeviceState.NotPresent };
        var savedOld = new ScenarioDefinition { AudioDeviceId = old.Id, AudioDeviceContainerId = physical.ToString() };
        visible = AudioService.GetVisibleRenderDevices([old, hdmi], [screen], savedOld);
        Check(visible.Any(item => item.Id == old.Id) && visible.Any(item => item.Id == hdmi.Id),
            "saved inactive endpoint is not hidden by an active sibling");
        visible = AudioService.GetVisibleRenderDevices([hdmi, hdmi with { Id = "second-active", IsDefault = false }], [screen]);
        Check(visible.Count == 2, "two active display outputs sharing a container are not merged");
        var savedCurrent = new ScenarioDefinition { AudioDeviceId = hdmi.Id, AudioDeviceContainerId = physical.ToString() };
        visible = AudioService.GetVisibleRenderDevices([old, hdmi], [screen], savedCurrent);
        Check(visible.Count == 1 && visible[0].Id == hdmi.Id, "retired HDMI does not reappear after saved binding migration");
        visible = AudioService.GetVisibleRenderDevices([old, hdmi, hdmi with { Id = "second-active" }], [screen], savedCurrent);
        Check(visible.Count == 3, "ambiguous active successors do not hide historical endpoint");
        visible = AudioService.GetVisibleRenderDevices([old, hdmi], [screen], [old.Id], savedOld);
        Check(visible.Count == 1 && visible[0].Id == hdmi.Id,
            "a verified retired endpoint stays hidden even when an older scenario still references it");
        Console.WriteLine($"PASS: {checks} audio naming/visibility regressions (synthetic Deck and shared-container fixtures).");
    }
}
