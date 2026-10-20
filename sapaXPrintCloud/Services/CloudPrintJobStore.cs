using sapaXPrintCloud.Models;
using System.Collections.Concurrent;

namespace sapaXPrintCloud.Services
{
    public static class CloudPrintJobStore
    {
        private static readonly ConcurrentDictionary<Guid, CloudPrintJob> _jobs = new();

        public static void Enqueue(CloudPrintJob job)
        {
            job.Status = CloudPrintJobStatus.Queued;
            job.CreatedAt = DateTime.UtcNow;
            _jobs[job.Id] = job;
        }

        // Agent pulls exactly ONE job
        public static CloudPrintJob? Dequeue(string agentId)
        {
            var now = DateTime.UtcNow;

            foreach (var job in _jobs.Values.OrderBy(j => j.CreatedAt))
            {
                if (job.Status != CloudPrintJobStatus.Queued)
                    continue;

                if (job.LockedUntil != null && job.LockedUntil > now)
                    continue;

                job.Status = CloudPrintJobStatus.Claimed;
                job.ClaimedByAgentId = agentId;
                job.ClaimedAt = now;
                job.LockedUntil = now.AddMinutes(5);
                job.RetryCount++;

                return job;
            }

            return null;
        }

        public static CloudPrintJob? GetById(Guid id)
        {
            _jobs.TryGetValue(id, out var job);
            return job;
        }

        public static void Complete(Guid id, bool success, string? error)
        {
            if (!_jobs.TryGetValue(id, out var job))
                return;

            job.Status = success
                ? CloudPrintJobStatus.Completed
                : CloudPrintJobStatus.Failed;

            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = error;
        }
    }
}
