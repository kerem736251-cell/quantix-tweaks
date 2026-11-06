# Quantix Elite

Windows WPF tweak utility with Quantix branding, license activation, hardware information, tweak selection, and apply/restore flows.

## Build

- Windows 10/11 x64.
- Visual Studio Community with the **.NET desktop development** workload and the **.NET 8 SDK**.
- Open `Elite.sln`, select Release, and build.
- Command line: `dotnet build Elite/QuantixTweakingUtility.csproj -c Release`.

The project includes its required local libraries in `Elite/lib`. Shared code and embedded PowerShell resources are in `Shared`. Keep that folder alongside `Elite`.

## Publish

```powershell
dotnet publish Elite/QuantixTweakingUtility.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
```

The executable requests administrator rights. Publishing does not apply any Windows tweaks.

## Current development state

This is the current development source, not a completed release or a verified full match to the reference recording. Some controls are hardware/driver dependent. `FixedPagefileTweak.cs` is work in progress and is not registered as a selectable control yet. Some original views use compiled `.baml` resources required by the project.

## Licensing and credentials

The included activation verification key is public. Private signing keys, bot files, tokens, saved licenses, and personal restore data are not included. Activation remains required; no license bypass is provided.

## Upload to GitHub

Extract this ZIP and upload its contents (including `.gitignore`) to your repository root. Do not upload `bin`, `obj`, `.vs`, credentials, or personal license/recovery files. The included `.gitignore` excludes those paths for future commits.
