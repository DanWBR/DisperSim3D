#nullable enable
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>AboutDialog</c>. Pulls the
    /// product name, version, description and copyright from the engine
    /// assembly's attributes (so the dialog stays in sync with the csproj
    /// metadata), plus a runtime block showing .NET / OS / Avalonia / core
    /// count so any bug report has the environment baked in.
    /// </summary>
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();

            // Read product info from the engine assembly so a single
            // <Version>1.0.0</Version> bump in DisperSim3D.csproj propagates
            // everywhere automatically — engine, WinForms App, Avalonia UI.
            Assembly engine = typeof(DisperSim3D.Core.SceneFileLoader).Assembly;
            string title = engine.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                ?? "DisperSim 3D";
            string version = engine.GetName().Version?.ToString(3) ?? "1.0.0";
            string description = engine.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
                ?? "Open Source 3D Gas Dispersion Analysis Library";
            string copyright = engine.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                ?? "Copyright © Daniel Wagner Oliveira de Medeiros";
            string company = engine.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
                ?? "Daniel Wagner Oliveira de Medeiros";

            TxtTitle.Text = title;
            TxtVersion.Text = "Version " + version;
            TxtDescription.Text = description;
            TxtCopyright.Text = copyright;
            TxtCompany.Text = company;

            // Runtime info — same fields the diagnostics smoke window
            // reports, just in a tidier table form.
            TxtDotNet.Text = Environment.Version.ToString();
            TxtOs.Text = RuntimeInformation.OSDescription.Trim();
            TxtAvalonia.Text =
                typeof(global::Avalonia.Application).Assembly.GetName().Version?.ToString()
                ?? "(unknown)";
            TxtCores.Text = Environment.ProcessorCount.ToString();
        }

        private void BtnOK_Click(object? sender, RoutedEventArgs e) => Close();

        private void BtnWebsite_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/DanWBR/DisperSim3D",
                    UseShellExecute = true
                });
            }
            catch { /* user can copy the URL from the title bar */ }
        }
    }
}
