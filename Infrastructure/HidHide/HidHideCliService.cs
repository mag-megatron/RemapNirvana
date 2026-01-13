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
            var code = await RunAsync("--cloak-on");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao habilitar: exit {code}");
        }

        public async Task DisableHidingAsync()
        {
            var code = await RunAsync("--cloak-off");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao desabilitar: exit {code}");
        }

        public async Task AddApplicationAsync(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));

            var code = await RunAsync($"--app-reg \"{exePath}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao adicionar app: exit {code}");
        }

        public async Task RemoveApplicationAsync(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));

            var code = await RunAsync($"--app-unreg \"{exePath}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao remover app: exit {code}");
        }

        public async Task AddDeviceAsync(string deviceIdOrPath)
        {
            if (string.IsNullOrWhiteSpace(deviceIdOrPath))
                throw new ArgumentNullException(nameof(deviceIdOrPath));

            var normalized = NormalizeDeviceInstancePath(deviceIdOrPath);
            Debug.WriteLine($"[HidHide] AddDevice normalized: {normalized}");
            var code = await RunAsync($"--dev-hide \"{normalized}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao adicionar device: exit {code} ({normalized})");
        }

        public async Task RemoveDeviceAsync(string deviceIdOrPath)
        {
            if (string.IsNullOrWhiteSpace(deviceIdOrPath))
                throw new ArgumentNullException(nameof(deviceIdOrPath));

            var normalized = NormalizeDeviceInstancePath(deviceIdOrPath);
            Debug.WriteLine($"[HidHide] RemoveDevice normalized: {normalized}");
            var code = await RunAsync($"--dev-unhide \"{normalized}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao remover device: exit {code} ({normalized})");
        }

        private static string NormalizeDeviceInstancePath(string deviceIdOrPath)
        {
            // HidHide expects a device instance path like "HID\\VID_054C&PID_0CE6&IG_00\\7&..."
            var trimmed = deviceIdOrPath.Trim();

            if (trimmed.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
                return trimmed.ToUpperInvariant();

            var withoutPrefix = trimmed;
            if (withoutPrefix.StartsWith(@"\\?\"))
                withoutPrefix = withoutPrefix.Substring(@"\\?\".Length);

            string? head = null;
            if (withoutPrefix.StartsWith("HID#", StringComparison.OrdinalIgnoreCase))
            {
                head = "HID\\";
                withoutPrefix = withoutPrefix.Substring("HID#".Length);
            }
            else if (withoutPrefix.StartsWith("USB#", StringComparison.OrdinalIgnoreCase))
            {
                head = "USB\\";
                withoutPrefix = withoutPrefix.Substring("USB#".Length);
            }

            if (withoutPrefix.StartsWith("{", StringComparison.Ordinal))
            {
                var endGuid = withoutPrefix.IndexOf("}_", StringComparison.Ordinal);
                if (endGuid >= 0 && endGuid + 2 < withoutPrefix.Length)
                    withoutPrefix = withoutPrefix.Substring(endGuid + 2);
            }

            withoutPrefix = NormalizeVidPidTokens(withoutPrefix);

            var guidIndex = withoutPrefix.LastIndexOf('#');
            if (guidIndex >= 0)
                withoutPrefix = withoutPrefix.Substring(0, guidIndex);

            var normalized = withoutPrefix.Replace('#', '\\');
            if (!string.IsNullOrEmpty(head))
                normalized = head + normalized;

            return normalized.ToUpperInvariant();
        }

        private static string NormalizeVidPidTokens(string input)
        {
            input = NormalizeToken(input, "VID", preferLast4: true);
            input = NormalizeToken(input, "PID", preferLast4: true);
            input = input.Replace("_PID_", "&PID_");
            return input;
        }

        private static string NormalizeToken(string input, string token, bool preferLast4)
        {
            var idx = 0;
            while (true)
            {
                idx = input.IndexOf(token, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0 || idx + token.Length + 1 > input.Length)
                    break;

                var sepIndex = idx + token.Length;
                var sep = input[sepIndex];
                if (sep != '&' && sep != '_')
                {
                    idx = sepIndex;
                    continue;
                }

                var valueStart = sepIndex + 1;
                var valueEnd = valueStart;
                while (valueEnd < input.Length && IsHexChar(input[valueEnd]))
                    valueEnd++;

                var len = valueEnd - valueStart;
                if (len > 4 && preferLast4)
                {
                    var value = input.Substring(valueStart, len);
                    if (len == 8 && value.StartsWith("0002", StringComparison.OrdinalIgnoreCase))
                        value = value.Substring(4);
                    else
                        value = value.Substring(len - 4);

                    input = input.Substring(0, valueStart) + value + input.Substring(valueEnd);
                    valueEnd = valueStart + value.Length;
                }

                if (sep == '&')
                    input = input.Substring(0, sepIndex) + "_" + input.Substring(sepIndex + 1);

                idx = valueEnd;
            }

            return input;
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'a' && c <= 'f') ||
                   (c >= 'A' && c <= 'F');
        }
    }
}
