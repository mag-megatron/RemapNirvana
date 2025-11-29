using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Core.Interfaces;

namespace Infrastructure.HidHide
{
    public sealed class HidHideCliService : IHidHideService
    {
        private readonly string _cliPath;

        public HidHideCliService(string? cliPathFromConfig = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _cliPath = string.Empty;
                return;
            }

            if (!string.IsNullOrWhiteSpace(cliPathFromConfig))
            {
                _cliPath = cliPathFromConfig;
            }
            else
            {
                // Ajuste se o seu caminho for outro
                _cliPath = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";
            }
        }

        private bool CliExists => !string.IsNullOrWhiteSpace(_cliPath) && File.Exists(_cliPath);

        private async Task<int> RunAsync(string args)
        {
            if (!CliExists)
                return -1;

            var psi = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return -1;

            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }

        public Task<bool> IsInstalledAsync()
            => Task.FromResult(CliExists);

        public async Task EnableHidingAsync()
        {
            var code = await RunAsync("--global enable");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao habilitar: exit {code}");
        }

        public async Task DisableHidingAsync()
        {
            var code = await RunAsync("--global disable");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao desabilitar: exit {code}");
        }

        public async Task AddApplicationAsync(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));

            var code = await RunAsync($"--app-block add \"{exePath}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao adicionar app: exit {code}");
        }

        public async Task RemoveApplicationAsync(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));

            var code = await RunAsync($"--app-block remove \"{exePath}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao remover app: exit {code}");
        }

        public async Task AddDeviceAsync(string deviceIdOrPath)
        {
            if (string.IsNullOrWhiteSpace(deviceIdOrPath))
                throw new ArgumentNullException(nameof(deviceIdOrPath));

            var code = await RunAsync($"--device-block add \"{deviceIdOrPath}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao adicionar device: exit {code}");
        }

        public async Task RemoveDeviceAsync(string deviceIdOrPath)
        {
            if (string.IsNullOrWhiteSpace(deviceIdOrPath))
                throw new ArgumentNullException(nameof(deviceIdOrPath));

            var code = await RunAsync($"--device-block remove \"{deviceIdOrPath}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao remover device: exit {code}");
        }
    }
}
