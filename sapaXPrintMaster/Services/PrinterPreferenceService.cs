using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using sapaXPrintCore.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace sapaXPrintMaster.Services
{
    public static class PrinterPreferenceService
    {
        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sapaXPrintMaster");

        private static readonly string FilePath = Path.Combine(Folder, "printers.json");

        public static List<PrinterInfo> Load()
        {
            if (!File.Exists(FilePath))
                return new List<PrinterInfo>();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<PrinterInfo>>(json) ?? new();
        }

        public static void Save(IEnumerable<PrinterInfo> printers)
        {
            if (!Directory.Exists(Folder))
                Directory.CreateDirectory(Folder);

            string json = JsonSerializer.Serialize(printers, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(FilePath, json);
        }

        public static void MergePreferences(List<PrinterInfo> discovered)
        {
            var stored = Load();

            foreach (var printer in discovered)
            {
                var saved = stored.FirstOrDefault(p => p.PrinterId == printer.PrinterId);
                if (saved != null)
                {
                    printer.IsActive = saved.IsActive;
                    printer.IsDefault = saved.IsDefault;
                }
            }
        }
    }
}
