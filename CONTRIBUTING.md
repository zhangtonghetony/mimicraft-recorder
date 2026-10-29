# Contributing

Start with a small, reproducible issue. Describe the action, expected behavior and actual result. For a change, explain why it helps and how you checked it.

- Use original assets or the built-in practice pattern for demonstrations.
- Preserve source media. Editing should export new files.
- Run the checks relevant to the change; capture changes require an actual Windows session and recorded-content validation.
- Device tests are opt-in. Do not modify system sound or privacy permissions.
- Never commit settings, logs, recordings, stream keys, camera images or downloaded SDK/media binaries.
- Keep UI, capture/audio, media processing and Core models separated. Avoid introducing a generic platform for a single feature.
- Respect upstream licenses and include needed third-party notices when changing packaging.

See [development](docs/DEVELOPMENT.md) and [testing](docs/TESTING.md). Contributions to project code are made under the repository's MIT license; third-party material retains its original license.
