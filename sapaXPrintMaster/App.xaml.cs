using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.IO;
using sapaXPrintMaster.Services;
using sapaXPrintCore.Cloud;
using sapaXPrintCore.Jobs;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printing;

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

            // DEV ONLY
            ServicePointManager.ServerCertificateValidationCallback +=
                (_, _, _, _) => true;

            ZebraPrinterService.DryRun = false;

            // =========================
            // WORKERS
            // =========================
            PrintJobWorker.Start();
            CloudAgentPoller.Start();
            //StartDevCloudPolling();
            base.OnStartup(e);

            _mainWindow = new MainWindow();
            _mainWindow.Hide();

            InitTray();
        }



        // ======================================================
        // DEV CLOUD POLLING (PrintNode-style agent loop)
        // ======================================================
        private void StartDevCloudPolling()
        {
            var cloudUrl = "https://sapaxprintmaster-dnb6edbdb8g4hydn.canadacentral-01.azurewebsites.net";

            var cloudClient = new CloudPrintClient(
                cloudUrl,
                AppSettings.AgentId,
                AppSettings.AgentApiKey
            );

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var job = await cloudClient.FetchNextJobAsync();

                        if (job != null)
                        {
                            PrintJobProcessor.Process(new PrintRequest
                            {
                                PrinterId = job.PrinterId,
                                FileName = job.FileName,
                                FileBase64 = job.FileBase64
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[CLOUD POLL ERROR]");
                        System.Diagnostics.Debug.WriteLine(ex);
                    }

                    await Task.Delay(2000);
                }
            });
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
