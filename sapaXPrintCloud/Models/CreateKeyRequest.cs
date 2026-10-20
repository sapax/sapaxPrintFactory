namespace sapaXPrintCloud.Models
{
    public class CreateKeyRequest
    {
        public string OwnerType { get; set; } = "agent";
        public string OwnerId { get; set; } = string.Empty;
    }
}
