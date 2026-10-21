using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.IO;
using sapaXPrintMaster.Services;
using sapaXPrintCore.Jobs;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printing;
using sapaXPrintCore.Printers;

using Application = System.Windows.Application;

namespace sapaXPrintMaster
{
    public partial class App : Application
    {
        // =========================
        // Native DLL loading
        // =========================
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private NotifyIcon _tray;
        private MainWindow _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var nativeDir = Path.Combine(exeDir, "native", "x64");

            SetDllDirectory(nativeDir);

            Environment.SetEnvironmentVariable(
                "PATH",
                nativeDir + ";" + Environment.GetEnvironmentVariable("PATH"),
                EnvironmentVariableTarget.Process
            );

            ZebraPrinterService.DryRun = false;

            // =========================
            // WORKERS
            // =========================
            // 1. prints PDF jobs that were queued locally
            PrintJobWorker.Start();

            // 2. every N seconds (Administrator tab) claims jobs straight from the SQL database
            DbJobPoller.Start(
                connectionString: () => DbConfig.IsConfigured ? DbConfig.ConnectionString : null,
                computerId: () => AppSettings.AgentId,
                intervalSeconds: () => AppSettings.PollIntervalSeconds,
                printerProvider: () =>
                {
                    // only publish printers that are ticked "Active" in the Printers tab
                    var printers = PrinterDiscoveryService.GetPrinters(checkNetworkReachability: false);
                    PrinterPreferenceService.MergePreferences(printers);
                    return printers.Where(p => p.IsActive).ToList();
                });

            base.OnStartup(e);

            _mainWindow = new MainWindow();
            _mainWindow.Hide();

            InitTray();
        }

        // ======================================================
        // Tray UI
        // ======================================================
        private void InitTray()
        {
            _tray = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application, // replace with .ico later
                Visible = true,
                Text = "sapaX Print Node"
            };

            var menu = new ContextMenuStrip();

            menu.Items.Add("Open", null, (_, __) =>
            {
                ShowMainWindow();
            });

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("Exit", null, (_, __) =>
            {
                _tray.Visible = false;
                _tray.Dispose();
                Shutdown();
            });

            _tray.ContextMenuStrip = menu;

            _tray.DoubleClick += (_, __) => ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
            }

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
