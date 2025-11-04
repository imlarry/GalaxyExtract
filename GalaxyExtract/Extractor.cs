using Eleon.Modding;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using YamlDotNet.Serialization;

namespace GalaxyExtract
{
    // client mod
    public class Extractor : IMod
    {
        #region Constants

        // Star structure layout (48 bytes total)
        private const int STAR_ENTRY_SIZE = 48;
        private const int STAR_X_OFFSET = 0;
        private const int STAR_Y_OFFSET = 4;
        private const int STAR_Z_OFFSET = 8;
        private const int STAR_NAME_LENGTH_OFFSET = 12;
        private const int STAR_NAME_OFFSET = 14;
        private const int STAR_FLOAT_SECTION_SIZE = 12;

        private const int MAX_STAR_NAME_LENGTH = 40;
        private const int MIN_STAR_NAME_LENGTH = 2;

        // Minimum expected stars in a valid galaxy to filter out false positives
        private const int MIN_STAR_COUNT = 1000;

        // 2MB no-GC region to prevent GC during memory scanning
        private const long NO_GC_SIZE = 2 * 1024 * 1024;

        // Stop parsing after this many consecutive invalid entries
        private const int MAX_CONSECUTIVE_PARSE_FAILURES = 100;

        // Log progress every N regions during memory scan
        private const int REGION_PROGRESS_INTERVAL = 100;

        #endregion

        #region Fields

        // Runtime-initialized fields
        private string outputPath;
        private string sectorPath;
        private byte[] searchPattern;

        IModApi modApi;

        #endregion

        #region Initialization and Lifecycle

        public void Init(IModApi modApi)
        {
            this.modApi = modApi;

            modApi.Application.GameEntered += OnGameEntered;
            modApi.Log("GalaxyExtractor mod initialized");
        }

        private void InitializePaths()
        {
            // Get save game path - both output and sector files are rooted from here
            var saveGamePath = modApi.Application.GetPathFor(AppFolder.SaveGame);
            modApi.Log(string.Format("GalaxyExtractor: SaveGame path: {0}", saveGamePath ?? "NULL"));

            var fullPath = Path.GetFullPath(saveGamePath);
            modApi.Log(string.Format("GalaxyExtractor: Full path: {0}", fullPath ?? "NULL"));

            // Set output file path
            outputPath = Path.Combine(fullPath, "Content", "Mods", "GalaxyExtract", "galaxy.csv");
            modApi.Log(string.Format("GalaxyExtractor: Output path set to {0}", outputPath ?? "NULL"));

            // Set sector file path (copy exists in save game directory)
            sectorPath = Path.Combine(fullPath, "Sectors", "Sectors.yaml");
            modApi.Log(string.Format("GalaxyExtractor: Sector path set to {0}", sectorPath ?? "NULL"));
        }

