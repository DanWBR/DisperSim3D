using System.Collections.Generic;
using System.IO;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Decides which files of an OpenFOAM case to copy into a .dsproj bundle.
    /// Two policies: <see cref="BundleEmbedMode.ResultsOnly"/> (small, replay-only)
    /// and <see cref="BundleEmbedMode.FullCase"/> (re-runnable, large).
    /// </summary>
    public static class CaseBundler
    {
        /// <summary>
        /// Enumerates absolute file paths to include from <paramref name="caseRoot"/> for
        /// the given embed mode. Returned paths preserve the directory structure under caseRoot.
        /// </summary>
        public static IEnumerable<string> EnumerateFilesToBundle(string caseRoot, BundleEmbedMode mode)
        {
            if (string.IsNullOrEmpty(caseRoot) || !Directory.Exists(caseRoot))
                yield break;

            // FluidX3D wind fields drop a single windfield.bin at the case root — there's
            // no OpenFOAM directory structure to enumerate. If we detect it, just bundle
            // that file and exit; the rest of the OpenFOAM rules don't apply.
            string fluidBin = Path.Combine(caseRoot, "windfield.bin");
            if (File.Exists(fluidBin))
            {
                yield return fluidBin;
                yield break;
            }

            // FluidX3D dispersion case: a flat directory of <time>.bin concentration
            // snapshots, no OpenFOAM structure. Detected by absence of system/controlDict
            // combined with presence of .bin files at the root. Just yield all .bin files.
            bool hasControlDict = File.Exists(Path.Combine(caseRoot, "system", "controlDict"));
            if (!hasControlDict)
            {
                var rootBins = Directory.EnumerateFiles(caseRoot, "*.bin",
                    SearchOption.TopDirectoryOnly).ToList();
                if (rootBins.Count > 0)
                {
                    foreach (var f in rootBins) yield return f;
                    yield break;
                }
            }

            if (mode == BundleEmbedMode.FullCase)
            {
                foreach (var f in EnumerateFullCase(caseRoot))
                    yield return f;
                yield break;
            }

            foreach (var f in EnumerateResultsOnly(caseRoot))
                yield return f;
        }

        private static IEnumerable<string> EnumerateFullCase(string caseRoot)
        {
            foreach (var f in Directory.EnumerateFiles(caseRoot, "*", SearchOption.AllDirectories))
            {
                var rel = GetRelative(caseRoot, f);
                if (IsExcludedFromFullCase(rel)) continue;
                yield return f;
            }
        }

        private static IEnumerable<string> EnumerateResultsOnly(string caseRoot)
        {
            // Always include blockMeshDict and a couple of small dicts so the case is recognizable.
            string blockMeshDict = Path.Combine(caseRoot, "system", "blockMeshDict");
            if (File.Exists(blockMeshDict)) yield return blockMeshDict;

            string controlDict = Path.Combine(caseRoot, "system", "controlDict");
            if (File.Exists(controlDict)) yield return controlDict;

            // 0/ initial conditions (small).
            string zero = Path.Combine(caseRoot, "0");
            if (Directory.Exists(zero))
            {
                foreach (var f in Directory.EnumerateFiles(zero, "*", SearchOption.TopDirectoryOnly))
                    yield return f;
            }

            // constant/polyMesh — needed by readers; keep as-is (typically text in small cases).
            string polyMesh = Path.Combine(caseRoot, "constant", "polyMesh");
            if (Directory.Exists(polyMesh))
            {
                foreach (var f in Directory.EnumerateFiles(polyMesh, "*", SearchOption.TopDirectoryOnly))
                {
                    var info = new FileInfo(f);
                    if (info.Length > 50L * 1024 * 1024) continue;
                    yield return f;
                }
            }

            // Numeric timestep folders (the actual results).
            foreach (var dir in Directory.EnumerateDirectories(caseRoot))
            {
                string name = Path.GetFileName(dir);
                if (!IsTimeFolder(name)) continue;
                if (name == "0") continue; // already handled
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    yield return f;
            }

            // postProcessing/ — small probe & sample data.
            string postProc = Path.Combine(caseRoot, "postProcessing");
            if (Directory.Exists(postProc))
            {
                foreach (var f in Directory.EnumerateFiles(postProc, "*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(f);
                    if (info.Length > 5L * 1024 * 1024) continue;
                    yield return f;
                }
            }
        }

        private static bool IsExcludedFromFullCase(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            var norm = relativePath.Replace('\\', '/');
            if (norm.StartsWith("processor", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (norm.StartsWith("dynamicCode/", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (norm.Equals("dynamicCode", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsTimeFolder(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            double dummy;
            return double.TryParse(name, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out dummy);
        }

        private static string GetRelative(string root, string fullPath)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var fullAbs = Path.GetFullPath(fullPath);
            if (fullAbs.StartsWith(fullRoot, System.StringComparison.OrdinalIgnoreCase))
                return fullAbs.Substring(fullRoot.Length).TrimStart(Path.DirectorySeparatorChar);
            return Path.GetFileName(fullPath);
        }
    }
}
