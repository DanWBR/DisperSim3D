using System;
using System.IO;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Translates bundle-relative URIs (bundle://...) used inside the saved project.xml
    /// to absolute paths under an extracted bundle root, and back. The in-memory
    /// <see cref="DisperSim3D.Models.Scene3D"/> always carries absolute paths; the
    /// bundle:// scheme only appears in serialized form.
    /// </summary>
    public static class BundlePathResolver
    {
        public const string Scheme = "bundle://";

        public static bool IsBundlePath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Convert a bundle:// URI to an absolute path under <paramref name="bundleRoot"/>.</summary>
        public static string ToAbsolute(string bundlePath, string bundleRoot)
        {
            if (string.IsNullOrEmpty(bundlePath)) return bundlePath;
            if (!IsBundlePath(bundlePath)) return bundlePath;
            if (string.IsNullOrEmpty(bundleRoot)) return bundlePath;

            var relative = bundlePath.Substring(Scheme.Length).TrimStart('/', '\\');
            relative = relative.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(bundleRoot, relative);
        }

        /// <summary>Convert an absolute path under <paramref name="bundleRoot"/> back to a bundle:// URI.</summary>
        public static string ToBundle(string absolutePath, string bundleRoot)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;
            if (string.IsNullOrEmpty(bundleRoot)) return absolutePath;
            if (IsBundlePath(absolutePath)) return absolutePath;

            string fullAbs;
            string fullRoot;
            try
            {
                fullAbs = Path.GetFullPath(absolutePath);
                fullRoot = Path.GetFullPath(bundleRoot);
            }
            catch
            {
                return absolutePath;
            }

            if (!fullAbs.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return absolutePath;

            var rel = fullAbs.Substring(fullRoot.Length).TrimStart('/', '\\');
            rel = rel.Replace('\\', '/');
            return Scheme + rel;
        }

        /// <summary>Returns true if the given absolute path is inside the bundle root.</summary>
        public static bool IsInsideBundle(string absolutePath, string bundleRoot)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(bundleRoot)) return false;
            try
            {
                var fullAbs = Path.GetFullPath(absolutePath);
                var fullRoot = Path.GetFullPath(bundleRoot);
                return fullAbs.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
