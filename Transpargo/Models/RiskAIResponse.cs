namespace Transpargo.Models
{
    public class RiskAIResponse
    {
        public string hsCode { get; set; }
        public string riskLevel { get; set; }
        public int riskScore { get; set; }
        public double confidence { get; set; }
        public string summary { get; set; }

        public List<string> requiredDocuments { get; set; }   // AI generated
        public List<string> keyRisks { get; set; }
        public List<string> recommendations { get; set; }

        public List<string>? userProvidedDocuments { get; set; }   // added
        public List<string>? missingDocuments { get; set; }        // added
        public string productName { get; set; }
    }

}
