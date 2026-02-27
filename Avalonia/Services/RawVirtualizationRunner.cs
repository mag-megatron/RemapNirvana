using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using AvaloniaUI.Hub;
using AvaloniaUI.ProgramCore;
using System.IO;

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

        // Wrapper para medir latência da fila
        private readonly record struct QueuedSnapshot(Dictionary<string, double> Data, long EnqueueTick);

        private readonly Channel<QueuedSnapshot> _inputQueue =
            Channel.CreateBounded<QueuedSnapshot>(
                new BoundedChannelOptions(capacity: 8)
                {
                    SingleWriter = true,
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                });

        private CancellationTokenSource? _workerCts;
        private Task? _workerTask;

        // Telemetría básica
        private int _sampleCount = 0;
        private double _maxLatencyMs = 0;
        private double _accLatencyMs = 0;
        private long _lastReportTick = 0;

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
            Console.WriteLine("[PERF] Monitoramento de latência ATIVO (Log a cada 5s).");

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
                
                // Timestamp de entrada na fila (Producer)
                long tick = Stopwatch.GetTimestamp();
                _inputQueue.Writer.TryWrite(new QueuedSnapshot(copy, tick));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[RAW] Falha ao enfileirar snapshot: " + ex);
            }
        }

        private async Task ProcessQueueAsync(CancellationToken ct)
        {
            InitCsv();
            _lastReportTick = Stopwatch.GetTimestamp();

            try
            {
                while (await _inputQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (_inputQueue.Reader.TryRead(out var item))
                    {
                        // Processamento (Consumer)
                        var outState = _engine.BuildOutput(item.Data);

                        try
                        {
                            _vigem.ApplyAll(outState);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[RAW] Falha ao aplicar estado ViGEm: " + ex);
                        }

                        // Calcular Latência (Enqueue -> Applied)
                        long currentTick = Stopwatch.GetTimestamp();
                        double latencyMs = (currentTick - item.EnqueueTick) * 1000.0 / Stopwatch.Frequency;

                        RecordMetric(latencyMs, currentTick);
                        LogToCsv(currentTick, latencyMs);
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
            finally
            {
                _csvWriter?.Dispose();
                _csvWriter = null;
            }
        }

        private StreamWriter? _csvWriter;

        private void InitCsv()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "latency_stats.csv");
                _csvWriter = new StreamWriter(path, append: false);
                _csvWriter.WriteLine("Tick,LatencyMs");
                Console.WriteLine($"[LOG] Salvando métricas em: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERR] Falha ao criar CSV: {ex.Message}");
            }
        }

        private void LogToCsv(long tick, double latency)
        {
            if (_csvWriter == null) return;
            // Escreve apenas os dados brutos para minimizar overhead
            _csvWriter.WriteLine($"{tick},{latency:F6}");
        }

        private void RecordMetric(double latencyMs, long currentTick)
        {
            _sampleCount++;
            _accLatencyMs += latencyMs;
            if (latencyMs > _maxLatencyMs) _maxLatencyMs = latencyMs;

            // Reportar a cada 5 segundos (aprox)
            double secondsSinceLast = (currentTick - _lastReportTick) * 1.0 / Stopwatch.Frequency;
            if (secondsSinceLast >= 5.0)
            {
                if (_sampleCount > 0)
                {
                    double avg = _accLatencyMs / _sampleCount;
                    Console.WriteLine($"[PERF] Latência Interna (Queue+Process): Méd={avg:F3}ms | Máx={_maxLatencyMs:F3}ms | Samples={_sampleCount}");
                }

                _sampleCount = 0;
                _accLatencyMs = 0;
                _maxLatencyMs = 0;
                _lastReportTick = currentTick;
            }
        }

        public void Dispose()
        {
            _capture.InputBatch -= OnInputBatch;
            _workerCts?.Cancel();
        }
    }
}
