using System;
using System.Windows.Forms;

namespace DisperSim3D.App
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--gptest")
            {
                GpTest.Run();
                return;
            }

            if (args.Length > 0 && args[0] == "--iogp-selftest")
            {
                try
                {
                    var report = DisperSim3D.Core.IogpTableTests.RunAll();
                    Console.WriteLine(report);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Environment.Exit(1);
                }
                return;
            }

            if (Environment.OSVersion.Version.Major >= 6)
            {
                SetProcessDPIAware();
            }
            DisperSim3D.Core.TempManager.StartupPurge();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
