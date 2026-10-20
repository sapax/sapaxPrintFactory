using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;
using sapaXPrintCore.Printing;

namespace sapaXPrintCore.Jobs
{
    public static class ZplJobWorker
    {
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;

            new Thread(Loop)
            {
                IsBackground = true,
                Name = "ZplJobWorker"
            }.Start();
        }

        private static void Loop()
        {
            Console.WriteLine("ZPL JOB WORKER STARTED");

            while (true)
            {
                ZplJobQueue.Wait();

                while (true)
                {
                    var job = ZplJobQueue.Dequeue();
                    if (job == null) break;

                    try
                    {
                        var printer = PrinterDiscoveryService.GetPrinterById(job.PrinterId);
                        if (printer == null)
                            throw new Exception("Zebra printer not found");

                        ZebraPrinterService.Print("127.0.0.1", job.Zpl); // Fake Zebra
                        Console.WriteLine("ZPL SENT");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ZPL FAILED");
                        Console.WriteLine(ex.Message);
                    }
                }
            }
        }
    }
}
