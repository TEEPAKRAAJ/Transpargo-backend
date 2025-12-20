using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Transpargo.Models
{
    [Table("Product")]
    public class Product : BaseModel
    {
        [PrimaryKey("product_id")]
        public int ProductId { get; set; }

        [Column("s_id")]
        public int ShipmentId { get; set; }

        [Column("type")]
        public string? Type { get; set; }

        [Column("no_of_packages")]
        public int? NoOfPackages { get; set; }

        [Column("weight")]
        public decimal? Weight { get; set; }

        [Column("Length")]
        public decimal? Length { get; set; }

        [Column("Width")]
        public decimal? Width { get; set; }

        [Column("Height")]
        public decimal? Height { get; set; }

        [Column("special_handling")]
        public string? SpecialHandling { get; set; }

        [Column("quantity")]
        public int? Quantity { get; set; }

        [Column("category")]
        public string? Category { get; set; }

        [Column("value")]
        public decimal? Value { get; set; }

        [Column("Description")]
        public string? Description { get; set; }

        [Column("composition")]
        public string? Composition { get; set; }

        [Column("intended_use")]
        public string? IntendedUse { get; set; }

        [Column("hs_code")]
        public string? HsCode { get; set; }

        [Column("unit")]
        public string? Unit { get; set; }

        [Column("sender_hs_code")]
        public string? SenderHs { get; set; }
    }
}
