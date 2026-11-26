# DawnPro-GUI
DawnPro-GUI is a tool used to control the Moondrop Dawn Pro AMP/DAC.

![screenshot](preview.png)

## Features

- Change the LED status (on, temp-off, off)
- Set the gain (low, high)
- Configure the filters:
    - Fast-roll-off-low-latency
    - Fast-roll-off-phase-compensated
    - Slow-roll-off-low-latency
    - Slow-roll-off-phase-compensated
    - Non-oversampling
- Adjust the volume
- Fully configurable through JSON configuration file

## Windows

This fork includes a native C# and WPF controller for the original Moondrop Dawn Pro (USB `2FC6:F06A`). It uses the HID driver built into Windows alongside the audio driver. The initial package targets Windows x64. Dawn Pro 2 uses a different protocol.

Run these commands from the repository root with the .NET 10 SDK installed.

```powershell
dotnet build windows/DawnPro.slnx -c Release
dotnet test windows/DawnPro.Tests -c Release --no-build
dotnet run --project windows/DawnPro.Windows -c Release --no-build
```

- **Refresh** reads the connected DAC. Use it after plugging in or reconnecting the device. Connect one Dawn Pro at a time.
- **Apply to DAC** sends changed settings and checks the device's reply. If an operation fails, refresh to see which settings the device accepted.
- **Save locally** writes the selected values to `%LOCALAPPDATA%\DawnPro-GUI\config.json`. **Load saved** and **Import JSON…** put values into the controls. They take effect on the DAC when you select **Apply to DAC**. Import accepts the `default_settings` section of the existing Linux config.
- **DAC volume** is the device's 0–60-step hardware attenuator. Windows also has its own audio volume.

Use `dotnet publish` to create a self-contained Windows x64 build.

```powershell
dotnet publish windows/DawnPro.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded
```

Run `DawnPro.Windows.exe` from `windows/DawnPro.Windows/bin/Release/net10.0-windows/win-x64/publish/`. The automated tests use fake HID reports. Windows launch and behavior on a physical DAC require a manual check.

## Linux requirements

- Python 3.7 or higher
- `usb` module
- `PyGObject`

## Linux installation

### From AUR (Arch Linux)

The package is available on the Arch User Repository (AUR). You can install it using your preferred AUR helper:

```sh
# Using yay
yay -S dawnpro-gui

# Using paru
paru -S dawnpro-gui
```

Or build it manually:
```sh
git clone https://aur.archlinux.org/dawnpro-gui.git
cd dawnpro-gui
makepkg -si
```

### Manual Installation

To install pyusb, run:

```sh
pip install pyusb
```

To install PyGObject, it may depend on your distro or operating system:

https://pygobject.gnome.org/

## Setup

Add the following rule to your udev rules (you may need to adjust the rule name based on existing rules in `/etc/udev/rules.d/`):

```sh
echo 'SUBSYSTEM=="usb", ATTRS{idVendor}=="2fc6", MODE="0666"' | sudo tee /etc/udev/rules.d/99-dawn-pro.rules
```

Then run:

```sh
sudo udevadm control --reload-rules
sudo udevadm trigger
```

## Configuration

The application uses a configuration file located at `~/.config/dawnpro/config.json`. If this file doesn't exist, the application will use default settings.

### Setting Up Configuration

1. Create the configuration directory:
```sh
mkdir -p ~/.config/dawnpro
```

2. Copy the default configuration:
```sh
cp config.json ~/.config/dawnpro/config.json
```

### Configuration Sections

The configuration file is divided into several sections:

1. `device_constants`: USB communication constants
   ```json
   "device_constants": {
       "BM_REQUEST_TYPE_OUT": 67,
       "BM_REQUEST_TYPE_IN": 195,
       "B_REQUEST": 160,
       "B_REQUEST_GET": 161,
       "W_VALUE": 0,
       "W_INDEX": 2464,
       "VOLUME_REFRESH_DATA": [192, 165, 162],
       "DATA_LENGTH": 7,
       "LED_STATUS_ENABLED": 0,
       "LED_STATUS_TEMP_OFF": 1,
       "LED_STATUS_OFF": 2
   }
   ```

