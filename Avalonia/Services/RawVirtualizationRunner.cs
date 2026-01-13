using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
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
        private readonly Channel<Dictionary<string, double>> _inputQueue =
            Channel.CreateBounded<Dictionary<string, double>>(
                new BoundedChannelOptions(capacity: 8)
                {
                    SingleWriter = true,
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });

        private CancellationTokenSource? _workerCts;
        private Task? _workerTask;

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
                _workerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _workerTask = Task.Run(() => ProcessQueueAsync(_workerCts.Token), _workerCts.Token);

                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutdown normal
            }
            finally
            {
                _workerCts?.Cancel();
                _capture.InputBatch -= OnInputBatch;
                _capture.Stop();

                if (_workerTask != null)
                {
                    try
                    {
                        await _workerTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // cancelamento esperado
                    }
                }
            }
        }

        private void OnInputBatch(Dictionary<string, double> snapshot)
        {
            if (snapshot.Count == 0)
                return;

            try
            {
                // Cópia leve para não bloquear o loop de captura nem depender
                // do buffer reutilizado pelo GamepadRemapService.
                var copy = new Dictionary<string, double>(snapshot, StringComparer.Ordinal);
                _inputQueue.Writer.TryWrite(copy);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[RAW] Falha ao enfileirar snapshot: " + ex);
            }
        }

        private async Task ProcessQueueAsync(CancellationToken ct)
        {
            try
            {
                while (await _inputQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (_inputQueue.Reader.TryRead(out var snap))
                    {
                        var outState = _engine.BuildOutput(snap);

                        try
                        {
                            _vigem.ApplyAll(outState);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[RAW] Falha ao aplicar estado ViGEm: " + ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // encerramento solicitado
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[RAW] Loop de processamento interrompido: " + ex);
            }
        }

        public void Dispose()
        {
            _capture.InputBatch -= OnInputBatch;
            _workerCts?.Cancel();
        }
    }
}
