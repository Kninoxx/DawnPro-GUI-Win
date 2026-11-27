<img width="506" height="548" alt="example" src="https://github.com/user-attachments/assets/87f7e2ff-0cb2-434e-aba6-f7086cbb3719" />
# DawnPro-GUI-Win

A Windows desktop controller for the original Moondrop Dawn Pro DAC/amp (USB `2FC6:F06A`). It is built with C# and WPF and uses the HID driver built into Windows alongside the audio driver. The packaged app targets Windows x64.

## Controls

- **DAC volume** uses the device's 0–60-step hardware volume table. Windows also has its own audio volume.
- **Gain** switches between low and high.
- **Indicator LED** supports on, temporarily off, and off.
- **Reconstruction filter** selects from four roll-off filters and non-oversampling.

Connect one original Dawn Pro at a time. **Refresh** reads the DAC after you plug it in or reconnect it. **Apply to DAC** sends the changed settings and checks their readback. If an operation fails, refresh to see the settings the device accepted.

**Save locally** writes the selected values to `%LOCALAPPDATA%\DawnPro-GUI\config.json`. **Load saved** and **Import JSON…** put values into the controls. Select **Apply to DAC** to use those values on the device. Import accepts the `default_settings` section from an existing DawnPro-GUI config.

## Build and test

Run these commands from the repository root with the .NET 10 SDK installed.

```powershell
dotnet build windows/DawnPro.slnx -c Release
dotnet test windows/DawnPro.Tests -c Release --no-build
dotnet run --project windows/DawnPro.Windows -c Release --no-build
```

The automated tests use fake HID reports.

## Publish a portable executable

```powershell
dotnet publish windows/DawnPro.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded
```

Run `DawnPro.Windows.exe` from `windows/DawnPro.Windows/bin/Release/net10.0-windows/win-x64/publish/`.

## License and credits

This project uses the [MIT License](LICENSE).

It began as a fork of Shay Power's DawnPro-GUI, which was inspired by mdrop. [DawnPro-GUI](https://github.com/shaypower/DawnPro-GUI), [mdrop](https://github.com/frahz/mdrop/)

The Windows HID report layout and reply handling were cross-checked against Abdul Mateen's controller and the Glacier EQ Windows hardware results. [Moondrop Dawn Pro controller](https://github.com/RetR0-hex/moondrop-dawn-pro-gui), [Glacier EQ test report](https://github.com/Bukutsu/glacier-eq/issues/46#issuecomment-6171187452)

The final revision with the Linux app is available under the [`last-linux` tag](https://github.com/Kninoxx/DawnPro-GUI-Win/tree/last-linux).
