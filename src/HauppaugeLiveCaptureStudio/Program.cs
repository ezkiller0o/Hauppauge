using System;
using System.Threading;
using System.Windows.Forms;
using HauppaugeLiveCaptureStudio.Core;
using HauppaugeLiveCaptureStudio.UI;

namespace HauppaugeLiveCaptureStudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Only one copy may run: two copies would fight for the capture device and could start two recordings.
            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\HauppaugeLiveCaptureStudio.SingleInstance", out createdNew))
            {
                AppPaths.Initialize();
                AppSettings settings = AppSettings.Load();
                Loc.SetLanguage(settings.Language);

                if (!createdNew)
                {
                    MessageBox.Show(Loc.T("msg.alreadyRunning"), AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, Loc.IsArabic ? (MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) : (MessageBoxOptions)0);
                    return;
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
                {
                    Logger.Error("Unhandled UI exception", e.Exception);
                    MessageBox.Show(e.Exception.Message + "\n\n" + AppPaths.LogFile, AppPaths.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    Logger.Error("Unhandled exception", e.ExceptionObject as Exception);
                };

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Logger.Info("=== " + AppPaths.AppName + " started ===");
                Application.Run(new MainForm(settings));
                Logger.Info("=== " + AppPaths.AppName + " closed ===");
            }
        }
    }
}
