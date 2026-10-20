using Microsoft.Win32;
using PdfSharpCore.Pdf.IO;
using sapaXPrintCore.Jobs;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;
using sapaXPrintMaster.Services;
using sapaXPrintMaster.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Xps.Packaging;


namespace sapaXPrintMaster
{
    // The Administrator tab logic lives in MainWindow.Admin.cs (same partial class).
    public partial class MainWindow : Window
    {
        private LocalHttpServer _server;
        private string pdfPath = string.Empty;

        // Live printer status monitor
        private PrinterStatusMonitor? _printerMonitor;

        public MainWindow()
        {
            InitializeComponent();
            LoadPrinters();
            LoadRealPrinters();
            StartPrinterMonitor();   // live status updates
            // Job queue
            DataContext = new JobQueueViewModel();
            // Start local HTTP server (PrintNode-like agent)
            //_server = new LocalHttpServer();
            //_server.Start();

            /*PrintJobWorker.Start();
            CloudPrintPoller.Start();*/

            CheckActivation();
            CheckFirstRunAsync();    // defined in MainWindow.Admin.cs
        }

        private void LoadPrinters()
        {
            cmbPrinters.ItemsSource = PrinterSettings.InstalledPrinters.Cast<string>();
            if (cmbPrinters.Items.Count > 0)
                cmbPrinters.SelectedIndex = 0;
        }

        private void SelectPdf_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf"
            };

