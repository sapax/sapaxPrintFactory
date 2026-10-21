using Microsoft.Win32;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;
using sapaXPrintMaster.Services;
using sapaXPrintMaster.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace sapaXPrintMaster
{
    // Partial class.
    // Administrator tab and sign-in logic: MainWindow.Admin.cs
    public partial class MainWindow : Window
    {
        private string _pdfPath = string.Empty;
        private PrinterStatusMonitor? _printerMonitor;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new JobQueueViewModel();

            LoadRealPrinters();
            StartPrinterMonitor();

            CheckFirstRunAsync();    // MainWindow.Admin.cs
        }

        // =======================================================
        // PRINT TAB: send a PDF to dbo.PrintJobs (Source = Manual)
        // =======================================================

        private void SelectPdf_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf"
            };

            if (dialog.ShowDialog() == true)
            {
                _pdfPath = dialog.FileName;
                var fileInfo = new FileInfo(_pdfPath);

                pnlNoFile.Visibility = Visibility.Collapsed;
                pnlFileInfo.Visibility = Visibility.Visible;

                txtFileName.Text = fileInfo.Name;
                txtFilePath.Text = _pdfPath;
                txtFileSize.Text = $"{fileInfo.Length / 1024:N0} KB";

                btnPrint.IsEnabled = true;
                Title = $"sapaX Print Master - {fileInfo.Name}";
            }
        }

        private async void PrintPdf_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_pdfPath) || !File.Exists(_pdfPath))
            {
                MessageBox.Show("Please select a PDF file.", "Missing PDF");
                return;
            }

            if (cmbPrinters.SelectedItem is not PrinterInfo printer)
            {
                MessageBox.Show("Please select an active printer.", "Missing Printer");
                return;
            }

            btnPrint.IsEnabled = false;
            try
            {
                await AdminDbService.EnqueuePdfJobAsync(
                    printer.PrinterId,
                    Path.GetFileName(_pdfPath),
                    _pdfPath,
                    "Manual",
                    _currentUser?.UserId ?? Environment.UserName);

                MessageBox.Show($"Sent to the print queue for \"{printer.PrinterName}\". The agent will print it shortly.",
                                "Queued", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(AdminDbService.FriendlyError(ex), "Print error");
            }
            finally
            {
                btnPrint.IsEnabled = true;
            }
        }

        // =======================================================
        // PRINTERS (local list, saved in printers.json)
        // =======================================================

        private void LoadRealPrinters()
        {
            var printers = PrinterDiscoveryService.GetPrinters();
            PrinterPreferenceService.MergePreferences(printers);

            dgPrinters.ItemsSource = new ObservableCollection<PrinterInfo>(printers);

            RefreshPrinterCombo();
            RefreshAgentPrinterList();
        }

        private void RefreshPrinters_Click(object sender, RoutedEventArgs e)
        {
            LoadRealPrinters();
        }

        private IEnumerable<PrinterInfo> CurrentPrinters() =>
            (dgPrinters.ItemsSource as IEnumerable<PrinterInfo>) ?? Enumerable.Empty<PrinterInfo>();

        // Print tab dropdown: only printers marked Active
        private void RefreshPrinterCombo()
        {
            var selectedId = (cmbPrinters.SelectedItem as PrinterInfo)?.PrinterId;
            var active = CurrentPrinters().Where(p => p.IsActive).ToList();

            cmbPrinters.ItemsSource = active;
            cmbPrinters.SelectedItem =
                active.FirstOrDefault(p => p.PrinterId == selectedId)
                ?? active.FirstOrDefault(p => p.IsDefault)
                ?? active.FirstOrDefault();
        }

        // Administrator > Agent tab: printers this computer registers (default = Active ones)
        private void RefreshAgentPrinterList()
        {
            if (lstAgentPrinters == null) return;

            var printers = CurrentPrinters().ToList();
            lstAgentPrinters.ItemsSource = printers;
            lstAgentPrinters.SelectedItems.Clear();
            foreach (var p in printers.Where(p => p.IsActive))
                lstAgentPrinters.SelectedItems.Add(p);
        }

        // Active / Default checkbox clicks in the Printers tab
        private void PrinterFlag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not PrinterInfo changed)
                return;

            // Only one default printer
            if (cb.Tag as string == "Default" && changed.IsDefault)
            {
                foreach (var p in CurrentPrinters())
                    if (p != changed) p.IsDefault = false;
            }

            PrinterPreferenceService.Save(CurrentPrinters());
            dgPrinters.Items.Refresh();
            RefreshPrinterCombo();
        }

        // =======================================================
        // LIVE PRINTER STATUS
        // =======================================================

        private void StartPrinterMonitor()
        {
            _printerMonitor = new PrinterStatusMonitor(TimeSpan.FromSeconds(3));
            _printerMonitor.PrintersChanged += OnPrintersChanged;
            _printerMonitor.Start();
        }

        private void OnPrintersChanged(List<PrinterInfo> latest)
        {
            // Background thread: hop to the UI thread
            Dispatcher.BeginInvoke(() =>
            {
                if (dgPrinters.ItemsSource is not ObservableCollection<PrinterInfo> current)
                    return;

                dgPrinters.CommitEdit(DataGridEditingUnit.Row, true);

                bool listChanged = false;

                foreach (var gone in current.Where(c => !latest.Any(l => l.PrinterId == c.PrinterId)).ToList())
                {
                    current.Remove(gone);
                    listChanged = true;
                }

                var added = new List<PrinterInfo>();
                foreach (var l in latest)
                {
                    var existing = current.FirstOrDefault(c => c.PrinterId == l.PrinterId);
                    if (existing != null)
                        existing.Status = l.Status;      // keeps the user's Active / Default edits
                    else
                        added.Add(l);
                }

                if (added.Count > 0)
                {
                    PrinterPreferenceService.MergePreferences(added);
                    foreach (var p in added)
                        current.Add(p);
                    listChanged = true;
                }

                dgPrinters.Items.Refresh();

                if (listChanged)
                {
                    RefreshPrinterCombo();
                    RefreshAgentPrinterList();
                }
            });
        }

        public void StopPrinterMonitor()
        {
            _printerMonitor?.Dispose();
            _printerMonitor = null;
        }

        // Closing the window hides it to the tray; the printer monitor keeps running.
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}