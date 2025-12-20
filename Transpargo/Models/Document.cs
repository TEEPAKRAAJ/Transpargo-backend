using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;
namespace Transpargo.Models
{
    [Table("Document")]
    public class Document : BaseModel
    {
        [PrimaryKey("doc_id")]
        [JsonPropertyName("doc_id")]
        public int doc_id { get; set; }

        [PrimaryKey("document_url")]
        [JsonPropertyName("document_url")]
        public string document_url { get; set; }

        [PrimaryKey("document_name")]
        [JsonPropertyName("document_name")]
        public string document_name { get; set; }

        [PrimaryKey("s_id")]
        [JsonPropertyName("s_id")]
        public int s_id { get; set; }

        [PrimaryKey("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime created_at { get; set; }
    }
}
