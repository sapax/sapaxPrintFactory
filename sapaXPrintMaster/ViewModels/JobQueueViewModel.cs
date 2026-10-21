using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using sapaXPrintCore.Data;
using sapaXPrintCore.Models;
using sapaXPrintMaster.Services;

namespace sapaXPrintMaster.ViewModels
{
    // Jobs tab: reads dbo.PrintJobs (all sources: Manual, Power Automate, Power Apps, Web Form ...)
    public class JobQueueViewModel : INotifyPropertyChanged
    {
        private readonly PrintJobRepository _repo;
        private readonly DispatcherTimer _timer;
        private bool _busy;
        private string _statusText = "Loading...";

        public ObservableCollection<DbPrintJobRow> Jobs { get; } = new();

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public JobQueueViewModel()
        {
            _repo = new PrintJobRepository(() => DbConfig.IsConfigured ? DbConfig.ConnectionString : null);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (_, _) => await RefreshAsync();
            _timer.Start();

            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;

            try
            {
                if (!DbConfig.IsConfigured)
                {
                    Jobs.Clear();
                    StatusText = "Database is not configured. Open the Administrator tab.";
                    return;
                }

                var rows = await _repo.GetRecentJobsAsync(200);

                Jobs.Clear();
                foreach (var row in rows)
                    Jobs.Add(row);

                StatusText = rows.Count == 0
                    ? "No jobs yet."
                    : $"{rows.Count} job(s), newest first. Refreshes every 2 seconds.";
            }
            catch (Exception ex)
            {
                StatusText = "Could not read jobs: " + AdminDbService.FriendlyError(ex);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}