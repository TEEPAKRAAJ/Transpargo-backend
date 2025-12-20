using Microsoft.ML.Data;

namespace Transpargo.Models
{
    public class HsCodePrediction
    {
        [ColumnName("PredictedLabel")]
        public string PredictedLabel { get; set; }
    }

}
