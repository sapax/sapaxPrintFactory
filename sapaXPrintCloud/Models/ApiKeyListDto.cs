namespace sapaXPrintCloud.Models
{
    public class ApiKeyListDto
    {
        public Guid ApiKeyId { get; set; }
        public string KeyPrefix { get; set; } = string.Empty;
        public string OwnerType { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
