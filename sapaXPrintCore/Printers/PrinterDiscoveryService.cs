using sapaXPrintCore.Models;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Management;
using System.Net.Sockets;
using System.Printing;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintCore.Printers
{
    public static class PrinterDiscoveryService
    {
        // Timeout for the live TCP reachability check on network printers
        private const int TcpTimeoutMs = 700;

        // Safety net: never block the caller longer than this waiting for all reachability checks
        private const int WaitAllTimeoutMs = TcpTimeoutMs + 1500;

        private sealed class WmiPrinterData
        {
            public bool WorkOffline { get; set; }
            public ushort? PrinterStatus { get; set; }
            public ushort? ExtendedPrinterStatus { get; set; }
            public ushort? DetectedErrorState { get; set; }
            public string? PortName { get; set; }
        }

        private sealed class TcpPortData
        {
            public string Host { get; set; } = "";
            public int Port { get; set; } = 9100;
        }

        public static List<PrinterInfo> GetPrinters(bool checkNetworkReachability = true)
        {
            var printers = new List<PrinterInfo>();

            string defaultPrinter = new PrinterSettings().PrinterName;
            string machineName = Environment.MachineName;

            // One WMI round-trip for all printers (much faster than per-printer queries)
            var wmiMap = GetAllWmiData();
            var tcpPorts = checkNetworkReachability ? GetTcpPorts() : new Dictionary<string, TcpPortData>();

            using var printServer = new LocalPrintServer();
            var queues = printServer.GetPrintQueues();

            // Network reachability checks run in parallel so one dead printer doesn't block the rest
            var reachability = new Dictionary<string, Task<bool>>(StringComparer.OrdinalIgnoreCase);
            if (checkNetworkReachability)
            {
                foreach (var kvp in wmiMap)
                {
                    string? portName = kvp.Value.PortName;
                    if (portName != null && tcpPorts.TryGetValue(portName, out var tcp))
                    {
                        string host = tcp.Host;
                        int port = tcp.Port;

                        // FIX: Task.Run moves the work to the thread pool, so it can never
                        // depend on the caller's (UI) synchronization context.
                        reachability[kvp.Key] = Task.Run(() => IsTcpReachableAsync(host, port));
                    }
                }

                try
                {
                    // FIX: use a timeout so a stuck check can never freeze the app
                    Task.WaitAll(reachability.Values.ToArray(), WaitAllTimeoutMs);
                }
                catch { /* individual results are handled below */ }
            }

            foreach (var queue in queues)
            {
                try
                {
                    queue.Refresh();

                    string printerName = queue.Name;

                    wmiMap.TryGetValue(printerName, out var wmi);

                    bool? reachable = null;
                    if (reachability.TryGetValue(printerName, out var task) &&
                        task.Status == TaskStatus.RanToCompletion)
                    {
                        reachable = task.Result;
                    }

                    printers.Add(new PrinterInfo
                    {
                        IsActive = true,
                        PrinterName = printerName,
                        PrinterId = GeneratePrinterId(machineName, printerName),
                        Status = ResolveStatus(queue, wmi, reachable),
                        IsDefault = printerName.Equals(defaultPrinter, StringComparison.OrdinalIgnoreCase),
                        DriverName = queue.QueueDriver?.Name ?? "Unknown"
                    });
                }
                catch (Exception ex)
                {
                    printers.Add(new PrinterInfo
                    {
                        IsActive = true,
                        PrinterName = queue.Name,
                        PrinterId = GeneratePrinterId(machineName, queue.Name),
                        Status = "Unavailable",
                        IsDefault = queue.Name.Equals(defaultPrinter, StringComparison.OrdinalIgnoreCase),
                        DriverName = "Unknown"
                    });

                    System.Diagnostics.Debug.WriteLine(
                        $"Printer '{queue.Name}' error: {ex.Message}");
                }
            }

            return printers;
        }

        public static PrinterInfo? GetPrinterById(string printerId)
        {
            if (string.IsNullOrWhiteSpace(printerId))
                return null;

            return GetPrinters()
                .FirstOrDefault(p => p.PrinterId == printerId);
        }

        // ------------------------------------------------------------------
        // Status resolution: TCP reachability -> WMI -> spooler queue status
        // ------------------------------------------------------------------
        private static string ResolveStatus(PrintQueue queue, WmiPrinterData? wmi, bool? tcpReachable)
        {
            // 1. Live network check (most accurate for network printers)
            if (tcpReachable == false)
                return "Offline";

            // 2. WMI
            if (wmi != null)
            {
                string? wmiStatus = GetWmiStatus(wmi);
                if (wmiStatus != null)
                    return wmiStatus;
            }

            // 3. Spooler queue status
            string queueStatus = GetQueueStatus(queue);
            if (queueStatus != "Online")
                return queueStatus;

            // Everything looks fine. If WMI says it is actively printing, show that.
            if (wmi?.PrinterStatus == 4) return "Printing";
            if (wmi?.PrinterStatus == 5) return "Warming Up";

            return "Online";
        }

        // Returns null when WMI reports nothing wrong
        private static string? GetWmiStatus(WmiPrinterData wmi)
        {
            if (wmi.WorkOffline)
                return "Offline";

            switch (wmi.DetectedErrorState)
            {
                case 4: return "Out Of Paper";
                case 6: return "No Toner";
                case 7: return "Door Open";
                case 8: return "Paper Jam";
                case 9: return "Offline";
                case 10: return "Service Requested";
                case 11: return "Output Bin Full";
                    // 3 = Low Paper, 5 = Low Toner: warnings only, printer is still usable
            }

            if (wmi.PrinterStatus == 7 || wmi.ExtendedPrinterStatus == 7)
                return "Offline";

            if (wmi.PrinterStatus == 6)
                return "Stopped Printing";

            return null;
        }

        private static string GetQueueStatus(PrintQueue queue)
        {
            if (queue.IsOffline)
                return "Offline";

            var status = queue.QueueStatus;

            if (status.HasFlag(PrintQueueStatus.Offline)) return "Offline";
            if (status.HasFlag(PrintQueueStatus.PaperJam)) return "Paper Jam";
            if (status.HasFlag(PrintQueueStatus.PaperOut)) return "Out Of Paper";
            if (status.HasFlag(PrintQueueStatus.DoorOpen)) return "Door Open";
            if (status.HasFlag(PrintQueueStatus.NoToner)) return "No Toner";
            if (status.HasFlag(PrintQueueStatus.OutOfMemory)) return "Out Of Memory";
            if (status.HasFlag(PrintQueueStatus.UserIntervention)) return "User Action Required";
            if (status.HasFlag(PrintQueueStatus.Error)) return "Error";
            if (status.HasFlag(PrintQueueStatus.Paused)) return "Paused";
            if (status.HasFlag(PrintQueueStatus.Busy)) return "Busy";
            if (status.HasFlag(PrintQueueStatus.Printing)) return "Printing";
            if (status.HasFlag(PrintQueueStatus.IOActive)) return "Processing";
            if (status.HasFlag(PrintQueueStatus.Initializing)) return "Initializing";
            if (status.HasFlag(PrintQueueStatus.WarmingUp)) return "Warming Up";
            if (status.HasFlag(PrintQueueStatus.Waiting)) return "Waiting";
            if (status.HasFlag(PrintQueueStatus.PendingDeletion)) return "Pending Deletion";

            return "Online";
        }

        // ------------------------------------------------------------------
        // WMI helpers
        // ------------------------------------------------------------------
        private static Dictionary<string, WmiPrinterData> GetAllWmiData()
        {
            var map = new Dictionary<string, WmiPrinterData>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, PortName, WorkOffline, PrinterStatus, ExtendedPrinterStatus, DetectedErrorState FROM Win32_Printer");

                foreach (ManagementObject p in searcher.Get())
                {
                    using (p)
                    {
                        string? name = p["Name"]?.ToString();
                        if (string.IsNullOrEmpty(name))
                            continue;

                        map[name] = new WmiPrinterData
                        {
                            WorkOffline = p["WorkOffline"] is bool b && b,
                            PrinterStatus = ToUShort(p["PrinterStatus"]),
                            ExtendedPrinterStatus = ToUShort(p["ExtendedPrinterStatus"]),
                            DetectedErrorState = ToUShort(p["DetectedErrorState"]),
                            PortName = p["PortName"]?.ToString()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WMI printer query failed: {ex.Message}");
            }

            return map;
        }

        private static Dictionary<string, TcpPortData> GetTcpPorts()
        {
            var map = new Dictionary<string, TcpPortData>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, HostAddress, PortNumber, Protocol FROM Win32_TCPIPPrinterPort");

                foreach (ManagementObject p in searcher.Get())
                {
                    using (p)
                    {
                        string? name = p["Name"]?.ToString();
                        string? host = p["HostAddress"]?.ToString();
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(host))
                            continue;

                        // Protocol 1 = RAW, 2 = LPR. Only RAW has a meaningful port to probe.
                        // LPR uses 515; raw printing normally uses 9100.
                        int port = 9100;
                        if (p["PortNumber"] != null)
                        {
                            try { port = Convert.ToInt32(p["PortNumber"]); }
                            catch { port = 9100; }
                        }

                        if (p["Protocol"] != null && Convert.ToInt32(p["Protocol"]) == 2)
                            port = 515;

                        map[name] = new TcpPortData { Host = host, Port = port };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WMI TCP port query failed: {ex.Message}");
            }

            return map;
        }

        private static ushort? ToUShort(object? value)
        {
            if (value == null) return null;
            try { return Convert.ToUInt16(value); }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // TCP reachability
        // ------------------------------------------------------------------
        private static async Task<bool> IsTcpReachableAsync(string host, int port)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);

                // FIX: ConfigureAwait(false) so continuations never need the UI thread
                var completed = await Task.WhenAny(connectTask, Task.Delay(TcpTimeoutMs))
                                          .ConfigureAwait(false);

                if (completed != connectTask)
                {
                    // Timed out: observe the pending connect so it doesn't raise an unobserved exception
                    _ = connectTask.ContinueWith(
                        t => { var _ = t.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted);
                    return false;
                }

                await connectTask.ConfigureAwait(false); // surface connection errors
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        private static string GeneratePrinterId(string machine, string printer)
        {
            using SHA1 sha1 = SHA1.Create();

            byte[] hash = sha1.ComputeHash(
                Encoding.UTF8.GetBytes($"{machine}|{printer}")
            );

            uint id = BitConverter.ToUInt32(hash, 0);

            return (id % 100_000_000).ToString("D8");
        }

    }
}