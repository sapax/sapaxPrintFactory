using sapaXPrintCore.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace sapaXPrintCore.Printers
{
    /// <summary>
    /// Watches printers in the background and raises PrintersChanged
    /// whenever a printer is added, removed, or its status changes.
    /// Add this file to sapaXPrintCore next to PrinterDiscoveryService.cs.
    /// </summary>
    public sealed class PrinterStatusMonitor : IDisposable
    {
        private readonly TimeSpan _interval;
        private readonly bool _checkNetwork;
        private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private ManagementEventWatcher? _watcher;
        private Dictionary<string, string> _lastSnapshot = new();

        /// <summary>
        /// Raised on a BACKGROUND thread. In WPF, marshal to the UI thread
        /// (Dispatcher.Invoke) before touching UI-bound collections.
        /// </summary>
        public event Action<List<PrinterInfo>>? PrintersChanged;

        public PrinterStatusMonitor(TimeSpan? interval = null, bool checkNetworkReachability = false)
        {
            _interval = interval ?? TimeSpan.FromSeconds(3);
            _checkNetwork = checkNetworkReachability;
        }

        public void Start()
        {
            if (_loop != null) return;

            _cts = new CancellationTokenSource();
            StartWmiWatcher();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _wake.Release();

            try { _watcher?.Stop(); } catch { }
            _watcher?.Dispose();
            _watcher = null;

            try { _loop?.Wait(2000); } catch { }
            _loop = null;

            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose() => Stop();

        /// <summary>Force an immediate check (e.g. from a Refresh button).</summary>
        public void RefreshNow() => _wake.Release();

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var printers = PrinterDiscoveryService.GetPrinters(_checkNetwork);
                    var snapshot = BuildSnapshot(printers);

                    if (!SnapshotsEqual(_lastSnapshot, snapshot))
                    {
                        _lastSnapshot = snapshot;
                        PrintersChanged?.Invoke(printers);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Printer monitor error: {ex.Message}");
                }

                try
                {
                    // Wait for the next tick OR an early wake-up from WMI / RefreshNow()
                    await _wake.WaitAsync(_interval, ct);
                    await Task.Delay(300, ct); // debounce bursts of WMI events
                    while (_wake.CurrentCount > 0) _wake.Wait(0); // drain extra signals
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // WMI fires when a printer's properties change (WorkOffline, status, errors...)
        private void StartWmiWatcher()
        {
            try
            {
                var query = new WqlEventQuery(
                    "__InstanceOperationEvent",
                    TimeSpan.FromSeconds(2),
                    "TargetInstance ISA 'Win32_Printer'");

                _watcher = new ManagementEventWatcher(query);
                _watcher.EventArrived += (_, _) => _wake.Release();
                _watcher.Start();
            }
            catch (Exception ex)
            {
                // Not fatal: polling still works without WMI events
                System.Diagnostics.Debug.WriteLine($"WMI watcher failed: {ex.Message}");
            }
        }

        private static Dictionary<string, string> BuildSnapshot(List<PrinterInfo> printers) =>
            printers.ToDictionary(
                p => p.PrinterName,
                p => $"{p.Status}|{p.IsDefault}",
                StringComparer.OrdinalIgnoreCase);

        private static bool SnapshotsEqual(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count) return false;

            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var other) || other != kvp.Value)
                    return false;
            }

            return true;
        }
    }
}