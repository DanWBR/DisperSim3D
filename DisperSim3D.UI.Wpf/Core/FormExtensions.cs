using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Provides extension methods and helpers for applying consistent DPI scaling
    /// and theming to Windows Forms controls.
    /// </summary>
    public static class FormExtensions
    {
        private static Icon _appIcon;
        private static bool _appIconLoaded;

        /// <summary>
        /// Gets the application icon loaded from <c>Resources/Icons/Air.ico</c> relative to the executing assembly.
        /// Returns <c>null</c> if the icon file is not found. The icon is loaded once and cached.
        /// </summary>
        public static Icon AppIcon
        {
            get
            {
                if (!_appIconLoaded)
                {
                    _appIconLoaded = true;
                    string dir = Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string icoPath = Path.Combine(dir, "Resources", "Icons", "Air.ico");
                    if (File.Exists(icoPath))
                        _appIcon = new Icon(icoPath);
                }
                return _appIcon;
            }
        }

        /// <summary>
        /// Applies DPI-aware scaling to the specified <see cref="Form"/> and all of its descendant controls.
        /// Sets the form's auto-scale mode to DPI, assigns the application icon, updates fonts to the
        /// system message-box font family, and scales <see cref="DataGridView"/> rows and <see cref="Button"/> images
        /// when the DPI scale factor exceeds 100 %.
        /// </summary>
        /// <param name="form">The <see cref="Form"/> to scale.</param>
        public static void ApplyDpiScaling(this Form form)
        {
            form.AutoScaleMode = AutoScaleMode.Dpi;

            if (AppIcon != null)
                form.Icon = AppIcon;

            float dpiScale = form.DeviceDpi / 96f;

            foreach (Control control in GetAllChildren(form))
            {
                control.Font = new Font(
                    SystemFonts.MessageBoxFont.FontFamily,
                    control.Font.Size,
                    control.Font.Style);

                if (dpiScale > 1.0f)
                {
                    if (control is DataGridView dgv)
                    {
                        dgv.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
                        dgv.AllowUserToResizeRows = false;
                        int scaledRowHeight = (int)(23 * dpiScale);
                        dgv.RowTemplate.Height = scaledRowHeight;
                        dgv.ColumnHeadersHeight = scaledRowHeight;
                        foreach (DataGridViewRow r in dgv.Rows)
                            r.Height = scaledRowHeight;
                    }
                    else if (control is Button btn && btn.Image != null)
                    {
                        btn.Image = new Bitmap(btn.Image,
                            new Size((int)(16 * dpiScale), (int)(16 * dpiScale)));
                    }
                }
            }
        }

        /// <summary>
        /// Applies DPI-aware scaling to a <see cref="ToolStrip"/>, resizing its image scaling size,
        /// individual item dimensions, and overall strip height. No changes are made when
        /// <paramref name="dpiScale"/> is 1.0 or less.
        /// </summary>
        /// <param name="strip">The <see cref="ToolStrip"/> to scale.</param>
        /// <param name="dpiScale">The DPI scale factor (e.g., 1.5 for 144 DPI on a 96 DPI baseline).</param>
        public static void ApplyDpiScaling(this ToolStrip strip, float dpiScale)
        {
            if (dpiScale <= 1.0f) return;

            strip.AutoSize = false;

            var imgSize = new Size((int)(20 * dpiScale), (int)(20 * dpiScale));
            strip.ImageScalingSize = imgSize;

            foreach (ToolStripItem item in strip.Items)
            {
                if (item is ToolStripButton btn)
                {
                    btn.Size = new Size(imgSize.Width, imgSize.Height);
                }
                else if (item is ToolStripComboBox cmb)
                {
                    cmb.Size = new Size((int)(cmb.Width * dpiScale), cmb.Height);
                }
                else if (item is ToolStripTextBox txt)
                {
                    txt.Size = new Size((int)(txt.Width * dpiScale), txt.Height);
                }
            }

            strip.Height = (int)(28 * dpiScale);
            strip.AutoSize = true;
            strip.Invalidate();
        }

        /// <summary>
        /// Applies DPI-aware scaling to a <see cref="MenuStrip"/> by adjusting its image scaling size.
        /// No changes are made when <paramref name="dpiScale"/> is 1.0 or less.
        /// </summary>
        /// <param name="menu">The <see cref="MenuStrip"/> to scale.</param>
        /// <param name="dpiScale">The DPI scale factor (e.g., 1.5 for 144 DPI on a 96 DPI baseline).</param>
        public static void ApplyDpiScaling(this MenuStrip menu, float dpiScale)
        {
            if (dpiScale <= 1.0f) return;

            menu.ImageScalingSize = new Size((int)(16 * dpiScale), (int)(16 * dpiScale));
            menu.Invalidate();
        }

        private static IEnumerable<Control> GetAllChildren(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control grandchild in GetAllChildren(child))
                    yield return grandchild;
            }
        }
    }
}
