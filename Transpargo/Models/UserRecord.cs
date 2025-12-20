namespace Transpargo.Models
{
    public class UserRecord
    {
        public int id { get; set; }
        public string name { get; set; }
        public string email { get; set; }
        public string password { get; set; }
        public string role { get; set; }
        public bool is_active { get; set; }
        public string phone_no { get; set; }
    }
}