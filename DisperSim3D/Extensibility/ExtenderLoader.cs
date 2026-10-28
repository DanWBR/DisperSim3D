using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace DisperSim3D.Extensibility
{
    /// <summary>What a load found: the collections, and a line per decision or failure.</summary>
    public sealed class ExtenderLoadResult
    {
        public List<IExtenderCollection> Collections { get; } = new List<IExtenderCollection>();

        public List<string> Messages { get; } = new List<string>();
    }

    /// <summary>
    /// Finds and loads extender assemblies, the way DWSIM's FormMain does with its
    /// "extenders" folders: a folder next to the executable for what ships with an
    /// install, and one under the user's application data for what the user adds.
    ///
    /// An extender is any DLL whose name contains "Extender", either directly in a
    /// folder or one level down (extenders/&lt;name&gt;/&lt;name&gt;.Extender.dll), so an
    /// extender can bring its own dependencies in its own folder. Each loads in a
    /// load context of its own that resolves those dependencies — managed and
    /// native — from beside it, while the host's assemblies, DisperSim3D.dll above
    /// all, stay shared: an extender's Scene3D has to be the host's Scene3D.
    /// </summary>
    public static class ExtenderLoader
    {
        private static readonly object Gate = new object();
        private static ExtenderLoadResult? _cached;

        /// <summary>The folders searched, in order.</summary>
        public static IReadOnlyList<string> SearchDirectories
        {
            get
            {
                var dirs = new List<string>
                {
                    Path.Combine(AppContext.BaseDirectory, "extenders"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                 "DisperSim3D", "extenders")
                };
                string? extra = Environment.GetEnvironmentVariable("DISPERSIM3D_EXTENDERS");
                if (!string.IsNullOrWhiteSpace(extra))
                    dirs.AddRange(extra.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
                return dirs;
            }
        }

        /// <summary>
        /// Loads every extender once per process and returns the same result on
        /// later calls, so the two places that build menus see the same instances.
        /// </summary>
        public static ExtenderLoadResult LoadAll()
        {
            lock (Gate)
            {
                return _cached ??= Load(SearchDirectories);
            }
        }

        /// <summary>Loads the extenders found in <paramref name="directories"/>, without caching.</summary>
        public static ExtenderLoadResult Load(IEnumerable<string> directories)
        {
            var result = new ExtenderLoadResult();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string dir in directories)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (string dll in Candidates(dir))
                {
                    string full = Path.GetFullPath(dll);
                    if (!seenFiles.Add(full)) continue;
                    LoadAssembly(full, result, seenIds);
                }
            }

            return result;
        }

        private static IEnumerable<string> Candidates(string dir)
        {
            IEnumerable<string> top = Directory.EnumerateFiles(dir, "*Extender*.dll", SearchOption.TopDirectoryOnly);
            IEnumerable<string> nested = Directory.EnumerateDirectories(dir)
                .SelectMany(sub => Directory.EnumerateFiles(sub, "*Extender*.dll", SearchOption.TopDirectoryOnly));
            return top.Concat(nested).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        }

        private static void LoadAssembly(string path, ExtenderLoadResult result, HashSet<string> seenIds)
        {
            Assembly asm;
            try
            {
                var context = new ExtenderLoadContext(path);
                asm = context.LoadFromAssemblyPath(path);
            }
            catch (Exception ex)
            {
                result.Messages.Add($"skipped {path}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            Type[] types;
            try
            {
                types = asm.GetExportedTypes();
            }
            catch (Exception ex)
            {
                result.Messages.Add($"skipped {Path.GetFileName(path)}: cannot list its types ({ex.GetType().Name}: {ex.Message})");
                return;
            }

            int found = 0;
            foreach (Type t in types)
            {
                if (t.IsAbstract || t.IsInterface || !typeof(IExtenderCollection).IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null)
                {
                    result.Messages.Add($"{t.FullName}: no parameterless constructor, ignored");
                    continue;
                }

                try
                {
                    var collection = (IExtenderCollection)Activator.CreateInstance(t)!;
                    if (!seenIds.Add(collection.ID))
                    {
                        result.Messages.Add($"{t.FullName}: ID '{collection.ID}' already loaded, ignored");
                        continue;
                    }
                    result.Collections.Add(collection);
                    found++;
                    result.Messages.Add($"loaded '{collection.DisplayText}' ({collection.Collection.Count} command(s)) from {path}");
                }
                catch (Exception ex)
                {
                    Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                    result.Messages.Add($"{t.FullName}: failed to start ({inner.GetType().Name}: {inner.Message})");
                }
            }

            if (found == 0)
                result.Messages.Add($"{Path.GetFileName(path)}: no IExtenderCollection in it");
        }
    }

    /// <summary>
    /// Load context of one extender. Its own dependencies resolve from its
    /// .deps.json and folder; anything the host already provides — DisperSim3D.dll,
    /// the framework — falls through to the default context and is shared.
    /// </summary>
    internal sealed class ExtenderLoadContext : AssemblyLoadContext
    {
        /// <summary>Host assemblies an extender must never load a copy of.</summary>
        private static readonly HashSet<string> Shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DisperSim3D",
            "DisperSim3D.UI.Wpf",
            "DisperSim3D.UI.Avalonia",
            "DWSIMCore.Foundation"
        };

        private readonly AssemblyDependencyResolver _resolver;

        public ExtenderLoadContext(string mainAssemblyPath)
            : base(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? name = assemblyName.Name;
            if (name == null || Shared.Contains(name)) return null;

            // Already loaded by the host: share it rather than load a second copy.
            foreach (Assembly loaded in Default.Assemblies)
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    return null;

            // Shipped with the host but not loaded yet: the host's copy serves it
            // too, so an extender never runs against a library version of its own
            // where the host has one.
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll")))
                return null;

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path != null && File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path != null && File.Exists(path) ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }
    }
}
