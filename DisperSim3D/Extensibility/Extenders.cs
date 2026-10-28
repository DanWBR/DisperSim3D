#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DisperSim3D.Models;

namespace DisperSim3D.Extensibility
{
    /// <summary>
    /// Top-level menu an extender's items go into. The names follow the menus the
    /// two desktop UIs share; a host that has no such menu places the items in File.
    /// </summary>
    public enum ExtenderCategory
    {
        File = 0,
        Edit = 1,
        View = 2,
        Tools = 3,
        Help = 4
    }

    /// <summary>
    /// A group of menu commands shipped in one extender assembly — the unit a host
    /// discovers. Modelled on DWSIM's IExtenderCollection: an extender DLL exposes
    /// one or more public classes implementing this, each with a parameterless
    /// constructor.
    /// </summary>
    public interface IExtenderCollection
    {
        /// <summary>Stable identifier; a second collection with the same ID is ignored.</summary>
        string ID { get; }

        string Description { get; }

        string DisplayText { get; }

        ExtenderCategory Category { get; }

        IReadOnlyList<IExtender> Collection { get; }
    }

    /// <summary>
    /// One menu command. Unlike DWSIM's IExtender, which receives the host's main
    /// window as an object of its own UI framework, a DisperSim 3D extender talks
    /// to the host only through <see cref="IExtenderHost"/>, so the same assembly
    /// runs unchanged in the WinForms shell and in the Avalonia UI.
    /// </summary>
    public interface IExtender
    {
        string ID { get; }

        string DisplayText { get; }

        /// <summary>The menu icon, as the bytes of a PNG, or null for none.</summary>
        byte[]? DisplayImage { get; }

        /// <summary>
        /// Index in the category's menu to insert at, or a negative value to add
        /// the item at the end of the menu's own commands (above Exit, for File).
        /// </summary>
        int InsertAtPosition { get; }

        /// <summary>
        /// Runs the command. Called on the UI thread; long work belongs on a
        /// worker (<see cref="Task.Run(Func{Task})"/>), and every
        /// <see cref="IExtenderHost"/> member may be called from any thread.
        /// </summary>
        Task RunAsync(IExtenderHost host);
    }

    /// <summary>A file type offered in an open or save dialog: a name and its patterns ("*.ifx").</summary>
    public sealed class ExtenderFileFilter
    {
        public ExtenderFileFilter(string name, params string[] patterns)
        {
            Name = name;
            Patterns = patterns;
        }

        public string Name { get; }

        public IReadOnlyList<string> Patterns { get; }
    }

    /// <summary>
    /// What a host offers an extender. Each UI implements it once; every member
    /// marshals to the UI thread itself, so an extender can call it from a worker.
    /// </summary>
    public interface IExtenderHost
    {
        /// <summary>"WinForms" or "Avalonia".</summary>
        string HostName { get; }

        /// <summary>
        /// The host's main window, typed by its own UI framework — the escape hatch
        /// for an extender written for one host only. Portable extenders ignore it.
        /// </summary>
        object? MainWindow { get; }

        /// <summary>The project open in the host, or null when there is none.</summary>
        Scene3D? CurrentScene { get; }

        /// <summary>Path of the open project, or null when it has never been saved.</summary>
        string? CurrentProjectPath { get; }

        /// <summary>Asks the user for a file to open; null when cancelled.</summary>
        Task<string?> PickOpenFileAsync(string title, IReadOnlyList<ExtenderFileFilter> filters);

        /// <summary>Asks the user where to save a file; null when cancelled.</summary>
        Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<ExtenderFileFilter> filters);

        /// <summary>
        /// Opens a project file exactly as File → Open does, replacing the current
        /// project.
        /// </summary>
        Task OpenProjectAsync(string path);

        Task ShowMessageAsync(string title, string message, bool isError = false);

        /// <summary>Text for the host's status bar.</summary>
        void SetStatus(string text);
    }

    /// <summary>
    /// Optional, on an <see cref="IExtenderCollection"/>: called once by each host
    /// after its main window is up. The place for an extender that works in the
    /// background rather than from a menu — DWSIM's InitializationScript category,
    /// as an interface.
    /// </summary>
    public interface IExtenderStartup
    {
        void OnHostStarted(IExtenderHost host);
    }

    /// <summary>
    /// Second version of the host contract. <see cref="IExtenderHost"/> stays as it
    /// is, as DWSIM's IExtender does, and what a newer extender needs comes here: an
    /// extender checks for it with <c>host is IExtenderHost2</c>.
    /// </summary>
    public interface IExtenderHost2 : IExtenderHost
    {
        /// <summary>Saves the open project to <paramref name="path"/>, as File → Save does.</summary>
        Task SaveProjectAsync(string path);

        /// <summary>What the host's status bar shows now.</summary>
        string StatusText { get; }

        /// <summary>
        /// The 3D view as a PNG. A host renders at the requested size when it can and
        /// at the view's on-screen size otherwise; null when it has no view to render.
        /// </summary>
        Task<byte[]?> CaptureViewportPngAsync(int width, int height);

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread and completes when it does,
        /// for code that lives on another thread and must run a command or read the
        /// project the way the UI does.
        /// </summary>
        Task RunOnUiThreadAsync(Func<Task> action);
    }
}
