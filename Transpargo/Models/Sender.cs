using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Transpargo.Models
{
    [Table("Sender")]
    public class Sender : BaseModel
    {
        [PrimaryKey("sender_id")]
        public int SenderId { get; set; }

        [Column("s_id")]
        public int ShipmentId { get; set; }

        [Column("Name")]
        public string Name { get; set; }

        [Column("email")]
        public string Email { get; set; }

        [Column("phone")]
        public string? Phone { get; set; }

        [Column("address")]
        public string? Address { get; set; }

        [Column("city")]
        public string? City { get; set; }

        [Column("state")]
        public string? State { get; set; }

        [Column("postal")]
        public string? Postal { get; set; }

        [Column("Country")]
        public string? Country { get; set; }
    }
}
