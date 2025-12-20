using Transpargo.Models;
namespace Transpargo.DTOs
{
    public class ShipmentDashboardDto
    {
        public string Id { get; set; } = null!;
        public string Sender { get; set; } = null!;
        public string SenderEmail { get; set; } = null!;
        public string Receiver { get; set; } = null!;
        public string ReceiverEmail { get; set; } = null!;
        public decimal? Value { get; set; }
        public string? Status { get; set; }
        public string? Hs { get; set; }
        public bool HsApproved { get; set; } = false;
        public string Origin { get; set; } = null!;
        public string Destination { get; set; } = null!;
        public string DeclaredValue { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string Category { get; set; } = null!;
        public string Type { get; set; } = null!;
        public string Shippingcost { get; set; } = null!;
        public string PaymentStatus { get; set; } = "Pending";
        public string? Reason { get; set; }
        public List<LogEntry> Sender_log { get; set; } = [];
        public List<LogEntry> Receiver_log { get; set; } = [];

        public DateTime? created_at { get; set; }

        public string? SenderHs { get; set; }


    }
}
