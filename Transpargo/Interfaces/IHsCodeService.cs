using System.Threading.Tasks;
using Transpargo.Models;


namespace Transpargo.Interfaces
{
    public interface IHsCodeService
    {
        Task<string> GetHsCodeAsync(HsCodeInput input);
    }

}
