using DisperSim3D.Controls;
using System.Windows.Forms;

namespace DisperSim3D.App
{
    public partial class MainForm : Form
    {
        private Scene3DEditorPanel _panel;

        public MainForm()
        {
            InitializeComponent();

            this.Text = "DisperSim 3D - Test App";
            this.Size = new System.Drawing.Size(1280, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;

            _panel = new Scene3DEditorPanel { Dock = DockStyle.Fill };
            this.Controls.Add(_panel);

            this.KeyPreview = true;
            this.KeyDown += (s, e) => _panel.HandleKeyDown(e.KeyCode, e.Control);

            this.FormClosing += MainForm_FormClosing;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                var result = MessageBox.Show(this,
                    "Do you want to close the application?\n\nAny unsaved changes will be lost.",
                    "Confirm Exit",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (result == DialogResult.No)
                    e.Cancel = true;
            }
        }
    }
}
