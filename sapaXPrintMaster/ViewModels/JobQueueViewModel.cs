using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using sapaXPrintMaster.Services;
using sapaXPrintCore.Models;
using sapaXPrintCore.Jobs;

namespace sapaXPrintMaster.ViewModels
{
    public class JobQueueViewModel
    {
        public ObservableCollection<PrintJob> Jobs { get; } = new();

        private readonly DispatcherTimer _timer;

        public JobQueueViewModel()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        }

        private void Refresh()
        {
            var jobs = PrintJobQueue.GetAllJobs();

            Jobs.Clear();
            foreach (var job in jobs)
                Jobs.Add(job);
        }
    }
}
