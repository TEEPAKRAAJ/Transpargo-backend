namespace Transpargo.Models
{
    public class PendingUser
    {
        public int id { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string password { get; set; }
        public string requested_role { get; set; }
        public string phone_no { get; set; }
    }
}
