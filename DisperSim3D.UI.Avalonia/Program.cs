using System;
using Avalonia;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;

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
        {
            // Register the Material Design Icons provider BEFORE building the
            // app so XAML <i:Icon Value="mdi-..."/> resolves to the bundled
            // SVG path geometry. Register is additive — chain more providers
            // (FontAwesome, Lucide, …) here if we need broader coverage.
            IconProvider.Current.Register<MaterialDesignIconProvider>();

            DisperSim3D.Core.TempManager.StartupPurge();

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
