# Contributing

Thanks for your interest in improving **Cloudict**! Contributions of
all kinds are welcome — bug reports, fixes, features, and especially **new UI translations**.

## Getting started

### Prerequisites
- Windows 10/11 (x64)
- [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)
- Google Chrome installed (the app drives Google Translate through it)

### Build & run
```bash
# from the repository root
dotnet build src/Cloudict/Cloudict.csproj -c Debug
dotnet run  --project src/Cloudict/Cloudict.csproj
```
The app requests administrator rights at startup (needed for global hotkeys and to send
keystrokes to other apps), so accept the UAC prompt.

To produce a single self-contained executable, see [scripts/publish.bat](scripts/publish.bat).

## Project layout
See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for a full map. In short:
```
src/Cloudict/
├─ Views/         WPF windows (XAML + code-behind)
├─ Services/      app logic (speech transfer, voice commands, settings, shortcuts)
├─ Models/        data models (AppSettings, VoiceCommand)
├─ Localization/  LocalizationManager + per-language string dictionaries
└─ Assets/        icon and fonts
```

## Adding a new UI language

The UI is fully data-driven from resource dictionaries — no code changes are needed to
add a language:

1. Copy `src/Cloudict/Localization/Strings/Strings.en.xaml` to
   `Strings.<code>.xaml` (e.g. `Strings.de.xaml` for German) and translate every
   `<s:String>` value. **Keep the `x:Key` attributes unchanged.**
2. Register the code in `src/Cloudict/Localization/LocalizationManager.cs`:
   add it to `SupportedLanguages` (and to `RightToLeftLanguages` if the script is RTL).
3. Add a `<ComboBoxItem Tag="<code>" .../>` entry to the language selector in
   `src/Cloudict/Views/SettingsWindow.xaml`.

English (`en`) is the default and fallback language: any key missing from a translation
automatically falls back to its English text.

## Coding guidelines
- Match the style of the surrounding code.
- Keep user-facing strings in the resource dictionaries (use `{DynamicResource Key}` in
  XAML and `Loc.Get("Key")` in code-behind) — don't hard-code UI text.
- Build cleanly (`dotnet build`) before opening a pull request.
