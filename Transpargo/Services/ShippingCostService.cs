using System.Collections.Generic;

namespace Transpargo.Services
{

    public class ShippingRule
    {
        public decimal Base { get; set; }
        public decimal PerKg { get; set; }
    }

    public class ShippingInput
    {
        public string Country { get; set; }
        public int Qty { get; set; }
        public decimal Weight { get; set; }
        public decimal L { get; set; }
        public decimal W { get; set; }
        public decimal H { get; set; }
        public string Unit { get; set; } 
    }


    public class ShippingCostService
    {
        private readonly Dictionary<string, ShippingRule> _rate = new()
    {
        {"USA", new ShippingRule{Base=1500, PerKg=350}},
        {"UK", new ShippingRule{Base=1300, PerKg=300}},
        {"UAE", new ShippingRule{Base=1000, PerKg=250}},
        {"GERMANY", new ShippingRule{Base=1400, PerKg=320}},
        {"SINGAPORE", new ShippingRule{Base=900, PerKg=200}}
    };

        private decimal ConvertToCm(decimal value, string unit)
        {
            unit = unit?.Trim().ToLower() ?? "cm";

            return unit switch
            {
                "cm" => value,
                "m" => value * 100,
                "mm" => value / 10m,
                "ft" => value * 30.48m,
                "in" => value * 2.54m,
                _ => value // fallback assume cm
            };
        }

        public decimal Calculate(ShippingInput i)
        {
            var key = i.Country?.Trim().ToUpper();

            if (!_rate.TryGetValue(key ?? "", out var r))
            {
                r = new ShippingRule { Base = 1500, PerKg = 400 };
            }

            // Convert all dimensions to CM
            decimal L = ConvertToCm(i.L, i.Unit);
            decimal W = ConvertToCm(i.W, i.Unit);
            decimal H = ConvertToCm(i.H, i.Unit);

            decimal actual = i.Weight * i.Qty;
            decimal vol = ((L * W * H) / 5000m) * i.Qty;

            decimal chargeable = Convert.ToDecimal(Math.Ceiling((double)Math.Max(actual, vol)));

            return r.Base + (r.PerKg * chargeable);
        }
    }


}
