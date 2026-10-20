namespace sapaXPrintCloud.Models
{
    public enum CloudPrintJobStatus
    {
        Queued,
        Claimed,
        Printed,
        Completed,
        Failed
    }

    public class CloudPrintJob
    {
        public Guid Id { get; set; }
        public string PrinterId { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? FileBase64 { get; set; }
        public string? Zpl { get; set; }

        // Managed by cloud
        public CloudPrintJobStatus Status { get; set; } = CloudPrintJobStatus.Queued;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Claimed by agent
        public string? ClaimedByAgentId { get; set; }
        public DateTime? ClaimedAt { get; set; }

        // Job Status Update
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }

        public int RetryCount { get; set; }
        public DateTime? LockedUntil { get; set; }

    }
}
