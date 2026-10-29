# Working on Mimicraft Recorder

- Read README.md, docs/DEVELOPMENT.md and docs/TESTING.md before changing behavior.
- Windows desktop app: C# / .NET 10 / WPF / MVVM. Keep capture, audio, encoding and UI responsibilities separate.
- All current product features are free. Use original UI, icons and assets.
- Verify capture changes on a visible Windows desktop. File metadata alone cannot prove correct recorded content.
- Device tests are opt-in. Do not change system volume or privacy permissions. Do not interrupt an active recording to rebuild or restart.
- Never commit recordings, microphone/camera samples, credentials, personal settings, logs or tools. Use the built-in practice pattern for public screenshots.
- Preserve source media; edits export to new files. Failed captures must not appear as successfully completed recordings.
- Respect the licenses of bundled tools. The application's MIT license does not relicense FFmpeg, .NET or other dependencies.
