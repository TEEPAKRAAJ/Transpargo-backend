using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Transpargo.Models
{
    public class SignupRequest
    {
        [Required]
        public string name { get; set; }

        [Required]
        [EmailAddress]
        public string email { get; set; }

        [Required]
        public string password { get; set; }

        [Required]
        public string role { get; set; }

        [Required]
        public string phone_no { get; set; }
    }

}
