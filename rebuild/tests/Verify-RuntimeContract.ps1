$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../src/RoomSwitcherTray.Core'
$tray = Get-Content -Raw -LiteralPath (Join-Path $source 'Services/TrayService.cs')
$coordinator = Get-Content -Raw -LiteralPath (Join-Path $source 'Services/ScenarioCoordinator.cs')
$audio = Get-Content -Raw -LiteralPath (Join-Path $source 'Services/AudioService.cs')
$interop = Get-Content -Raw -LiteralPath (Join-Path $source 'Services/CoreAudioInterop.cs')
$watcher = Get-Content -Raw -LiteralPath (Join-Path $source 'Services/AudioDeviceWatcher.cs')
$factory = Get-Content -Raw -LiteralPath (Join-Path $source 'Services/TrayIconFactory.cs')
$settings = Get-Content -Raw -LiteralPath (Join-Path $source 'WinUiSettingsWindow.cs')
foreach ($token in @('WM_DEVICECHANGE','WM_DISPLAYCHANGE','WM_WTSSESSION_CHANGE',
    'new AudioDeviceWatcher','_deviceTimer.IsRepeating = false','FromMilliseconds(750)',
    '_lastIconKey','MF_GRAYED','ScenarioPolicy.Next','UnregisterDeviceNotification')) {
    if (-not $tray.Contains($token)) { throw "Missing runtime contract: $token" }
}
if ($audio -match 'Task.Delay|SetDefaultWhenAvailableAsync') { throw 'Legacy periodic audio polling returned.' }
if (($audio + $watcher) -match 'FinalReleaseComObject|class (MMDeviceEnumeratorComObject|EnumeratorComObject)') {
    throw 'Independent Core Audio callers must not share forced-release coclass wrappers.'
}
if (-not $interop.Contains('Marshal.GetUniqueObjectForIUnknown') -or
    -not $audio.Contains('enumerator = CreateEnumerator()') -or -not $watcher.Contains('_enumerator = CreateEnumerator()')) {
    throw 'Reader and watcher must use the shared, independently owned enumerator factory.'
}
if (-not $tray.Contains('ScenarioPolicy.TrayDevices(scenario, snapshot)') -or
    $tray.Contains('_settings.Current.Scenarios.SelectMany(item => item.DisplayIds)') -or
    $tray.Contains('App.Audio.SetDefaultEndpointMuted')) {
    throw 'Tray scope/actions must be bound to the selected scenario, not all devices or default audio.'
}
if (-not $coordinator.Contains('nextSnapshot.WaitAsync(wait.Token)')) { throw 'HDMI must wait on events.' }
if (-not $factory.Contains('remote ? ScenarioArtwork.RenderRemote')) { throw 'RDP icon must have highest priority.' }
if (-not $settings.Contains('choices.Add(new Choice(own, SavedDeviceName') -or
    -not $settings.Contains('audioChoices.Add(new Choice(_draft.AudioDeviceId, SavedDeviceName')) {
    throw 'Disconnected selections must remain in the settings lists.'
}
'PASS: event subscriptions, one-shot debounce, idle icon cache, hotkey availability, cancellation, RDP priority and offline selections.'
