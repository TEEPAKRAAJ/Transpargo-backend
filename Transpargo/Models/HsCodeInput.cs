namespace Transpargo.Models
{
    public class HsCodeInput
    {
        public string Description { get; set; }
        public string Country { get; set; }
        public string Category { get; set; }
        public string Material { get; set; }

        public string HsCode { get; set; } = "0";
    }

}
