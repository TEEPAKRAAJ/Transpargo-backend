namespace Transpargo.Models
{
    public class TariffCalc
    {
        public string hscode { get; set; } = string.Empty;
        public string country { get; set; } = string.Empty;
        public int value { get; set; }
        public int weight { get; set; }
    }
}
