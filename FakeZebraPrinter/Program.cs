using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace FakeZebraPrinter
{
    class Program
    {
        // ---- Settings -------------------------------------------------
        const string PrinterName = "Fake Zebra";
        const string DriverName = "Generic / Text Only";
        const string HostAddress = "127.0.0.1";
        const int Port = 9100;
        // ---------------------------------------------------------------

        static string PortName
        {
            get { return "IP_" + HostAddress + "_" + Port; }
        }

        static int Main(string[] args)
        {
            bool uninstall = Array.Exists(args,
                a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

            // Creating printers needs admin rights. Relaunch elevated if needed.
            if (!IsAdministrator())
            {
                return RelaunchAsAdmin(args);
            }

            if (uninstall)
            {
                Console.WriteLine("Removing Windows printer '" + PrinterName + "'...");
                RunPowerShell(BuildUninstallScript());
                Console.WriteLine("Done.");
                return 0;
            }

            Console.WriteLine("Checking Windows printer '" + PrinterName + "'...");
            int code = RunPowerShell(BuildInstallScript());
            if (code != 0)
            {
                Console.WriteLine("WARNING: could not create the Windows printer (exit code " + code + ").");
                Console.WriteLine("The listener will still start, but sapaX won't list the printer.\n");
            }
            else
            {
                Console.WriteLine("Printer '" + PrinterName + "' is ready (port " + PortName + ").\n");
            }

            return RunListener();
        }

        // ------------------------------------------------------------------
        // TCP listener
        // ------------------------------------------------------------------
        static int RunListener()
        {
            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Could not listen on port " + Port + ": " + ex.Message);
                Console.WriteLine("Is another copy of the fake printer already running?");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return 1;
            }

            Console.WriteLine("Fake Zebra Printer listening on port " + Port + "...");
            Console.WriteLine("Waiting for ZPL...\n");

            while (true)
            {
                var client = listener.AcceptTcpClient();
                Task.Run(() => Handle(client));
            }
        }

        static void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[4096];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        ms.Write(buffer, 0, read);

                    var data = ms.ToArray();
                    if (data.Length == 0)
                        return; // reachability probe from sapaX, ignore

                    Console.WriteLine("===== ZPL RECEIVED =====");
                    Console.WriteLine(Encoding.ASCII.GetString(data));
                    Console.WriteLine("========================\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Printer installation (runs PowerShell, safe to run repeatedly)
        // ------------------------------------------------------------------
        static string BuildInstallScript()
        {
            return
$@"$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'Stop'
$driver = '{DriverName}'
$port   = '{PortName}'
$name   = '{PrinterName}'

if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) {{
    try {{ Add-PrinterDriver -Name $driver }} catch {{ Write-Host 'Add-PrinterDriver failed, will try printui.' }}
}}

if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) {{
    Add-PrinterPort -Name $port -PrinterHostAddress '{HostAddress}' -PortNumber {Port}
}}

if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) {{
    try {{
        Add-Printer -Name $name -DriverName $driver -PortName $port
    }} catch {{
        Write-Host 'Add-Printer failed, trying printui fallback...'
        rundll32 printui.dll,PrintUIEntry /if /b $name /f ""$env:windir\inf\ntprint.inf"" /r $port /m $driver
        Start-Sleep -Seconds 3
        if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) {{ throw 'Printer could not be created.' }}
    }}
}}";
        }

        static string BuildUninstallScript()
        {
            return
$@"$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'SilentlyContinue'
Remove-Printer -Name '{PrinterName}'
Remove-PrinterPort -Name '{PortName}'";
        }

        static int RunPowerShell(string script)
        {
            // EncodedCommand avoids any quoting problems
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output.Trim());
                    if (!string.IsNullOrWhiteSpace(error) && !error.TrimStart().StartsWith("#< CLIXML"))
                        Console.WriteLine(error.Trim());

                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not run PowerShell: " + ex.Message);
                return -1;
            }
        }

        // ------------------------------------------------------------------
        // Admin helpers
        // ------------------------------------------------------------------
        static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        static int RelaunchAsAdmin(string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    Verb = "runas" // triggers the UAC prompt
                };
                Process.Start(psi);
                return 0;
            }
            catch (Win32Exception)
            {
                Console.WriteLine("Administrator rights are required to create the printer.");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return 1;
            }
        }
    }
}