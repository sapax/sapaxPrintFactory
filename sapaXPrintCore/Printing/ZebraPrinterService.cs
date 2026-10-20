using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;
using System.Runtime.InteropServices;

namespace sapaXPrintCore.Printing
{
    public sealed class ZplSendResult
    {
        public string? PrinterName { get; init; }
        public string? PrinterStatus { get; init; }
        public string DocumentName { get; init; } = "";
        public int? SpoolerJobId { get; init; }
    }

    public static class ZebraPrinterService
    {
        public static bool DryRun = false;

        /// <summary>
        /// Print ZPL using PrinterId (PrintNode style). Kept for compatibility.
        /// NOTE: returning only means "accepted by the Windows spooler". Use Send() +
        /// SpoolerJobMonitor.Confirm() when you need to know it really printed.
        /// </summary>
        public static string? Print(string printerId, string zpl)   // returns the Windows printer name
        {
            return Send(printerId, zpl, "ZPL").PrinterName;
        }

        /// <summary>
        /// Checks the printer is ready, sends the ZPL as a RAW job and returns the spooler job info.
        /// Throws (with a readable message) if the printer is missing / offline / rejects the data.
        /// </summary>
        public static ZplSendResult Send(string printerId, string zpl, string label, Guid? jobId = null)
        {
            string documentName = SpoolerJobMonitor.MakeDocumentName(jobId ?? Guid.NewGuid(), label);

            if (DryRun)
            {
                Console.WriteLine("===== ZPL DRY RUN =====");
                Console.WriteLine(zpl);
                return new ZplSendResult { PrinterName = null, DocumentName = documentName };
            }

            // Resolve printer from PrinterId (also gives us its CURRENT status)
            var printer = PrinterDiscoveryService.GetPrinterById(printerId);

            if (printer == null)
                throw new Exception($"Printer not found for PrinterId: {printerId}");

            Console.WriteLine("=== ZPL PRINT ===");
            Console.WriteLine($"PrinterId   : {printer.PrinterId}");
            Console.WriteLine($"PrinterName : {printer.PrinterName}");
            Console.WriteLine($"Status      : {printer.Status}");

            // Do not even spool the job when we already know the printer can't print
            if (SpoolerJobMonitor.IsBlocking(printer.Status))
                throw new Exception($"Printer '{printer.PrinterName}' is {printer.Status}. Job was not sent.");

            int spoolerJobId = RawPrinterHelper.SendStringToPrinter(printer.PrinterName, zpl, documentName);

            return new ZplSendResult
            {
                PrinterName = printer.PrinterName,
                PrinterStatus = printer.Status,
                DocumentName = documentName,
                SpoolerJobId = spoolerJobId
            };
        }
    }

    internal static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDocName = "";

            [MarshalAs(UnmanagedType.LPStr)]
            public string? pOutputFile;

            [MarshalAs(UnmanagedType.LPStr)]
            public string pDataType = "RAW";
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true)]
        private static extern bool OpenPrinter(
            string pPrinterName,
            out IntPtr phPrinter,
            IntPtr pDefault
        );

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        // Returns the spooler JOB ID (0 on failure)
        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true)]
        private static extern int StartDocPrinter(
            IntPtr hPrinter,
            int level,
            [In] DOCINFOA di
        );

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool AbortPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true)]
        private static extern bool WritePrinter(
            IntPtr hPrinter,
            byte[] pBytes,
            int dwCount,
            out int dwWritten
        );

        private static Exception Win32Failure(string api, string printerName) =>
            new Exception($"{api} failed for printer '{printerName}': {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

        /// <summary>Sends a RAW job and returns the Windows spooler job id. Throws on any failure.</summary>
        public static int SendStringToPrinter(string printerName, string zpl, string documentName)
        {
            if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
                throw Win32Failure("OpenPrinter", printerName);

            bool finished = false;
            try
            {
                var docInfo = new DOCINFOA
                {
                    pDocName = documentName,
                    pDataType = "RAW"
                };

                int spoolerJobId = StartDocPrinter(hPrinter, 1, docInfo);
                if (spoolerJobId <= 0)
                    throw Win32Failure("StartDocPrinter", printerName);

                try
                {
                    if (!StartPagePrinter(hPrinter))
                        throw Win32Failure("StartPagePrinter", printerName);

                    byte[] bytes = Encoding.ASCII.GetBytes(zpl);

                    if (!WritePrinter(hPrinter, bytes, bytes.Length, out int written))
                        throw Win32Failure("WritePrinter", printerName);

                    if (written != bytes.Length)
                        throw new Exception($"Only {written} of {bytes.Length} bytes were written to printer '{printerName}'.");

                    if (!EndPagePrinter(hPrinter))
                        throw Win32Failure("EndPagePrinter", printerName);

                    if (!EndDocPrinter(hPrinter))
                        throw Win32Failure("EndDocPrinter", printerName);

                    finished = true;
                    return spoolerJobId;
                }
                finally
                {
                    // Something went wrong half way: drop the partial job instead of leaving it in the queue
                    if (!finished)
                        AbortPrinter(hPrinter);
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }
    }
}