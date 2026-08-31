# Core Audio integration regression

Run on Windows x64 with the Windows audio service available. No physical endpoint is required and no device IDs, names or driver brands are hard-coded. The tests link production AudioService, AudioDeviceWatcher and CoreAudioInterop, not mocks; SettingsStore.Log is replaced with an in-memory error collector. No application window, settings writes or device setters are used.

Run each order in a fresh process:

```powershell
dotnet run --project rebuild/tests/AudioInteropTests/AudioInteropTests.csproj -c Release
dotnet run --project rebuild/tests/AudioInteropTests/AudioInteropTests.csproj -c Release -- --reader-first
dotnet run --project rebuild/tests/AudioInteropTests/AudioInteropTests.csproj -c Release -- --foreign-first
```

The watcher-first case reproduced the 0.11.0 InvalidCastException before the production fix. Reader-first tests the inverse order; foreign-first retains another coclass wrapper to check coexistence with other components. Each run also exercises STA/MTA concurrent enumeration, nested observer lifetimes and independent wrapper ownership. Suppressed production errors fail the test.

CI compiles this project, and runs pure scenario tests separately; hardware-service integration runs are required on the test machine before release. Do not treat compilation as an integration-test pass. A Windows runner without the audio service cannot exercise the real endpoint APIs.

After publishing, also run `rebuild/tests/Probe-Devices.ps1 -AssemblyPath <full-publish>/RoomSwitcherTray.dll`. It creates the observer before the first enumeration, uses the actual published assembly, and keeps the observer alive during reads. Test on a stable device configuration; changes made by other applications or unplugging devices during enumeration may invalidate a read. Actual hot-plug callback delivery remains a manual test.
