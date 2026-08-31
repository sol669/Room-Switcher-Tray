param([Parameter(Mandatory)][string]$AssemblyPath)
# READ-ONLY: no App, SettingsStore, tray window or device setters are created/called.
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$displayType = $assembly.GetType('RoomSwitcherTray.Core.Services.DisplayService', $true)
$audioType = $assembly.GetType('RoomSwitcherTray.Core.Services.AudioService', $true)
$watcherType = $assembly.GetType('RoomSwitcherTray.Core.Services.AudioDeviceWatcher', $true)
$displays = [Activator]::CreateInstance($displayType)
$audio = [Activator]::CreateInstance($audioType)
$renderDevices = $audioType.GetMethod('GetRenderDevices')
$readVolume = $audioType.GetMethods() | Where-Object { $_.Name -eq 'GetDefaultEndpointStatus' -and $_.GetParameters().Count -eq 1 }
Add-Type @'
using System.Threading;
public static class DeviceProbeCallback {
    public static int Count;
    public static void Mark() { Interlocked.Increment(ref Count); }
}
'@
$callback = [Action][Delegate]::CreateDelegate([Action], [DeviceProbeCallback].GetMethod('Mark'))
# Match application startup: watcher FIRST, then reads while the watcher is alive.
$observer = [Activator]::CreateInstance($watcherType, [object[]]@($callback))
try {
    $timings = @()
    for ($i=0; $i -lt 12; $i++) {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $screens = $displayType.GetMethod('GetKnownDisplays').Invoke($displays,@())
        $active = $displayType.GetMethod('GetActiveDisplayStatuses', [Type[]]@()).Invoke($displays,@())
        $endpoints = $renderDevices.Invoke($null,@())
        $volume = $readVolume.Invoke($audio, [object[]]@(,$endpoints))
        $timings += $clock.Elapsed.TotalMilliseconds
    }
    for ($i=0; $i -lt 20; $i++) {
        $temporary = [Activator]::CreateInstance($watcherType, [object[]]@($callback))
        try { $endpoints = $renderDevices.Invoke($null,@()) }
        finally { $temporary.Dispose() }
        $endpoints = $renderDevices.Invoke($null,@())
    }
    $process = [Diagnostics.Process]::GetCurrentProcess()
    $process.Refresh()
    $beforeCpu = $process.TotalProcessorTime.TotalSeconds
    $beforeHandles = $process.HandleCount
    Start-Sleep -Seconds 10
    $process.Refresh()
    [PSCustomObject]@{
        DeviceReadsWhileWatching = 52
        ScreensObserved = $screens.Count
        ActiveScreens = $active.Count
        AudioEndpointsObserved = $endpoints.Count
        DefaultAudioName = $volume.Name
        MeanReadMilliseconds = [Math]::Round(($timings | Measure-Object -Average).Average,2)
        MaximumReadMilliseconds = [Math]::Round(($timings | Measure-Object -Maximum).Maximum,2)
        ObserverRegistrationsDisposed = 20
        ObserverIdleSeconds = 10
        ObserverCpuSeconds = [Math]::Round($process.TotalProcessorTime.TotalSeconds-$beforeCpu,4)
        ObserverEvents = [DeviceProbeCallback]::Count
        HandlesStart = $beforeHandles
        HandlesEnd = $process.HandleCount
        ApplicationStarted = $false
        DeviceConfigurationChanged = $false
    } | ConvertTo-Json
} finally { $observer.Dispose() }
