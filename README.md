# Galaxy Extractor

A client mod for Empyrion: Galactic Survival that extracts the complete galaxy star map from game memory and saves it as a CSV file.

## Download

**[Latest Release](../../releases/latest)** - Download the compiled mod here

See [Release Notes](RELEASE_NOTES.md) for version history and changes.

## What It Does

When you enter a game, this mod automatically:
- Scans game memory for the galaxy star data structure
- Extracts all star names and coordinates (x, y, z)
- Saves the data to a CSV file for use in external tools

## Installation

1. Download `GalaxyExtract.zip` from the [latest release](../../releases/latest)
2. Extract to your Empyrion mods folder:
   - **Client**: `SteamApps\common\Empyrion - Galactic Survival\Content\Mods`
   - Default location: `C:\Program Files (x86)\Steam\SteamApps\common\Empyrion - Galactic Survival\Content\Mods`
3. Launch Empyrion **without EAC (Easy Anti-Cheat)** enabled

## How It Works

### Automatic Configuration
The mod automatically configures itself at runtime:
- **Output Path**: Reads your current save game directory and creates the output file at:  
  `{SaveGame}\Content\Mods\GalaxyExtract\galaxy.csv`
- **Search Star**: Reads the first solar system name from your scenario's `Sectors.yaml` file to locate the galaxy data in memory

### Extraction Process
1. **On Game Entry**: Triggered automatically when you enter a game world
2. **Memory Scanning**: Searches for the star data structure by finding the first solar system name
3. **Data Extraction**: Reads all star entries (name + coordinates) from the discovered structure
4. **CSV Output**: Writes star count and data in CSV format

### Output Format
```csv
[star count]
x,y,z,name
134,25,126,Ellyon
70,5,97,Ashon
...
```

## Features

- **Zero Configuration**: Works automatically with any scenario
- **One-Time Extraction**: Skips extraction if output file already exists
- **Progress Logging**: Detailed logs show extraction progress and results
- **Efficient Scanning**: Pauses garbage collection during memory scan
- **Data Validation**: Verifies coordinate ranges and checks for duplicates

## Log Messages

The mod provides detailed logging to help verify everything is working:
- Output path and search star name on initialization
- Memory scanning progress (every 100 regions)
- Structure discovery location
- Star count and extraction time
- Coordinate ranges for validation
- Any warnings (e.g., duplicate star names)

Check your Empyrion log files to see the mod's progress.

## Requirements

- Empyrion: Galactic Survival (Client)
- Game must be launched without EAC
- YamlDotNet library (included in release)

## Technical Details

- **Language**: C#
- **Target Framework**: .NET 4.0
- **Memory Scan**: Uses Windows VirtualQuery API to scan committed, private, read-write memory regions
- **Star Structure**: 48-byte entries containing 3 floats (x,y,z coordinates) + string (star name)
- **Dependencies**: Eleon.Modding API, YamlDotNet

## Source Code

The mod is structured for readability and maintainability:
- Memory extraction with documented constants (no magic numbers)
- Comprehensive error handling and logging
- Data validation to verify extraction quality
- Clean separation of concerns (extraction, validation, file I/O)

## Known Limitations

- Client mod only (requires game memory access)
- Extracts data only once per save game (delete CSV to re-extract)
- Minimum 1,000 stars required to validate successful extraction

## License

This is free and unencumbered software released into the public domain. See [LICENSE](LICENSE) for details.

## Credits

Created for the Empyrion: Galactic Survival modding community.
Technique/code for scanning memory from https://github.com/shudson6/EGS-GalacticWaez.git (shudson6)
