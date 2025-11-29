using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

using AvaloniaUI.Services;

namespace AvaloniaUI.ViewModels
{
    public partial class DiagnosticsGamepadViewModel : ObservableObject, IDisposable
    {
        private readonly GamepadRemapService _svc;
        private readonly Dictionary<string, double> _lastUi = new();
        private long _lastUiTick;

        private const int UiMinIntervalMs = 40;
        private const double UiEpsilon = 0.01;

        [ObservableProperty] private bool connected;
        [ObservableProperty] private string deviceName = "Nenhum controle";
        [ObservableProperty] private string deviceType = "—";

        public ObservableCollection<InputStatus> Inputs { get; } = new();

        public DiagnosticsGamepadViewModel(GamepadRemapService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));

            // estado inicial
            Connected = _svc.CurrentPadName != null;
            DeviceName = _svc.CurrentPadName ?? "Nenhum controle";
            DeviceType = _svc.CurrentPadType ?? "—";

            _svc.InputBatch += OnInputBatch;
            _svc.ConnectionChanged += OnConnectionChanged;
        }

        private void OnConnectionChanged(bool connected)
        {
            Connected = connected;
            DeviceName = _svc.CurrentPadName ?? "Nenhum controle";
            DeviceType = _svc.CurrentPadType ?? "—";
        }

        private void OnInputBatch(Dictionary<string, double> snap)
        {
            if (!Connected || snap.Count == 0)
                return;

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

            if (diff.Count == 0)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var (name, val) in diff)
                {
                    var item = Inputs.FirstOrDefault(i => i.Name == name);
                    if (item is null)
                        Inputs.Add(new InputStatus { Name = name, Value = val });
                    else
                        item.Value = val;
                }
            });
        }

        public void Dispose()
        {
            _svc.InputBatch -= OnInputBatch;
            _svc.ConnectionChanged -= OnConnectionChanged;
        }
    }
}
