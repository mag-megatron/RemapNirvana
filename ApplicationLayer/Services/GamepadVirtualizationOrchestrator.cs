using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Core.Interfaces;

namespace ApplicationLayer.Services
{
    /// <summary>
    /// Orquestra o HidHide:
    /// - garante que o app esta na whitelist
    /// - habilita o hiding global
    /// - adiciona/remove devices fisicos na lista de ocultos
    /// </summary>
    public sealed class GamepadVirtualizationOrchestrator
    {
        private readonly IHidHideService _hidHide;

        public GamepadVirtualizationOrchestrator(IHidHideService hidHide)
        {
            _hidHide = hidHide ?? throw new ArgumentNullException(nameof(hidHide));
        }

        /// <summary>
        /// Configures HidHide to ensure physical devices are hidden from games
        /// while allowing this application to access them.
        /// </summary>
        /// <param name="devicesToHide">
        /// Collection of device instance paths to add to the HidHide blacklist.
        /// These devices will be hidden from all applications except those in the whitelist.
        /// </param>
        /// <returns>
        /// <c>true</c> if virtualization was successfully configured;
        /// <c>false</c> if HidHide is not installed or detectable.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the executable path for this application cannot be determined after
        /// trying multiple fallback methods (Environment.ProcessPath, Process.MainModule, Assembly.Location).
        /// </exception>
        /// <remarks>
        /// <para><strong>Side Effects:</strong></para>
        /// <list type="bullet">
        ///   <item>Modifies Windows Registry via HidHide CLI to add this application to the whitelist</item>
        ///   <item>Enables global hiding in HidHide configuration</item>
        ///   <item>Adds all specified devices to the HidHide device blacklist</item>
        /// </list>
        /// <para><strong>Behavior:</strong></para>
        /// <para>
        /// The method attempts to determine the executable path using multiple fallback strategies
        /// to ensure compatibility across different deployment scenarios (single-file publish, dotnet run, etc.).
        /// Duplicate device paths are automatically deduplicated.
        /// </para>
        /// </remarks>
        public async Task<bool> EnsureVirtualIsPrimaryAsync(IEnumerable<string> devicesToHide)
        {
            if (_hidHide == null)
                throw new InvalidOperationException("IHidHideService nao foi configurado.");

            // garante que a enumeracao nao e nula
            var list = devicesToHide?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                       ?? new List<string>();

            var installed = await _hidHide.IsInstalledAsync().ConfigureAwait(false);
            if (!installed)
            {
                Debug.WriteLine("[HidHide] Nao instalado. Modo virtual indisponivel.");
                return false;
            }

            // tenta obter o caminho do exe de forma resiliente
            var exeCandidates = new List<string>();

            try
            {
                if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                    exeCandidates.Add(Environment.ProcessPath);
            }
            catch
            {
                // ignora, tenta fallback
            }

            try
            {
                var proc = Process.GetCurrentProcess();
                if (!string.IsNullOrWhiteSpace(proc.MainModule?.FileName))
                    exeCandidates.Add(proc.MainModule.FileName);
            }
            catch
            {
                // ignora, tenta fallback
            }

            try
            {
                var entryLocation = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrWhiteSpace(entryLocation))
                    exeCandidates.Add(entryLocation);
            }
            catch
            {
                // ignora, tenta fallback
            }

            try
            {
                var entryName = Assembly.GetEntryAssembly()?.GetName().Name;
                if (!string.IsNullOrWhiteSpace(entryName))
                {
                    var appHost = Path.Combine(AppContext.BaseDirectory, entryName + ".exe");
                    if (File.Exists(appHost))
                        exeCandidates.Add(appHost);
                }
            }
            catch
            {
                // ignora, tenta fallback
            }

            var uniqueCandidates = exeCandidates
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniqueCandidates.Count == 0)
                throw new InvalidOperationException("Nao foi possivel obter o caminho do executavel do NirvanaRemap.");

            // App na whitelist (tentar todas as variantes possiveis)
            foreach (var candidate in uniqueCandidates)
            {
                if (!File.Exists(candidate))
                    continue;

                Debug.WriteLine($"[HidHide] App candidate: {candidate}");
                await _hidHide.AddApplicationAsync(candidate).ConfigureAwait(false);
            }

            // Hiding global ligado
            await _hidHide.EnableHidingAsync().ConfigureAwait(false);

            // Devices fisicos ocultos
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
        /// Removes specified devices from the HidHide blacklist, making them visible to games again.
        /// </summary>
        /// <param name="devicesToUnhide">
        /// Collection of device instance paths to remove from the HidHide blacklist.
        /// </param>
        /// <returns>
        /// <c>true</c> if devices were successfully removed from the blacklist;
        /// <c>false</c> if HidHide is not installed.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when IHidHideService is null or not properly configured.
        /// </exception>
        /// <remarks>
        /// <para><strong>Side Effects:</strong></para>
        /// <list type="bullet">
        ///   <item>Modifies Windows Registry via HidHide CLI to remove devices from blacklist</item>
        /// </list>
        /// <para><strong>Note:</strong></para>
        /// <para>
        /// This method does NOT disable global hiding. If global hiding is still enabled,
        /// other devices in the blacklist will remain hidden. To fully disable HidHide,
        /// you must separately disable global hiding via the HidHide service.
        /// </para>
        /// </remarks>
        public async Task<bool> DisableVirtualizationAsync(IEnumerable<string> devicesToUnhide)
        {
            if (_hidHide == null)
                throw new InvalidOperationException("IHidHideService nao foi configurado.");

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
