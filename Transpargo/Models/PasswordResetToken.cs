
namespace Transpargo.Models
{
    public class PasswordResetToken
    {
        public int id { get; set; }
        public string email { get; set; }
        public string token { get; set; }
        public DateTime expiry { get; set; }
    }
}
