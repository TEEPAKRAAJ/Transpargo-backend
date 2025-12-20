using System.Text.Json;
using System.Text.RegularExpressions;

namespace Transpargo.Services
{

    public class DutyRuleModel
    {
        public string country { get; set; } = "";
        public string category { get; set; } = "";
        public string subcategory { get; set; } = "";
        public List<string> hs { get; set; } = new();
        public string duty { get; set; } = "";
        public string gstRule { get; set; } = "";
    }

    public class TradeComplianceService
    {
        private readonly List<DutyRuleModel> _rules;

        public TradeComplianceService()
        {
            var basePath = AppContext.BaseDirectory;
            var filePath = Path.Combine(basePath, "Config", "DutyRules.json");

            //debug
            Console.WriteLine("Loading JSON from:");
            Console.WriteLine(filePath);


            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Duty rule file not found at: {filePath}");

            var json = File.ReadAllText(filePath);

            //debug
            Console.WriteLine(json.Substring(0, Math.Min(json.Length, 1000)));

            //skip the comments and trailing comma to not throw exception
            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            _rules = JsonSerializer.Deserialize<List<DutyRuleModel>>(json, options) ?? new();


            //debug
            Console.WriteLine("Number of rules:");
            Console.WriteLine(_rules.Count);
            var first = _rules.FirstOrDefault();

            if (first != null)
            {
                Console.WriteLine(JsonSerializer.Serialize(first, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            else
            {
                Console.WriteLine("No rules loaded!");
            }


            //clean the data
            foreach (var r in _rules)
            {
                r.country = r.country.Trim().ToUpper();
                r.category = r.category.Trim().ToUpper();
                r.subcategory = r.subcategory.Trim().ToUpper();
                r.duty = r.duty.Trim().ToUpper();
                r.gstRule = r.gstRule.Trim().ToUpper();

                r.hs = r.hs
                    .Select(h => Regex.Replace(h, @"\D", ""))
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .ToList();
            }
        }

        private DutyRuleModel? MatchRule(string country, string hsCode)
        {
            //debug
            Console.WriteLine("The parameters passed are");


            country = country.Trim().ToUpper();
            hsCode = Regex.Replace(hsCode, @"\D", "");

            Console.WriteLine(country);
            Console.WriteLine(hsCode);
            return _rules
                .Where(r => r.country == country)
                .Where(r => r.hs.Any(h => hsCode.StartsWith(h)))
                .OrderByDescending(r => r.hs.Max(h => h.Length))
                .FirstOrDefault();
        }

        private decimal ConvertCurrencyDuty(string duty, decimal weightKg, decimal declaredValue)
        {
            var fx = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            { "USD", 83m },
            { "EUR", 90m },
            { "GBP", 105m },
            { "IDR", 0.0055m },
            { "RP", 0.0055m }
        };

            var match = Regex.Match(duty, @"(\d+(\.\d+)?)\s*(EUR|GBP|USD|IDR|RP)\/*(KG|TNE)?");
            if (!match.Success) return 0;

            decimal amount = decimal.Parse(match.Groups[1].Value);
            string currency = match.Groups[3].Value.ToUpper();
            string unit = match.Groups[4].Value.ToUpper();

            decimal rate = fx.TryGetValue(currency, out var r) ? r : 1;

            decimal perKg = (unit == "TNE") ? (amount * rate) / 1000 : amount * rate;

            return declaredValue > 0 ? (perKg * weightKg / declaredValue * 100) : 0;
        }

        private decimal EvaluateGst(string rule, decimal declaredValue)
        {
            // Format: <2500?5:18
            var match = Regex.Match(rule, @"<(\d+)\?(\d+):(\d+)");

            if (match.Success)
            {
                decimal threshold = decimal.Parse(match.Groups[1].Value);
                decimal lowRate = decimal.Parse(match.Groups[2].Value);
                decimal highRate = decimal.Parse(match.Groups[3].Value);

                return declaredValue < threshold ? lowRate : highRate;
            }

            // fallback for simple case: "18"
            return decimal.TryParse(Regex.Match(rule, @"\d+").Value, out var fixedRate)
                ? fixedRate
                : 18;
        }

        public async Task<(decimal DutyPercent, decimal GstPercent)> ComputeAsync(
        string country,
        string hsCode,
        decimal declaredValue,
        decimal weightKg)
        {
            var normalizedCountry = country.ToUpper();
            var normalizedHs = Regex.Replace(hsCode, @"\D", "");

            var rule = MatchRule(normalizedCountry, normalizedHs);
            if (rule == null)
            {
                Console.WriteLine("rule is NULL");
                return (0, 0);
            }

            //debug
            Console.WriteLine("Matched rule:");
            Console.WriteLine(rule);

            decimal dutyPercent = 0;

            if (Regex.IsMatch(rule.duty, @"^\d+(\.\d+)?(\+\d+(\.\d+)?)*$"))
            {
                dutyPercent = rule.duty.Split('+').Sum(x => decimal.Parse(x));
            }
            else if (Regex.IsMatch(rule.duty, @"\d"))
            {
                dutyPercent = ConvertCurrencyDuty(rule.duty, weightKg, declaredValue);
            }

            decimal gstPercent = EvaluateGst(rule.gstRule, declaredValue);

            //debug
            Console.WriteLine("The value of duty and gst:");
            Console.WriteLine(Math.Round(dutyPercent, 2));
            Console.WriteLine(Math.Round(gstPercent, 2));

            return (Math.Round(dutyPercent, 2), Math.Round(gstPercent, 2));
        }



    }
}
