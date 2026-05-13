using System;
using Avalonia;

namespace DisperSim3D.UI.Avalonia
{
    /// <summary>
    /// Cross-platform entry point. The Avalonia.App lifetime drives the main loop
    /// on every OS; on Linux it talks to X11/Wayland directly, on Windows it sits
    /// on top of Direct2D, on macOS it uses CoreGraphics. From a developer
    /// perspective the only thing that differs vs. the WinForms App is that
    /// there's no <c>[STAThread]</c> required.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
            => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
