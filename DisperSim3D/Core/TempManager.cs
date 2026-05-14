#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DisperSim3D.Core
{
    public sealed class TempEntry
    {
        public string Path { get; }
        public string Category { get; }
        public long SizeBytes { get; }
        public DateTime LastWriteUtc { get; }

        internal TempEntry(string path, string category, long sizeBytes, DateTime lastWriteUtc)
        {
            Path = path;
            Category = category;
            SizeBytes = sizeBytes;
            LastWriteUtc = lastWriteUtc;
        }
    }

    public static class TempManager
    {
        private static readonly HashSet<string> ActivePaths = new(StringComparer.OrdinalIgnoreCase);

        private static readonly (string Prefix, string Category)[] KnownPrefixes =
        {
            ("DisperSim_OpenFOAM",          "OpenFOAM Cases"),
            ("DisperSim3D_fx3d_sim_",       "FluidX3D Dispersion"),
            ("DisperSim3D_fx3dsteady_sim_",  "FluidX3D Steady"),
            ("DisperSim3D_fx3dfire_sim_",    "FluidX3D Fire"),
            ("DisperSim3D_fx3d_",           "FluidX3D Wind Field"),
            ("DisperSim_GP_",               "Gaussian Puff/Plume"),
            ("DisperSim_OF_test_",          "OpenFOAM Validation"),
            ("DisperSim3D",                 "Project Sessions"),
        };

        private static readonly string[] KnownLogFiles =
        {
            "fluidx3d_bridge.log",
            "dispersim3d_view.log",
        };

        public static void RegisterActive(string path)
        {
            if (!string.IsNullOrEmpty(path))
                lock (ActivePaths) ActivePaths.Add(Path.GetFullPath(path));
        }

        public static void UnregisterActive(string path)
        {
            if (!string.IsNullOrEmpty(path))
                lock (ActivePaths) ActivePaths.Remove(Path.GetFullPath(path));
        }

        private static bool IsActive(string path)
        {
            lock (ActivePaths)
            {
                var full = Path.GetFullPath(path);
                return ActivePaths.Any(a =>
                    full.Equals(a, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static List<TempEntry> Scan()
        {
            var entries = new List<TempEntry>();
            var tempDir = Path.GetTempPath();

            foreach (var (prefix, category) in KnownPrefixes)
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(tempDir, prefix + "*"))
                    {
                        try
                        {
                            var info = new DirectoryInfo(dir);
                            long size = GetDirectorySize(info);
                            entries.Add(new TempEntry(dir, category, size, info.LastWriteTimeUtc));
                        }
                        catch { }
                    }

                    if (prefix == "DisperSim_OpenFOAM")
                    {
                        var ofDir = Path.Combine(tempDir, "DisperSim_OpenFOAM");
                        if (Directory.Exists(ofDir))
                        {
                            foreach (var sub in Directory.EnumerateDirectories(ofDir))
                            {
                                try
                                {
                                    var info = new DirectoryInfo(sub);
                                    long size = GetDirectorySize(info);
                                    entries.Add(new TempEntry(sub, "OpenFOAM Cases", size, info.LastWriteTimeUtc));
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }

            foreach (var logName in KnownLogFiles)
            {
                try
                {
                    var logPath = Path.Combine(tempDir, logName);
                    if (File.Exists(logPath))
                    {
                        var fi = new FileInfo(logPath);
                        entries.Add(new TempEntry(logPath, "Log Files", fi.Length, fi.LastWriteTimeUtc));
                    }
                }
                catch { }
            }

            return entries;
        }

        public static (int deleted, long freedBytes) PurgeOlderThan(TimeSpan maxAge)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            var entries = Scan().Where(e => e.LastWriteUtc < cutoff).ToList();
            return DeleteEntries(entries);
        }

        public static (int deleted, long freedBytes) PurgeAll()
        {
            return DeleteEntries(Scan());
        }

        public static (int deleted, long freedBytes) PurgeCategory(string category)
        {
            var entries = Scan().Where(e =>
                e.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
            return DeleteEntries(entries);
        }

        public static (long totalBytes, int entryCount, Dictionary<string, long> byCategory) GetSummary()
        {
            var entries = Scan();
            long total = entries.Sum(e => e.SizeBytes);
            var byCategory = entries
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.SizeBytes));
            return (total, entries.Count, byCategory);
        }

        public static void StartupPurge(int maxAgeDays = 7)
        {
            try { PurgeOlderThan(TimeSpan.FromDays(maxAgeDays)); }
            catch { }
        }

        private static (int deleted, long freedBytes) DeleteEntries(List<TempEntry> entries)
        {
            int deleted = 0;
            long freed = 0;

            foreach (var entry in entries)
            {
                if (IsActive(entry.Path)) continue;

                try
                {
                    if (File.Exists(entry.Path))
                    {
                        File.Delete(entry.Path);
                        deleted++;
                        freed += entry.SizeBytes;
                    }
                    else if (Directory.Exists(entry.Path))
                    {
                        Directory.Delete(entry.Path, recursive: true);
                        deleted++;
                        freed += entry.SizeBytes;
                    }
                }
                catch { }
            }

            return (deleted, freed);
        }

        private static long GetDirectorySize(DirectoryInfo dir)
        {
            long size = 0;
            try
            {
                foreach (var fi in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { size += fi.Length; }
                    catch { }
                }
            }
            catch { }
            return size;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
