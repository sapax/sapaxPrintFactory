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
using sapaXPrintCore.Settings;
using System.Threading;

using Application = System.Windows.Application;

namespace sapaXPrintMaster
{
    // Desktop app = admin screen + tray icon + Print tab (queues jobs in the database).
    // Printing is done by sapaXPrintService, which starts with Windows.
    public partial class App : Application
    {
        private static Mutex? _singleInstance;
        private NotifyIcon? _tray;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Only one desktop copy per Windows session
            _singleInstance = new Mutex(true, "sapaXPrintMaster.Desktop", out bool first);
            if (!first)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // Start hidden: the window is created (so the printer monitor runs) but not shown
            _mainWindow = new MainWindow();

            InitTray();
        }

        private void InitTray()
        {
            _tray = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,   // replace with your .ico later
                Visible = true,
                Text = "sapaX Print Master"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (_, __) => ShowMainWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, __) => ExitApp());

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, __) => ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            _mainWindow ??= new MainWindow();

            if (!_mainWindow.IsVisible)
                _mainWindow.Show();

            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
        }

        private void ExitApp()
        {
            _tray?.Dispose();
            _tray = null;
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mainWindow?.StopPrinterMonitor();
            _tray?.Dispose();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