            if (dialog.ShowDialog() == true)
            {
                pdfPath = dialog.FileName;
                FileInfo fileInfo = new FileInfo(pdfPath);

                // Show file info panel, hide "no file" panel
                pnlNoFile.Visibility = Visibility.Collapsed;
                pnlFileInfo.Visibility = Visibility.Visible;

                // Populate file information
                txtFileName.Text = fileInfo.Name;
                txtFilePath.Text = pdfPath;
                txtFileSize.Text = $"{fileInfo.Length / 1024:N0} KB";

                // Enable print button
                btnPrint.IsEnabled = true;

                // Update window title
                Title = $"sapaX Print Master - {fileInfo.Name}";
            }
        }

        private void PrintPdf_Click(object sender, RoutedEventArgs e)
        {
            // PDF selected
            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                MessageBox.Show("Please select a PDF file.", "Missing PDF");
                return;
            }

            // File exists
            if (!File.Exists(pdfPath))
            {
                MessageBox.Show("PDF file not found.", "Error");
                return;
            }

            // Printer selected
            if (cmbPrinters.SelectedItem == null)
            {
                MessageBox.Show("Please select a printer.", "Missing Printer");
                return;
            }

            string printerName = cmbPrinters.SelectedItem.ToString()!;

            // Printer exists
            if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(printerName))
            {
                MessageBox.Show("Selected printer is not available.", "Printer Error");
                return;
            }

            try
            {
                string xpsPath = ConvertPdfToXps(pdfPath);
                PrintXpsSilently(xpsPath, printerName);

                MessageBox.Show("PDF printed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Optional cleanup
                File.Delete(xpsPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Printing failed:\n{ex.Message}", "Error");
            }
        }

        // -------------------------------
        // PDF → XPS (Silent)
        // -------------------------------
        private string ConvertPdfToXps(string pdfFile)
        {
            string xpsPath = Path.ChangeExtension(pdfFile, ".xps");

            using var pdf = PdfReader.Open(pdfFile, PdfDocumentOpenMode.ReadOnly);
            using var xps = new XpsDocument(xpsPath, FileAccess.Write);

            var fixedDoc = new FixedDocument();
            var writer = XpsDocument.CreateXpsDocumentWriter(xps);

            foreach (var _ in pdf.Pages)
            {
                var fixedPage = new FixedPage
                {
                    Width = 816,
                    Height = 1056
                };

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(fixedPage);
                fixedDoc.Pages.Add(pageContent);
            }

            writer.Write(fixedDoc);
            return xpsPath;
        }


        // -------------------------------
        // Silent Print via PrintQueue
        // -------------------------------
        private void PrintXpsSilently(string xpsPath, string printerName)
        {
            PrintServer server = new PrintServer();
            PrintQueue queue = server.GetPrintQueue(printerName);

            using XpsDocument xpsDoc = new XpsDocument(xpsPath, FileAccess.Read);
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);
            writer.Write(xpsDoc.GetFixedDocumentSequence());
        }

        // -------------------------------
        // Load Real Printers
        // -------------------------------
        private void LoadRealPrinters()
        {
            var printers = PrinterDiscoveryService.GetPrinters();

            // Merge saved preferences
            PrinterPreferenceService.MergePreferences(printers);

            dgPrinters.ItemsSource = new ObservableCollection<PrinterInfo>(printers);
        }

        // -------------------------------
        // Refresh Printers (full reload, resets to saved preferences)
        // -------------------------------
        private void RefreshPrinters_Click(object sender, RoutedEventArgs e)
        {
            LoadRealPrinters();
        }

        // -------------------------------
        // Live printer status
        // -------------------------------
        private void StartPrinterMonitor()
        {
            _printerMonitor = new PrinterStatusMonitor(TimeSpan.FromSeconds(3));
            _printerMonitor.PrintersChanged += OnPrintersChanged;
            _printerMonitor.Start();
        }

        private void OnPrintersChanged(List<PrinterInfo> latest)
        {
            // Runs on a background thread, so hop to the UI thread
            Dispatcher.BeginInvoke(() =>
            {
                if (dgPrinters.ItemsSource is not ObservableCollection<PrinterInfo> current)
                    return;

                // Finish any cell/row the user is editing so Refresh() doesn't throw
                dgPrinters.CommitEdit(DataGridEditingUnit.Row, true);

                // 1. Remove printers that no longer exist
                foreach (var gone in current
                             .Where(c => !latest.Any(l => l.PrinterId == c.PrinterId))
                             .ToList())
                {
                    current.Remove(gone);
                }

                // 2. Update status on existing rows, collect brand-new printers
                var added = new List<PrinterInfo>();
                foreach (var l in latest)
                {
                    var existing = current.FirstOrDefault(c => c.PrinterId == l.PrinterId);
                    if (existing != null)
                        existing.Status = l.Status;   // keeps the user's IsDefault / IsActive edits
                    else
                        added.Add(l);
                }

                // 3. New printers get their saved preferences, then join the grid
                if (added.Count > 0)
                {
                    PrinterPreferenceService.MergePreferences(added);
                    foreach (var p in added)
                        current.Add(p);
                }

                dgPrinters.Items.Refresh();   // redraws Status even if PrinterInfo has no INotifyPropertyChanged
            });
        }

        // Call this where the app really exits (tray "Exit" handler or App.OnExit)
        public void StopPrinterMonitor()
        {
            _printerMonitor?.Dispose();
            _printerMonitor = null;
        }

        // -------------------------------
        // Change printer Default and Active modes
        // -------------------------------
        private void dgPrinters_CurrentCellChanged(object sender, EventArgs e)
        {
            if (dgPrinters.ItemsSource is ObservableCollection<PrinterInfo> printers)
            {
                var selectedDefault = printers.FirstOrDefault(p => p.IsDefault);
                if (selectedDefault != null)
                {
                    foreach (var p in printers)
                    {
                        if (p != selectedDefault)
                            p.IsDefault = false;
                    }
                }

                PrinterPreferenceService.Save(printers);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide(); // minimize to tray (monitor keeps running in the background)
        }

        // =======================================================
        // ACTIVATION SYSTEM
        // Blocks the whole window with pnlActivationOverlay until
        // a valid activation key has been entered once on this machine.
        // =======================================================

        private void CheckActivation()
        {
            // First run: the database isn't configured yet, so the admin must set it up
            // (Administrator tab) before an activation key can be checked.
            if (!DbConfig.IsConfigured)
                return;

            if (!ActivationService.IsActivatedLocally())
            {
                pnlActivationOverlay.Visibility = Visibility.Visible;
            }
        }

        private void txtActivationKey_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ActivateNow();
        }

        private void Activate_Click(object sender, RoutedEventArgs e) => ActivateNow();

        private async void ActivateNow()
        {
            btnActivate.IsEnabled = false;
            txtActivationStatus.Visibility = Visibility.Visible;
            txtActivationStatus.Foreground = Brushes.Gray;
            txtActivationStatus.Text = "Checking activation key...";

            var (result, message) = await ActivationService.ValidateAndActivateAsync(txtActivationKey.Text);

            if (result == ActivationResult.Valid)
            {
                txtActivationStatus.Foreground = Brushes.SeaGreen;
                txtActivationStatus.Text = "✔ Activated. Starting application...";
                await Task.Delay(500);
                pnlActivationOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                txtActivationStatus.Foreground = Brushes.Firebrick;
                txtActivationStatus.Text = $"✘ {message}";
            }

            btnActivate.IsEnabled = true;
        }

    }
}