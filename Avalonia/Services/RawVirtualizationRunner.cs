using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaUI.Hub;
using AvaloniaUI.ProgramCore;

namespace AvaloniaUI.Services
{
    /// <summary>
    /// Bridge headless que pega snapshots físicos (SDL) e aplica na saída ViGEm.
    /// Usado quando a app é iniciada com --raw.
    /// </summary>
    public sealed class RawVirtualizationRunner : IDisposable
    {
        private readonly GamepadRemapService _capture;
        private readonly MappingEngine _engine;
        private readonly Infrastructure.Adapters.Outputs.ViGEmOutput _vigem;

        public RawVirtualizationRunner(
            GamepadRemapService capture,
            IMappingStore mappingStore,
            Infrastructure.Adapters.Outputs.ViGEmOutput vigem)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _vigem = vigem ?? throw new ArgumentNullException(nameof(vigem));
            _engine = new MappingEngine(mappingStore ?? throw new ArgumentNullException(nameof(mappingStore)));
        }

        public async Task RunAsync(CancellationToken ct)
        {
            await _engine.LoadAsync(profileId: null, ct).ConfigureAwait(false);

            _capture.InputBatch += OnInputBatch;
            _capture.StartAsync();

            Console.WriteLine("[RAW] Capturando entradas físicas e emitindo via ViGEm. Ctrl+C para sair.");

            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutdown normal
            }
            finally
            {
                _capture.InputBatch -= OnInputBatch;
                _capture.Stop();
            }
        }

        private void OnInputBatch(Dictionary<string, double> snapshot)
        {
            if (snapshot.Count == 0)
                return;

            var outState = _engine.BuildOutput(snapshot);

            try
            {
                _vigem.ApplyAll(outState);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[RAW] Falha ao aplicar estado ViGEm: " + ex);
            }
        }

        public void Dispose()
        {
            _capture.InputBatch -= OnInputBatch;
        }
    }
}
