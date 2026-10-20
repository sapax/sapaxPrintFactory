namespace sapaXPrintCloud.Models
{
    public class ZplPrintJob
    {
        public Guid Id { get; set; }
        public string PrinterId { get; set; } = "";
        public string Zpl { get; set; } = "";
        public CloudPrintJobStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}
