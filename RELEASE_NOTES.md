# Release Notes

## v1.0.1 - ActiveScenario fix

**Release Date**: 11/4/2025

### Change Log

- changed use of ActiveScenario path to SaveGame to reference the active Sectors.yaml
- reorganized init to simplify and avoid a potential race condition
- added additional logging and NULL substitution to confirm path info

## v1.0.0 - Initial Release

**Release Date**: 11/2/2025

### What's New

First public release of Galaxy Extractor - a client mod that automatically extracts the complete galaxy star map from Empyrion: Galactic Survival.

### Features

- **Automatic Configuration**: No manual setup required - reads paths and scenario data at runtime
- **Memory Extraction**: Scans game memory to locate and extract galaxy star data
- **CSV Export**: Saves star coordinates and names to an easy-to-use CSV format
- **Progress Logging**: Detailed logging for troubleshooting and verification
- **Data Validation**: Checks for duplicates and logs coordinate ranges
- **Smart Caching**: Only extracts once per save game (delete CSV to re-extract)

### Installation

1. Download `GalaxyExtract.zip` from this release
2. Extract to `[Empyrion Install]\Content\Mods\`
3. Launch Empyrion without EAC enabled
4. Enter your game - extraction happens automatically

### Output

Galaxy data is saved to:
```
[SaveGame]\Content\Mods\GalaxyExtract\galaxy.csv
```

Format:
```csv
[star count]
x,y,z,name
134,25,126,Ellyon
70,5,97,Ashon
...
```

### Requirements

- Empyrion: Galactic Survival (Client)
- Game must be launched without EAC (Easy Anti-Cheat)
- .NET 4.0 or higher

### Tested With

- Empyrion version: 1.13.3
- Scenario: Default Random / Reforged Eden

### Known Issues

- None at this time

### Technical Notes

- Extracts minimum 1,000 stars to validate successful extraction
- Uses YamlDotNet for reading scenario configuration
- Pauses garbage collection during memory scan for stability

### Support

If you encounter issues:
1. Check your Empyrion log files for "GalaxyExtractor:" messages
2. Verify EAC is disabled when launching the game
3. On the first start of a new game a race condition with galaxy populate may result in extract failure, exit and retry.
4. Report issues on the [GitHub Issues](../../issues) page with:
   - Your Empyrion version
   - Scenario name
   - Relevant log excerpts

### Credits

Created for the Empyrion: Galactic Survival modding community.

---

**Full Changelog**: Initial release
