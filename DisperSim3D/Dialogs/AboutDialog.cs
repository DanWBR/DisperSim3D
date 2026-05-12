using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Standard "About" box. Pulls product / version / copyright from the assembly
    /// attributes so we don't drift from <c>DisperSim3D.csproj</c>'s
    /// <c>&lt;Title&gt;</c> / <c>&lt;Version&gt;</c> / <c>&lt;Copyright&gt;</c>.
    /// Layout uses TableLayoutPanel only (project convention) with Cancel-left /
    /// OK-right button order (memory: feedback_winforms_button_order).
    /// </summary>
    public class AboutDialog : Form
    {
        public AboutDialog()
        {
            Text = "About DisperSim 3D";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(520, 0);
            BuildUI();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            var asm = Assembly.GetExecutingAssembly();
            string title = ReadAttr<AssemblyTitleAttribute>(asm)?.Title ?? "DisperSim 3D";
            string version = asm.GetName().Version?.ToString(3) ?? "1.0.0";
            string description = ReadAttr<AssemblyDescriptionAttribute>(asm)?.Description
                ?? "Open Source 3D Gas Dispersion Analysis Library";
            string copyright = ReadAttr<AssemblyCopyrightAttribute>(asm)?.Copyright
                ?? "Copyright © Daniel Wagner Oliveira de Medeiros";
            string company = ReadAttr<AssemblyCompanyAttribute>(asm)?.Company
                ?? "Daniel Wagner Oliveira de Medeiros";

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding((int)(14 * dpi)),
                ColumnCount = 2,
                RowCount = 7,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 7; i++) outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Product icon (left column, spans rows 0..2). Air.ico is a Content file
            // (CopyToOutputDirectory in DisperSim3D.csproj), not an EmbeddedResource,
            // so we load it from disk next to the running assembly.
            var iconBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Width = (int)(64 * dpi),
                Height = (int)(64 * dpi),
                Margin = new Padding(0, 0, (int)(14 * dpi), 0)
            };
            try
            {
                string baseDir = System.IO.Path.GetDirectoryName(asm.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
                string icoPath = System.IO.Path.Combine(baseDir, "Resources", "Icons", "Air.ico");
                if (System.IO.File.Exists(icoPath))
                {
                    using (var icon = new Icon(icoPath, 64, 64))
                        iconBox.Image = icon.ToBitmap();
                }
            }
            catch { }
            outer.Controls.Add(iconBox, 0, 0);
            outer.SetRowSpan(iconBox, 3);

            // Title (large, bold).
            var lblTitle = new Label
            {
                Text = title + "  " + version,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 14f, FontStyle.Bold)
            };
            outer.Controls.Add(lblTitle, 1, 0);

            // Description.
            var lblDesc = new Label
            {
                Text = description,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, (int)(2 * dpi), 0, (int)(8 * dpi))
            };
            outer.Controls.Add(lblDesc, 1, 1);

            // Copyright.
            var lblCopyright = new Label
            {
                Text = copyright,
                AutoSize = true,
                Padding = new Padding(0, 0, 0, (int)(2 * dpi))
            };
            outer.Controls.Add(lblCopyright, 1, 2);

            // Spacer / horizontal rule.
            var rule = new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                Height = 2,
                Dock = DockStyle.Top,
                Margin = new Padding(0, (int)(6 * dpi), 0, (int)(6 * dpi))
            };
            outer.SetColumnSpan(rule, 2);
            outer.Controls.Add(rule, 0, 3);

            // Tech stack box — useful for support.
            var techBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                Dock = DockStyle.Fill,
                Height = (int)(96 * dpi),
                Text = BuildTechSummary()
            };
            outer.SetColumnSpan(techBox, 2);
            outer.Controls.Add(techBox, 0, 4);

            // Link row (GitHub + email).
            var linkRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Padding = new Padding(0, (int)(6 * dpi), 0, 0)
            };
            linkRow.Controls.Add(new LinkLabel
            {
                Text = "Project on GitHub",
                AutoSize = true,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Tag = "https://github.com/DanWBR/dispersim3d"
            });
            linkRow.Controls.Add(new Label { AutoSize = true, Text = "    " });
            linkRow.Controls.Add(new LinkLabel
            {
                Text = "DWSIM",
                AutoSize = true,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Tag = "https://dwsim.org"
            });
            linkRow.Controls.Add(new Label { AutoSize = true, Text = "    " });
            linkRow.Controls.Add(new LinkLabel
            {
                Text = "FluidX3D",
                AutoSize = true,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Tag = "https://github.com/ProjectPhysX/FluidX3D"
            });
            foreach (Control c in linkRow.Controls)
                if (c is LinkLabel ll)
                    ll.LinkClicked += (s, e) => OpenUrl((string)((LinkLabel)s).Tag);
            outer.SetColumnSpan(linkRow, 2);
            outer.Controls.Add(linkRow, 0, 5);

            // Button row — Cancel left, OK right (project convention).
            var btns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(0, (int)(10 * dpi), 0, 0)
            };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var btnCopy = new Button { Text = "Copy details", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
            btnCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(string.Format("{0} {1}\r\n{2}\r\n\r\n{3}",
                        title, version, copyright, BuildTechSummary()));
                }
                catch { }
            };
            var btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                Padding = new Padding(16, 2, 16, 2)
            };
            btns.Controls.Add(btnCopy, 0, 0);
            btns.Controls.Add(new Label(), 1, 0);
            btns.Controls.Add(btnOK, 2, 0);
            AcceptButton = btnOK;
            outer.SetColumnSpan(btns, 2);
            outer.Controls.Add(btns, 0, 6);

            Controls.Add(outer);
        }

        private static string BuildTechSummary()
        {
            return
                "Runtime: .NET " + Environment.Version + " (" + (Environment.Is64BitProcess ? "x64" : "x86") + ")\r\n" +
                "OS: " + Environment.OSVersion.VersionString + "\r\n" +
                "DPI: " + (Screen.PrimaryScreen?.Bounds.Size.ToString() ?? "?") + "\r\n" +
                "\r\n" +
                "Thermodynamics: DWSIM (FluentAPI)\r\n" +
                "CFD wind / dispersion: OpenFOAM, FluidX3D (OpenCL GPU LBM)\r\n" +
                "Plume tracer: built-in semi-Lagrangian advection–diffusion\r\n" +
                "3D rendering: WPF Viewport3D + HelixToolkit\r\n" +
                "Property grid: HandyControl";
        }

        private static T ReadAttr<T>(Assembly asm) where T : Attribute
        {
            try { return (T)Attribute.GetCustomAttribute(asm, typeof(T)); }
            catch { return null; }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
