using Supabase;
using System.Threading.Tasks;
namespace Transpargo.Services
{
    public class SupabaseServices
    {
        private readonly Client _client;
        private bool _initialized = false;

        public SupabaseServices(IConfiguration config)
        {
            var url = config["SUPABASE_URL"];
            var key = config["SUPABASE_KEY"];

            var options = new SupabaseOptions
            {
                AutoConnectRealtime = false
            };

            _client = new Client(url, key, options);
        }
        public async Task<Client> GetClientAsync()
        {
            if (!_initialized)
            {
                await _client.InitializeAsync();
                _initialized = true;
            }
            return _client;
        }
    }
}
