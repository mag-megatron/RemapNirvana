using ApplicationLayer.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AvaloniaUI.Hub;             // IMappingStore
using AvaloniaUI.ProgramCore;     // MappingEngine
using AvaloniaUI.Services;        // GamepadRemapService
using AvaloniaUI.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
      
using Infrastructure.Adapters.Outputs;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaUI.ViewModels
{
    public partial class InputStatus : ObservableObject
    {
        [ObservableProperty] private string name = "";
        [ObservableProperty] private double value;
    }

    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private const int UiMinIntervalMs = 40;
        private const double UiEpsilon = 0.02;

        private readonly GamepadRemapService _svc;
        private readonly MappingEngine _engine;
        private readonly IMappingStore _mappingStore;
        private readonly Dictionary<string, double> _lastUi = new();
        private readonly GamepadVirtualizationOrchestrator _virtualization;
        private readonly ViGEmOutput _vigem;
        private bool _physicalConnected;
        private string? _currentPhysicalDeviceId;
        private bool _currentDeviceLikelyVirtual;
        private long _lastUiTick;
        public MappingHubViewModel Hub { get; }


        public ObservableCollection<InputStatus> CurrentInputs { get; } = new();
        public ObservableCollection<InputStatus> VirtualOutputs { get; } = new();
        public ObservableCollection<string> DevicesToHide { get; } = new();

        [ObservableProperty] private string newDeviceId = "";
        [ObservableProperty] private string status = "Parado";
        [ObservableProperty] private string greeting = "Olá, Jogador 😎";

        [ObservableProperty] private bool outputReady;

        public string OutputStatusText => OutputReady
            ? "Saída Virtual: Ativa"
            : "Saída Virtual: Inativa";

        // ---------------------------------------------------------
        // Constructor
        // ---------------------------------------------------------
        public MainViewModel(
            GamepadRemapService svc,
            IMappingStore mappingStore,
            MappingHubViewModel hubVm,
            ViGEmOutput vigem,
            GamepadVirtualizationOrchestrator virtualization)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _mappingStore = mappingStore ?? throw new ArgumentNullException(nameof(mappingStore));
            _engine = new MappingEngine(mappingStore);

            _vigem = vigem ?? throw new ArgumentNullException(nameof(vigem));
            _virtualization = virtualization ?? throw new ArgumentNullException(nameof(virtualization));

            Hub = hubVm ?? throw new ArgumentNullException(nameof(hubVm));

            // quando o usuário salvar o mapping no Hub, recarrega o engine
            Hub.Saved += async () => await ReloadMappingAsync();

            InitializeVigem();
            SubscribeEvents();

         

            // Carrega o mapping inicial
            _ = ReloadMappingAsync();
        }

        private void InitializeVigem()
        {
            try
            {
                _vigem.EnsureConnected();
                OutputReady = true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ViGEmOutput init failed: " + ex);
                OutputReady = false;
                Status = "ViGEm indisponível (sem saída virtual)";
            }
        }

        private void SubscribeEvents()
        {
            _svc.InputBatch += OnInputBatch;
            _svc.ConnectionChanged += OnConnectionChanged;
            _svc.PhysicalDeviceChanged += OnPhysicalDeviceChanged;
        }

        // ---------------------------------------------------------
        // Mapping reload (quando Hub salva)
        // ---------------------------------------------------------
        public async Task ReloadMappingAsync()
        {
            await _engine.LoadAsync(Hub.CurrentProfileId, CancellationToken.None);
        }


        // ---------------------------------------------------------
        // Event: conexão física detectada/solta
        // ---------------------------------------------------------
        private void OnConnectionChanged(bool connected)
        {
            _physicalConnected = connected;

            Status = connected
                ? "Controle físico conectado"
                : "Aguardando controle físico";
        }

        private void OnPhysicalDeviceChanged(PhysicalDeviceInfo? info)
        {
            _currentPhysicalDeviceId = info?.Path;
            _currentDeviceLikelyVirtual = info?.IsLikelyVirtual ?? false;

            if (info is null)
                return;

            var friendly = $"{info.Name} (VID:PID {info.VendorId:X4}:{info.ProductId:X4})";
            Status = _currentDeviceLikelyVirtual
                ? $"Dispositivo virtual detectado: {friendly} (ignorado)"
                : $"Controle físico ativo: {friendly}";
        }

        // ---------------------------------------------------------
        // Event: snapshot SDL recebido
        // ---------------------------------------------------------
        private void OnInputBatch(Dictionary<string, double> snap)
        {
            // se não tem controle ou snapshot vazio, ignora
            if (!_physicalConnected || snap.Count == 0)
                return;

            // 1. UI sempre atualiza, mesmo que o ViGEm esteja indisponível
            UpdateUiThrottled(snap);

            // 2. Se não tem saída virtual, para aqui (apenas monitor)
            if (!OutputReady)
                return;

            // 3+4. Constrói estado final diretamente do snapshot (CONTÍNUO)
            var outState = _engine.BuildOutput(snap);

            // 5. Aplica na saída virtual
            try
            {
                _vigem.ApplyAll(outState);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[ViGEm] ApplyAll falhou: " + ex);
            }
            // >>> Atualiza diagnóstico do controle virtual
            UpdateVirtualUi(outState);

        }

        private void UpdateVirtualUi(Dictionary<string, float> state)
        {
            // simples: sem throttle; se quiser, pode reutilizar o esquema do físico
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var (name, value) in state)
                {
                    var item = VirtualOutputs.FirstOrDefault(i => i.Name == name);
                    if (item is null)
                        VirtualOutputs.Add(new InputStatus { Name = name, Value = value });
                    else
                        item.Value = value;
                }
            });
        }

        // ---------------------------------------------------------
        // UI throttle (não engasga)
        // ---------------------------------------------------------
        private void UpdateUiThrottled(Dictionary<string, double> snap)
        {
            var now = Environment.TickCount64;
            if (now - _lastUiTick < UiMinIntervalMs)
                return;

            _lastUiTick = now;
            var diff = new Dictionary<string, double>();

            foreach (var (name, value) in snap)
            {
                if (!_lastUi.TryGetValue(name, out var old) || Math.Abs(old - value) >= UiEpsilon)
                {
                    _lastUi[name] = value;
                    diff[name] = value;
                }
            }

            if (diff.Count > 0)
                Dispatcher.UIThread.Post(() => ApplyUiUpdates(diff));
        }

        private void ApplyUiUpdates(Dictionary<string, double> diff)
        {
            foreach (var (name, val) in diff)
            {
                var item = CurrentInputs.FirstOrDefault(i => i.Name == name);
                if (item is null)
                    CurrentInputs.Add(new InputStatus { Name = name, Value = val });
                else
                    item.Value = val;
            }
        }

        // ---------------------------------------------------------
        // Commands
        // ---------------------------------------------------------
        [RelayCommand]
        public async Task StartAsync()
        {
            await ReloadMappingAsync();

            _svc.StartAsync(); // ✅ chama o método certo do serviço
            Status = "Capturando…";
        }

        [RelayCommand]
        private void Stop()
        {
            _svc.Stop();
            Status = "Parado";
        }

      
        [RelayCommand]
        private void OpenDiagnostics()
        {
            var win = Program.Services.GetRequiredService<DiagnosticsGamepadWindow>();
            var vm = Program.Services.GetRequiredService<DiagnosticsGamepadViewModel>();

            win.DataContext = vm;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desk)
                win.Show(desk.MainWindow);
            else
                win.Show();
        }

        // ---------------------------------------------------------
        // HidHide / Modo virtual
        // ---------------------------------------------------------

        [RelayCommand]
        private void AddDeviceId()
        {
            if (!string.IsNullOrWhiteSpace(NewDeviceId))
            {
                DevicesToHide.Add(NewDeviceId.Trim());
                NewDeviceId = string.Empty;
            }
        }

        [RelayCommand]
        private void RemoveDeviceId(string deviceId)
        {
            if (!string.IsNullOrWhiteSpace(deviceId))
                DevicesToHide.Remove(deviceId);
        }

        [RelayCommand]
        private void HideCurrentDevice()
        {
            if (string.IsNullOrWhiteSpace(_currentPhysicalDeviceId))
            {
                Status = "Nenhum controle físico selecionado para ocultar.";
                return;
            }

            if (_currentDeviceLikelyVirtual)
            {
                Status = "Ignorando ocultação de controle virtual (ViGEm).";
                return;
            }

            if (!DevicesToHide.Contains(_currentPhysicalDeviceId))
                DevicesToHide.Add(_currentPhysicalDeviceId);

            Status = "Controle físico atual adicionado à lista de ocultos.";
        }

        [RelayCommand]
        private async Task EnableVirtualModeAsync()
        {
            try
            {
                Status = "Ativando modo virtual (HidHide + ViGEm)…";

                var devices = new List<string>(DevicesToHide);

                if (!string.IsNullOrWhiteSpace(_currentPhysicalDeviceId) &&
                    !_currentDeviceLikelyVirtual &&
                    !devices.Contains(_currentPhysicalDeviceId))
                {
                    devices.Add(_currentPhysicalDeviceId);
                }

                var ok = await _virtualization.EnsureVirtualIsPrimaryAsync(devices);

                if (ok)
                {
                    // ViGEm já está conectado pelo InitializeVigem()
                    Status = "Modo virtual ativo: jogos devem ver apenas o controle virtual.";
                }
                else
                {
                    Status = "HidHide não encontrado ou falha ao ativar modo virtual.";
                }
            }
            catch (Exception ex)
            {
                Status = $"Erro ao ativar modo virtual: {ex.Message}";
                Debug.WriteLine("[HidHide] Exception: " + ex);
            }
        }

        [RelayCommand]
        private async Task DisableVirtualModeAsync()
        {
            Status = "Removendo devices da lista de ocultos…";

            var ok = await _virtualization.DisableVirtualizationAsync(DevicesToHide);

            Status = ok
                ? "Devices removidos da ocultação HidHide."
                : "Falha ao desativar ocultação (HidHide ausente?).";
        }


        // ---------------------------------------------------------
        // Dispose
        // ---------------------------------------------------------
        public void Dispose()
        {
            _svc.InputBatch -= OnInputBatch;
            _svc.ConnectionChanged -= OnConnectionChanged;
            _svc.PhysicalDeviceChanged -= OnPhysicalDeviceChanged;
        }
    }
}