2. `device_identifiers`: Device vendor and product IDs
   ```json
   "device_identifiers": {
       "MOONDROP_VID": 12230,
       "DAWN_PRO_PID": 61546,
       "VOLUME_MAX": 0,
       "VOLUME_MIN": 112
   }
   ```

3. `default_settings`: Default values for device settings
   ```json
   "default_settings": {
       "DEFAULT_VOLUME": 50,
       "DEFAULT_LED_STATUS": "On",
       "DEFAULT_GAIN": "Low",
       "DEFAULT_FILTER": "Fast Roll-Off Low Latency"
   }
   ```

4. `ui_metrics`: Window size and UI element spacing
   ```json
   "ui_metrics": {
       "WINDOW_WIDTH": 400,
       "WINDOW_HEIGHT": 300,
       "MARGIN_TOP": 10,
       "MARGIN_BOTTOM": 20,
       "MARGIN_START": 10,
       "MARGIN_END": 10,
       "SPACING": 10
   }
   ```

5. `logging`: Logging configuration
   ```json
   "logging": {
       "LOG_LEVEL": "INFO",
       "LOG_FORMAT": "%(asctime)s - %(levelname)s - %(message)s",
       "LOG_FILE": "~/.config/dawnpro/dawnpro.log"
   }
   ```

### Example Custom Configuration

Here's an example of a custom configuration that changes some default values:

```json
{
    "default_settings": {
        "DEFAULT_VOLUME": 75,
        "DEFAULT_LED_STATUS": "Off",
        "DEFAULT_GAIN": "High",
        "DEFAULT_FILTER": "Fast Roll-Off Phase Compensated"
    },
    "ui_metrics": {
        "WINDOW_WIDTH": 500,
        "WINDOW_HEIGHT": 400,
        "SPACING": 15
    },
    "logging": {
        "LOG_LEVEL": "DEBUG",
        "LOG_FILE": "~/.config/dawnpro/debug.log"
    }
}
```

## Usage

Ensure the DAC/AMP is plugged in before running the script.

To run the tool, execute the following command:

```sh
python main.py
```

## Linux testing

The project includes a comprehensive test suite with hardware emulation, allowing tests to run without physical hardware.

### Running Tests

Install test dependencies:

```sh
pip install -r requirements-test.txt
```

Run all tests:

```sh
pytest
```

Run tests with verbose output:

```sh
pytest tests/ -v
```

Run tests with coverage report:

```sh
pytest --cov=device --cov=tests --cov-report=html
```

Open the coverage report:

```sh
xdg-open htmlcov/index.html  # Linux
open htmlcov/index.html      # macOS
```

### Test Structure

```
tests/
├── conftest.py           # Pytest fixtures and configuration
├── mock_hardware.py      # Hardware emulation layer
├── test_device.py        # Device detection tests
├── test_volume.py        # Volume control tests
├── test_gain.py          # Gain switching tests
├── test_filters.py       # Filter selection tests
├── test_led.py           # LED status tests
├── test_integration.py   # End-to-end integration tests
└── test_error_handling.py # Error handling tests
```

### Build Verification

Run the build verification script to check:
- Python version compatibility
- Required dependencies
- Syntax validation
- Configuration file validity

```sh
python scripts/build_check.py
```

## Acknowledgments
Inspired by:

"mdrop" by frahz: https://github.com/frahz/mdrop/

The Windows HID report layout and reply handling were cross-checked against [Abdul Mateen's Moondrop Dawn Pro controller](https://github.com/RetR0-hex/moondrop-dawn-pro-gui) and the [Windows hardware results in Glacier EQ](https://github.com/Bukutsu/glacier-eq/issues/46#issuecomment-6171187452).
