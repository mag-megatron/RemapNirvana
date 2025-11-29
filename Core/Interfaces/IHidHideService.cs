using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{

    public interface IHidHideService
    {
        Task<bool> IsInstalledAsync();

        Task EnableHidingAsync();
        Task DisableHidingAsync();

        Task AddApplicationAsync(string exePath);
        Task RemoveApplicationAsync(string exePath);

        Task AddDeviceAsync(string deviceIdOrPath);
        Task RemoveDeviceAsync(string deviceIdOrPath);
    }
}
