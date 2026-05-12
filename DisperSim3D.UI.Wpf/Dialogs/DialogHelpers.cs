using System.Drawing;
using System.Windows.Forms;

namespace DisperSim3D.Dialogs
{
    internal static class DialogHelpers
    {
        public static void AddRowWithHelp(TableLayoutPanel table, ref int row,
            string label, Control control, string description)
        {
            var lbl = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 0)
            };
            table.Controls.Add(lbl, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            table.Controls.Add(control, 1, row);
            row++;
            if (!string.IsNullOrEmpty(description))
            {
                var help = new Label
                {
                    Text = description,
                    AutoSize = true,
                    MaximumSize = new Size(2000, 0),
                    ForeColor = SystemColors.GrayText,
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                    Margin = new Padding(0, 0, 0, 6)
                };
                table.SetColumnSpan(help, 2);
                table.Controls.Add(help, 0, row);
                row++;
            }
        }

        public static Label MakeHelpLabel(string description)
        {
            return new Label
            {
                Text = description,
                AutoSize = true,
                MaximumSize = new Size(2000, 0),
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                Margin = new Padding(0, 0, 0, 6)
            };
        }
    }
}
