using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;
namespace Transpargo.Models
{
    [Table("Receiver")]
    public class Receiver : BaseModel
    {
        [PrimaryKey("receiver_id")]
        [JsonPropertyName("receiver_id")]
        public int ReceiverId { get; set; }

        [Column("s_id")]
        [JsonPropertyName("s_id")]
        public int ShipmentId { get; set; }

        [Column("Name")]
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [Column("Email")]
        [JsonPropertyName("Email")]
        public string Email { get; set; }

        [Column("Phone")]
        [JsonPropertyName("Phone")]
        public string? Phone { get; set; }

        [Column("Address")]
        [JsonPropertyName("Address")]
        public string? Address { get; set; }

        [Column("City")]
        [JsonPropertyName("City")]
        public string? City { get; set; }

        [Column("State")]
        [JsonPropertyName("State")]
        public string? State { get; set; }

        [Column("postal")]
        [JsonPropertyName("postal")]
        public string? Postal { get; set; }

        [Column("Country")]
        [JsonPropertyName("Country")]
        public string? Country { get; set; }
    }
}
