namespace Transpargo.Models
{
    public class AnalyticsSummary
    {
        public decimal? TotalShipments { get; set; }
        public int InProcess { get; set; }

        public double ClearanceSucessRate { get; set; }
        public double AvgDutyPaid { get; set; }
        public Dictionary<string, int> CommonDuty { get; set; }
        public Dictionary<string, int> ShipmentsOverMonths { get; set; }
        public Dictionary<string, int> ShipmentsPerCountry { get; set; }

        // NEW: Delivered / Returned / Destroyed
        public Dictionary<string, int> StatusDistribution { get; set; }

        public string AiSummary { get; set; }

        public double AbortedRate { get; set; }
    }
}
