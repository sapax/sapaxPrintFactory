namespace sapaXPrintCloud.Models
{
    public class ApiKey
    {
        public Guid ApiKeyId { get; set; }

        // First 16 chars of raw key (for display / audit)
        public string KeyPrefix { get; set; } = string.Empty;

        // SHA256 hash (32 bytes)
        public byte[] KeyHash { get; set; } = Array.Empty<byte>();

        public string OwnerType { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;

        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
