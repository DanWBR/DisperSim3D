#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace DisperSim3D.Core
{
    internal static class BuiltinAssetResolver
    {
        private static readonly Dictionary<string, string[]> CandidatePaths = new()
        {
            ["builtin:sky_clear_day"] = new[]
            {
                @"Assets\Sky\Clear Day Road.jpg",
                @"Sky\Clear Day Road.jpg"
            },
            ["builtin:sky_sunset"] = new[]
            {
                @"Assets\Sky\Sunset Rocky Coast.jpg",
                @"Sky\Sunset Rocky Coast.jpg"
            },
            ["builtin:sky_snowy_mountains"] = new[]
            {
                @"Assets\Sky\Snowy Mountains.hdr",
                @"Sky\Snowy Mountains.hdr"
            },
            ["builtin:ground_woodland"] = new[]
            {
                @"Assets\Ground\Woodland Terrain_Diffuse.png",
                @"Woodland Terrain\Textures\Woodland Terrain_Diffuse.png"
            }
        };

        private static readonly string[] SearchRoots;

        static BuiltinAssetResolver()
        {
            var roots = new List<string>();
            var appDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(appDir))
                roots.Add(appDir);

            var propsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "3D Props");
            if (Directory.Exists(propsDir))
                roots.Add(propsDir);

            SearchRoots = roots.ToArray();
        }

        public static string Resolve(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith("builtin:"))
                return key;

            if (!CandidatePaths.TryGetValue(key, out var candidates))
                return key;

            foreach (var root in SearchRoots)
            {
                foreach (var rel in candidates)
                {
                    var full = Path.Combine(root, rel);
                    if (File.Exists(full))
                        return full;
                }
            }

            return key;
        }
    }
}
