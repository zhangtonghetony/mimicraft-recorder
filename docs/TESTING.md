# Testing

Use a visible, unlocked Windows desktop for UI and device tests. Do not run two device suites at once. Keep private audio/video and local settings out of commits.

## Published validation snapshot — 0.3.2

| Check | Result | Scope |
| --- | --- | --- |
| Unit | 40 passed | Models, state and input boundaries |
| Related desktop regression | 10 passed | Camera/range guide, cancel waiting, annotation controls, actual encoded pixels, tray and split layout |
| Final portable package | 3 passed | Camera in full/cropped recordings and disabling it; old crop/reset/restart; countdown/schedule cancellation |

These are selected regression suites, not a claim that every hardware/parameter combination is tested. Earlier whole-project validation passed 84 checks before the later interaction fixes. The current public tree contains additional regressions introduced by those fixes.

The machine used Windows 11 x64 / Intel UHD. A previous 60-second 1080p/60fps camera and system-audio run decoded successfully. A controlled flash/tone recording and a playback loopback check were also performed. They do not replace hours-long recording, human speech intelligibility, subjective denoising quality or cross-device validation.

## Run checks

After preparing tools and restoring packages, from the repository root:

```powershell
./scripts/verify-all.ps1
```

Device-dependent tests explicitly skip when not enabled. For real camera/audio and the additional 60-second check:

```powershell
./scripts/verify-all.ps1 -IncludeDevices -IncludeStress
```

This second command uses the real camera, microphone and system-audio devices and briefly plays a low-gain tone. Use it only when the environment and people nearby allow it. The application does not change the system master volume or privacy settings.

`-SkipDesktop` skips the visible desktop suite. TRX results and media remain in ignored `artifacts/` folders. Never label skipped tests as passed.

## Reporting a problem

Include the application version, Windows version, display scaling, selected capture source and reproducible steps. For audio/camera issues, include the device type and whether the physical privacy switch is open. A redacted log is useful; personal recordings are not required.

Priorities for additional testing: mixed-DPI multi-monitor setups, AMD/NVIDIA, other cameras/sound devices, human speech/denoise listening, long sessions, storage failures and actual external streaming services.
