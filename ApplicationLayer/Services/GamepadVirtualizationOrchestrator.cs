using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Core.Interfaces;

namespace ApplicationLayer.Services
{
    /// <summary>
    /// Orquestra o HidHide:
    /// - garante que o app está na whitelist
    /// - habilita o hiding global
    /// - adiciona/remove devices físicos na lista de ocultos
    /// </summary>
    public sealed class GamepadVirtualizationOrchestrator
    {
        private readonly IHidHideService _hidHide;

        public GamepadVirtualizationOrchestrator(IHidHideService hidHide)
        {
            _hidHide = hidHide ?? throw new ArgumentNullException(nameof(hidHide));
        }

        /// <summary>
        /// Configura o HidHide para:
        /// - manter o NirvanaRemap visível ao controle físico
        /// - esconder os devices físicos dos jogos
        /// </summary>
        public async Task<bool> EnsureVirtualIsPrimaryAsync(IEnumerable<string> devicesToHide)
        {
            if (_hidHide == null)
                throw new InvalidOperationException("IHidHideService não foi configurado.");

            // garante que a enumeração não é nula
            var list = devicesToHide?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                       ?? new List<string>();

            var installed = await _hidHide.IsInstalledAsync().ConfigureAwait(false);
            if (!installed)
            {
                Debug.WriteLine("[HidHide] Não instalado. Modo virtual indisponível.");
                return false;
            }

            // tenta obter o caminho do exe de forma resiliente
            string? exePath = null;

            try
            {
                exePath = Environment.ProcessPath;
            }
            catch
            {
                // ignora, tenta fallback
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                try
                {
                    var proc = Process.GetCurrentProcess();
                    exePath = proc.MainModule?.FileName;
                }
                catch
                {
                    exePath = null;
                }
            }

            if (string.IsNullOrWhiteSpace(exePath))
                throw new InvalidOperationException("Não foi possível obter o caminho do executável do NirvanaRemap.");

            // App na whitelist
            await _hidHide.AddApplicationAsync(exePath).ConfigureAwait(false);

            // Hiding global ligado
            await _hidHide.EnableHidingAsync().ConfigureAwait(false);

            // Devices físicos ocultos
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dev in list)
            {
                if (!seen.Add(dev))
                    continue;

                await _hidHide.AddDeviceAsync(dev).ConfigureAwait(false);
            }

            Debug.WriteLine("[HidHide] Modo virtual configurado (app + devices).");
            return true;
        }

        /// <summary>
        /// Remove devices da lista de ocultos (não mexe no global).
        /// </summary>
        public async Task<bool> DisableVirtualizationAsync(IEnumerable<string> devicesToUnhide)
        {
            if (_hidHide == null)
                throw new InvalidOperationException("IHidHideService não foi configurado.");

            var list = devicesToUnhide?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                       ?? new List<string>();

            var installed = await _hidHide.IsInstalledAsync().ConfigureAwait(false);
            if (!installed)
                return false;

            foreach (var dev in list)
            {
                await _hidHide.RemoveDeviceAsync(dev).ConfigureAwait(false);
            }

            Debug.WriteLine("[HidHide] Devices removidos da lista de ocultos.");
            return true;
        }
    }
}
