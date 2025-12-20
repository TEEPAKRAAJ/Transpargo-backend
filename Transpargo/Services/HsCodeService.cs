using Microsoft.ML;
using Transpargo.Models;
using Transpargo.Interfaces;

namespace Transpargo.Services
{
    public class HsCodeService : IHsCodeService
    {
        private readonly PredictionEngine<HsCodeInput, HsCodePrediction> _engine;

        public HsCodeService()
        {
            var ml = new MLContext();

            string modelPath = Path.Combine(Directory.GetCurrentDirectory(), "MLModels", "hs_model.zip");

            Console.WriteLine("LOADING MODEL: " + modelPath);

            var model = ml.Model.Load(modelPath, out _);

            _engine = ml.Model.CreatePredictionEngine<HsCodeInput, HsCodePrediction>(model);
        }

        public Task<string> GetHsCodeAsync(HsCodeInput input)
        {
            var result = _engine.Predict(input);
            return Task.FromResult(result.PredictedLabel);
        }
    }
}
