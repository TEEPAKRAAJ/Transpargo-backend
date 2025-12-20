using Supabase.Postgrest.Models;
using Supabase.Postgrest.Attributes;
using System.Text.Json.Serialization;
using Transpargo.Models;
using System.Runtime.CompilerServices;

namespace Transpargo.Models
{
    [Table("Shipment")]
    public class Shipment : BaseModel
    {
        [PrimaryKey("s_id")]
        [Column("s_id")]
        [JsonPropertyName("s_id")]
        public int SId { get; set; }

        [Column("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime created_at { get; set; }

        [Column("id")]
        [JsonPropertyName("id")]
        public int UId { get; set; }

        [Column("duty_mode")]
        [JsonPropertyName("duty_mode")]
        public string? DutyMode { get; set; }

        [Column("shipping_cost")]
        [JsonPropertyName("shipping_cost")]
        public decimal? ShippingCost { get; set; }

        [Column("Sender_log")]
        [JsonPropertyName("Sender_log")]
        public List<LogEntry> Sender_Log { get; set; }

        [Column("Receiver_log")]
        [JsonPropertyName("Receiver_log")]
        public List<LogEntry> Receiver_Log { get; set; }

        [Column("status")]
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [Column("Additional_docs")]
        [JsonPropertyName("Additional_docs")]
        public List<Additional_docs> Additional_docs { get; set; }

        [Column("Reason")]
        [JsonPropertyName("Reason")]
        public string? Reason { get; set; }

        [Column("payment_log")]
        [JsonPropertyName("payment_log")]
        public Dictionary<string, object>? payment_log { get; set; }

    }
}