        private string GetFirstSolarSystemName()
        {
            try
            {
                modApi.Log(string.Format("GalaxyExtractor: Reading sectors file from {0}", sectorPath));

                if (!File.Exists(sectorPath))
                {
                    modApi.Log("GalaxyExtractor: Sectors.yaml file not found");
                    return null;
                }

                // Open file with read-only access and allow sharing in case game has it open
                string fileContent;
                using (var fileStream = new FileStream(sectorPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var streamReader = new StreamReader(fileStream))
                {
                    fileContent = streamReader.ReadToEnd();
                }

                var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
                using (var reader = new StringReader(fileContent))
                {
                    var sectors = deserializer.Deserialize<SectorsFile>(reader);

                    if (sectors?.SolarSystems != null && sectors.SolarSystems.Count > 0)
                    {
                        return sectors.SolarSystems[0].Name;
                    }
                }

                modApi.Log("GalaxyExtractor: No solar systems found in sectors.yaml");
                return null;
            }
            catch (Exception ex)
            {
                modApi.Log(string.Format("GalaxyExtractor: Error reading sectors.yaml: {0}", ex.Message));
                return null;
            }
        }

        void OnGameEntered(bool entered)
        {
            if (!entered)
                return;

            // Initialize paths now that the game and scenario are loaded
            InitializePaths();

            // Check if CSV already exists - if so, extraction was done previously
            if (File.Exists(outputPath))
            {
                modApi.Log(string.Format("GalaxyExtractor: Output file already exists at {0}, skipping extraction", outputPath));
                modApi.GUI.ShowGameMessage("Galaxy data already extracted - file exists", prio: 1);
                return;
            }

            // Get the first star name from sectors.yaml to use as search pattern
            string searchStarName = GetFirstSolarSystemName();
            if (string.IsNullOrEmpty(searchStarName))
            {
                modApi.Log("GalaxyExtractor: Could not read star name from sectors.yaml, skipping extraction");
                modApi.GUI.ShowGameMessage("Galaxy extraction skipped - sectors.yaml missing or invalid", prio: 1);
                return;
            }

            // Do the deed
            modApi.Log(string.Format("GalaxyExtractor: Star name set to '{0}' from sectors.yaml", searchStarName));
            searchPattern = Encoding.ASCII.GetBytes(searchStarName);
            PerformExtraction();
        }

        private void PerformExtraction()
        {
            modApi.Log("GalaxyExtractor: Starting extraction...");
            modApi.GUI.ShowGameMessage("Galaxy extraction started...", prio: 1);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var stars = GetGalaxyData();
                stopwatch.Stop();

                HandleExtractionResult(stars, stopwatch.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                HandleExtractionError(ex);
            }
        }

        private void HandleExtractionResult(StarWithName[] stars, double elapsedSeconds)
        {
            if (stars != null && stars.Length > 0)
            {
                ValidateExtractedData(stars);
                WriteCSV(stars);

                modApi.Log(string.Format("GalaxyExtractor: Successfully extracted {0} stars to {1} in {2:F2} seconds",
                    stars.Length, outputPath, elapsedSeconds));
                modApi.GUI.ShowGameMessage(string.Format("Galaxy extraction complete! {0} stars saved", stars.Length), prio: 1);
            }
            else
            {
                modApi.Log(string.Format("GalaxyExtractor: Failed to extract galaxy data from memory (elapsed: {0:F2}s), output path was {1}",
                    elapsedSeconds, outputPath));
                modApi.GUI.ShowGameMessage("Galaxy extraction failed - could not find star data in memory", prio: 1);
            }
        }

        private void HandleExtractionError(Exception ex)
        {
            modApi.Log(string.Format("GalaxyExtractor: Error during extraction (output path: {0}): {1}", outputPath, ex.Message));
            modApi.GUI.ShowGameMessage(string.Format("Galaxy extraction error: {0}", ex.Message), prio: 1);
        }

        public void Shutdown()
        {
            modApi.Application.GameEntered -= OnGameEntered;
            modApi.Log("GalaxyExtractor mod shutdown");
        }

        #endregion

        #region Memory Extraction

        private unsafe StarWithName[] GetGalaxyData()
        {
            PauseGC();

            try
            {
                Kernel32.GetSystemInfo(out var sysInfo);
                Kernel32.MEMORY_BASIC_INFORMATION memInfo = default;
                int memInfoSize = Marshal.SizeOf(memInfo);

                var regions = new List<Kernel32.MEMORY_BASIC_INFORMATION>();

                while (GetNextMemoryRegion(ref memInfo, sysInfo, memInfoSize))
                {
                    regions.Add(memInfo);
                }

                // Scan from highest to lowest address - newer allocations (like galaxy data loaded on game entry)
                // typically reside at higher addresses, so we find active game data faster
                regions.Reverse();

                modApi.Log(string.Format("GalaxyExtractor: Scanning {0} memory regions...", regions.Count));

                int regionsScanned = 0;
                foreach (var mem in regions)
                {
                    regionsScanned++;

                    if (regionsScanned % REGION_PROGRESS_INTERVAL == 0)
                    {
                        modApi.Log(string.Format("GalaxyExtractor: Progress - scanned {0}/{1} regions...",
                            regionsScanned, regions.Count));
                    }

                    byte* ptr = mem.BaseAddress;
                    byte* limit = mem.BaseAddress + mem.RegionSize - STAR_ENTRY_SIZE;

                    while (ptr < limit)
                    {
                        try
                        {
                            if (MatchesPattern(ptr, searchPattern))
                            {
                                byte* floatStart = ptr - STAR_NAME_OFFSET;

                                if (floatStart >= mem.BaseAddress && ValidateStructure(floatStart))
                                {
                                    modApi.Log(string.Format("GalaxyExtractor: Found structure at 0x{0:X} in region {1}/{2}",
                                        (long)floatStart, regionsScanned, regions.Count));

                                    var stars = GetStarsFromRegion(mem, floatStart);

                                    if (stars != null && stars.Length > MIN_STAR_COUNT)
                                    {
                                        modApi.Log(string.Format("GalaxyExtractor: Successfully parsed {0} stars", stars.Length));
                                        return stars;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log but continue - individual pointer reads may fail in valid scenarios
                            if (regionsScanned % REGION_PROGRESS_INTERVAL == 0)
                            {
                                modApi.Log(string.Format("GalaxyExtractor: Non-fatal error scanning at 0x{0:X}: {1}",
                                    (long)ptr, ex.Message));
                            }
                        }

                        ptr++;
                    }
                }

                return null;
            }
            finally
            {
                ResumeGC();
            }
        }

        private unsafe bool MatchesPattern(byte* ptr, byte[] pattern)
        {
            try
            {
                for (int i = 0; i < pattern.Length; i++)
                {
                    if (ptr[i] != pattern[i])
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool ValidateStructure(byte* floatStart)
        {
            try
            {
                // Validate structure format
                ushort strLen = *(ushort*)(floatStart + STAR_NAME_LENGTH_OFFSET);
                if (strLen < MIN_STAR_NAME_LENGTH || strLen > MAX_STAR_NAME_LENGTH)
                    return false;

                // Verify the name matches our search pattern
                byte* namePtr = floatStart + STAR_NAME_OFFSET;
                for (int i = 0; i < searchPattern.Length; i++)
                {
                    if (namePtr[i] != searchPattern[i])
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private unsafe StarWithName[] GetStarsFromRegion(Kernel32.MEMORY_BASIC_INFORMATION mem, byte* arrayStart)
        {
            try
            {
                var stars = new List<StarWithName>();
                byte* ptr = arrayStart;
                byte* limit = mem.BaseAddress + mem.RegionSize - STAR_ENTRY_SIZE;
                int consecutiveFailures = 0;

                while (ptr < limit && consecutiveFailures < MAX_CONSECUTIVE_PARSE_FAILURES)
                {
                    try
                    {
                        var star = GetStarEntry((float*)ptr);
                        if (star != null)
                        {
                            stars.Add(star.Value);
                            consecutiveFailures = 0;
                        }
                        else
                        {
                            consecutiveFailures++;
                        }
                    }
                    catch (Exception ex)
                    {
                        consecutiveFailures++;
                        if (consecutiveFailures == MAX_CONSECUTIVE_PARSE_FAILURES)
                        {
                            modApi.Log(string.Format("GalaxyExtractor: Stopped parsing after {0} consecutive failures: {1}",
                                MAX_CONSECUTIVE_PARSE_FAILURES, ex.Message));
                        }
                    }

                    ptr += STAR_ENTRY_SIZE;
                }

                return stars.Count > MIN_STAR_COUNT ? stars.ToArray() : null;
            }
            catch (Exception ex)
            {
                modApi.Log(string.Format("GalaxyExtractor: Error parsing star array: {0}", ex.Message));
                return null;
            }
        }

        private unsafe StarWithName? GetStarEntry(float* ptr)
        {
            try
            {
                float x = ptr[STAR_X_OFFSET / 4];
                float y = ptr[STAR_Y_OFFSET / 4];
                float z = ptr[STAR_Z_OFFSET / 4];
                ushort strLen = *(ushort*)((byte*)ptr + STAR_NAME_LENGTH_OFFSET);
                byte* strPtr = (byte*)ptr + STAR_NAME_OFFSET;

                if (strLen < MIN_STAR_NAME_LENGTH || strLen > MAX_STAR_NAME_LENGTH)
                    return null;

                byte[] nameBytes = new byte[strLen];
                Marshal.Copy((IntPtr)strPtr, nameBytes, 0, strLen);
                string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                if (string.IsNullOrWhiteSpace(name))
                    return null;

                // Convert floats to ints (coordinates are whole numbers)
                int intX = (int)x;
                int intY = (int)y;
                int intZ = (int)z;

                return new StarWithName
                {
                    x = intX,
                    y = intY,
                    z = intZ,
                    name = name
                };
            }
            catch
            {
                return null;
            }
        }

        private unsafe bool GetNextMemoryRegion(ref Kernel32.MEMORY_BASIC_INFORMATION memInfo,
            Kernel32.SYSTEM_INFO sysInfo, int memInfoSize)
        {
            byte* baseAddress = (memInfo.BaseAddress != null)
                ? memInfo.BaseAddress
                : sysInfo.lpMinimumApplicationAddress;

            while (baseAddress < sysInfo.lpMaximumApplicationAddress)
            {
                baseAddress += memInfo.RegionSize;
                if (Kernel32.VirtualQuery(baseAddress, out memInfo, memInfoSize) == 0)
                {
                    baseAddress += sysInfo.dwPageSize;
                    continue;
                }

                if (IsValidMemoryRegion(memInfo))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsValidMemoryRegion(Kernel32.MEMORY_BASIC_INFORMATION memInfo)
        {
            // Only scan writable, committed, private memory where game data lives
            return memInfo.Protect == Kernel32.PAGE_READWRITE &&
                   memInfo.State == Kernel32.MEM_COMMIT &&
                   memInfo.Type == Kernel32.MEM_PRIVATE;
        }

        private void PauseGC()
        {
            try
            {
                GC.TryStartNoGCRegion(NO_GC_SIZE);
            }
            catch (Exception ex)
            {
                modApi.Log(string.Format("GalaxyExtractor: Could not pause GC: {0}", ex.Message));
            }
        }

        private void ResumeGC()
        {
            try
            {
                GC.EndNoGCRegion();
            }
            catch (Exception ex)
            {
                modApi.Log(string.Format("GalaxyExtractor: Could not resume GC: {0}", ex.Message));
            }
        }

        #endregion

        #region Data Validation

        private void ValidateExtractedData(StarWithName[] stars)
        {
            if (stars == null || stars.Length == 0)
                return;

            // Check for duplicate names
            var duplicates = stars.GroupBy(s => s.name).Where(g => g.Count() > 1).ToList();
            if (duplicates.Any())
            {
                modApi.Log(string.Format("GalaxyExtractor: Warning - {0} duplicate star names found (e.g., '{1}' appears {2} times)",
                    duplicates.Count, duplicates.First().Key, duplicates.First().Count()));
            }

            // Log coordinate ranges for validation
            int minX = stars.Min(s => s.x);
            int maxX = stars.Max(s => s.x);
            int minY = stars.Min(s => s.y);
            int maxY = stars.Max(s => s.y);
            int minZ = stars.Min(s => s.z);
            int maxZ = stars.Max(s => s.z);

            modApi.Log(string.Format("GalaxyExtractor: Coordinate ranges - X:[{0},{1}] Y:[{2},{3}] Z:[{4},{5}]",
                minX, maxX, minY, maxY, minZ, maxZ));
        }

        #endregion

        #region File Output

        private void WriteCSV(StarWithName[] stars)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
                {
                    writer.WriteLine(stars.Length);

                    foreach (var star in stars)
                    {
                        string safeName = star.name;
                        if (safeName.Contains(","))
                        {
                            safeName = string.Format("\"{0}\"", safeName);
                        }
                        writer.WriteLine(string.Format("{0},{1},{2},{3}", star.x, star.y, star.z, safeName));
                    }
                }
            }
            catch (Exception ex)
            {
                modApi.Log(string.Format("GalaxyExtractor: Error writing CSV to {0}: {1}", outputPath, ex.Message));
                throw;
            }
        }

        #endregion

        #region Data Structures

        private struct StarWithName
        {
            public int x;
            public int y;
            public int z;
            public string name;
        }

        private class SectorsFile
        {
            public List<SolarSystem> SolarSystems { get; set; }
        }

        private class SolarSystem
        {
            public string Name { get; set; }
        }

        private static class Kernel32
        {
            public const int PAGE_READWRITE = 0x04;
            public const int MEM_COMMIT = 0x1000;
            public const int MEM_PRIVATE = 0x20000;

            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct MEMORY_BASIC_INFORMATION
            {
                public byte* BaseAddress;
                public byte* AllocationBase;
                public uint AllocationProtect;
                public ulong RegionSize;
                public uint State;
                public uint Protect;
                public uint Type;
            }

            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct SYSTEM_INFO
            {
                public uint dwOemId;
                public uint dwPageSize;
                public byte* lpMinimumApplicationAddress;
                public byte* lpMaximumApplicationAddress;
                public uint dwActiveProcessorMask;
                public uint dwNumberOfProcessors;
                public uint dwProcessorType;
                public uint dwAllocationGranularity;
                public ushort wProcessorLevel;
                public ushort wProcessorRevision;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            public static unsafe extern int VirtualQuery(
                byte* lpAddress,
                out MEMORY_BASIC_INFORMATION lpBuffer,
                int dwLength
            );

            [DllImport("kernel32.dll", SetLastError = true)]
            public static unsafe extern void GetSystemInfo(
                out SYSTEM_INFO lpSystemInfo
            );
        }

        #endregion
    }
}
