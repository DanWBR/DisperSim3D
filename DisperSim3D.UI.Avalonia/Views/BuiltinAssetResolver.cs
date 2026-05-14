#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace DisperSim3D.UI.Avalonia.Views
{
    internal sealed class DecorationPreset
    {
        public string Label { get; }
        public string ObjKey { get; }
        public double DefaultScale { get; }
        public string Icon { get; }

        public DecorationPreset(string label, string objKey,
            double defaultScale = 1.0, string icon = "mdi-pine-tree")
        {
            Label = label;
            ObjKey = objKey;
            DefaultScale = defaultScale;
            Icon = icon;
        }
    }

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
            },
            ["builtin:deco_european_beech"] = new[]
            {
                @"Assets\Trees\European Beech Tree.obj",
                @"European Beech Tree\European Beech Tree.obj"
            },
            ["builtin:deco_tulip_poplar"] = new[]
            {
                @"Assets\Trees\Tulip Poplar Tree.obj",
                @"Tulip Poplar Tree\Tulip Poplar Tree.obj"
            },
            ["builtin:deco_cloud"] = new[]
            {
                @"Assets\Cloud\Cloud.obj",
                @"Cloud\Cloud.obj"
            },
            ["builtin:deco_sphere_tank_old"] = new[]
            {
                @"Assets\Tanks\Old Sphere Tank.obj",
                @"Old Sphere Tank\Old Sphere Tank.obj"
            },
            ["builtin:deco_sphere_tank_flammable"] = new[]
            {
                @"Assets\Tanks\Sphere Tank Flammable.obj",
                @"Sphere Tank Flammable\Sphere Tank Flammable.obj"
            },
            ["builtin:deco_industrial_module"] = new[]
            {
                @"Assets\Industrial\Industrial Module.obj",
                @"Industrial Module\Industrial Module.obj"
            },
            ["builtin:deco_tank_containers"] = new[]
            {
                @"Assets\Tanks\Tank Containers.obj",
                @"Tank Containers\Tank Containers.obj"
            }
        };

        public static readonly DecorationPreset[] DecorationPresets = new[]
        {
            new DecorationPreset("European Beech Tree", "builtin:deco_european_beech",
                defaultScale: 1.0, icon: "mdi-tree"),
            new DecorationPreset("Tulip Poplar Tree", "builtin:deco_tulip_poplar",
                defaultScale: 1.0, icon: "mdi-tree"),
            new DecorationPreset("Cloud", "builtin:deco_cloud",
                defaultScale: 5.0, icon: "mdi-cloud-outline"),
            new DecorationPreset("Old Sphere Tank", "builtin:deco_sphere_tank_old",
                defaultScale: 1.0, icon: "mdi-silo"),
            new DecorationPreset("Sphere Tank (Flammable)", "builtin:deco_sphere_tank_flammable",
                defaultScale: 1.0, icon: "mdi-silo"),
            new DecorationPreset("Industrial Module", "builtin:deco_industrial_module",
                defaultScale: 1.0, icon: "mdi-factory"),
            new DecorationPreset("Tank Containers", "builtin:deco_tank_containers",
                defaultScale: 1.0, icon: "mdi-truck-cargo-container"),
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

        public static bool IsBuiltin(string? path)
            => !string.IsNullOrEmpty(path) && path.StartsWith("builtin:");
    }
}
