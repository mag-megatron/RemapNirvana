using System;
using System.Collections.Generic;
using Core.Events.Outputs;
using Core.Interfaces;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Infrastructure.Adapters.Outputs
{
    /// <summary>
    /// Output service that sends mapped outputs to a virtual Xbox 360 controller via ViGEm.
    /// </summary>
    public class ViGEmOutput : IOutputService, IDisposable
    {
        private readonly ViGEmClient _client;
        private readonly IXbox360Controller _controller;
        private bool _connected = false;

        public ViGEmOutput()
        {
            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateXbox360Controller();
                _controller.Connect();
                _connected = true;
            }
            catch (Exception ex)
            {
                _connected = false;
                throw new InvalidOperationException("ViGEm não encontrado ou falhou ao conectar.", ex);
            }
        }



        // O método Apply único ainda pode ser útil para debug, mas agora você vai usar ApplyAll!
        public void Apply(MappedOutput output)
        {
            if (!_connected || _controller == null) return;
            ApplyOutput(_controller, output.OutputName, output.Value);
            // Não chame SubmitReport aqui!
        }

        // ---- NOVO MÉTODO ----
        public void ApplyAll(Dictionary<string, float> outputState)
        {
            if (!_connected || _controller == null) return;

            var lx = outputState.TryGetValue("ThumbLX", out var lxValue) ? lxValue : 0f;
            var ly = outputState.TryGetValue("ThumbLY", out var lyValue) ? lyValue : 0f;
            var rx = outputState.TryGetValue("ThumbRX", out var rxValue) ? rxValue : 0f;
            var ry = outputState.TryGetValue("ThumbRY", out var ryValue) ? ryValue : 0f;

            var (sqLx, sqLy) = AxisUtils.CircleToSquare(lx, ly);
            var (sqRx, sqRy) = AxisUtils.CircleToSquare(rx, ry);

            _controller.SetButtonState(Xbox360Button.A, outputState.TryGetValue("ButtonA", out var a) && a > 0.5f);
            _controller.SetButtonState(Xbox360Button.B, outputState.TryGetValue("ButtonB", out var b) && b > 0.5f);
            _controller.SetButtonState(Xbox360Button.X, outputState.TryGetValue("ButtonX", out var x) && x > 0.5f);
            _controller.SetButtonState(Xbox360Button.Y, outputState.TryGetValue("ButtonY", out var y) && y > 0.5f);

            _controller.SetButtonState(Xbox360Button.Up, outputState.TryGetValue("DPadUp", out var up) && up > 0.5f);
            _controller.SetButtonState(Xbox360Button.Down, outputState.TryGetValue("DPadDown", out var down) && down > 0.5f);
            _controller.SetButtonState(Xbox360Button.Left, outputState.TryGetValue("DPadLeft", out var left) && left > 0.5f);
            _controller.SetButtonState(Xbox360Button.Right, outputState.TryGetValue("DPadRight", out var right) && right > 0.5f);

            _controller.SetButtonState(Xbox360Button.Start, outputState.TryGetValue("ButtonStart", out var start) && start > 0.5f);
            _controller.SetButtonState(Xbox360Button.Back, outputState.TryGetValue("ButtonBack", out var back) && back > 0.5f);
            _controller.SetButtonState(Xbox360Button.LeftShoulder, outputState.TryGetValue("ButtonLeftShoulder", out var lb) && lb > 0.5f);
            _controller.SetButtonState(Xbox360Button.RightShoulder, outputState.TryGetValue("ButtonRightShoulder", out var rb) && rb > 0.5f);

            _controller.SetButtonState(Xbox360Button.LeftThumb, outputState.TryGetValue("ThumbLPressed", out var l3) && l3 > 0.5f);
            _controller.SetButtonState(Xbox360Button.RightThumb, outputState.TryGetValue("ThumbRPressed", out var r3) && r3 > 0.5f);

            // Triggers
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(Math.Clamp(outputState.TryGetValue("TriggerLeft", out var lt) ? lt : 0f, 0f, 1f) * 255));
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(Math.Clamp(outputState.TryGetValue("TriggerRight", out var rt) ? rt : 0f, 0f, 1f) * 255));

            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, AxisToShort(sqLx));
            // SDL usa Y positivo para baixo; XInput/ViGEm usa positivo para cima. Inverte para casar.
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, AxisToShort(-sqLy));
            _controller.SetAxisValue(Xbox360Axis.RightThumbX, AxisToShort(sqRx));
            _controller.SetAxisValue(Xbox360Axis.RightThumbY, AxisToShort(-sqRy));
            // MUITO IMPORTANTE: aplique o report todo de uma vez
            try
            {
                _controller.SubmitReport();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Falha ao enviar estado ViGEm: {ex.Message}");
            }
        }

        private static short AxisToShort(float value)
        {
            value = Math.Clamp(value, -1f, 1f);
            return value >= 0f
                ? (short)(value * 32767)
                : (short)(value * 32768);
        }


        // Método privado para reduzir repetição
        private void ApplyOutput(IXbox360Controller ctrl, string name, float value)
        {
            switch (name)
            {
                case "ButtonA":
                    ctrl.SetButtonState(Xbox360Button.A, value > 0.5f); break;
                case "ButtonB":
                    ctrl.SetButtonState(Xbox360Button.B, value > 0.5f); break;
                case "ButtonX":
                    ctrl.SetButtonState(Xbox360Button.X, value > 0.5f); break;
                case "ButtonY":
                    ctrl.SetButtonState(Xbox360Button.Y, value > 0.5f); break;
                case "DPadUp":
                    ctrl.SetButtonState(Xbox360Button.Up, value > 0.5f); break;
                case "DPadDown":
                    ctrl.SetButtonState(Xbox360Button.Down, value > 0.5f); break;
                case "DPadLeft":
                    ctrl.SetButtonState(Xbox360Button.Left, value > 0.5f); break;
                case "DPadRight":
                    ctrl.SetButtonState(Xbox360Button.Right, value > 0.5f); break;
                case "ButtonStart":
                    ctrl.SetButtonState(Xbox360Button.Start, value > 0.5f); break;
                case "ButtonBack":
                    ctrl.SetButtonState(Xbox360Button.Back, value > 0.5f); break;
                case "ButtonLeftShoulder":
                    ctrl.SetButtonState(Xbox360Button.LeftShoulder, value > 0.5f); break;
                case "ButtonRightShoulder":
                    ctrl.SetButtonState(Xbox360Button.RightShoulder, value > 0.5f); break;
                case "ThumbLPressed":
                    ctrl.SetButtonState(Xbox360Button.LeftThumb, value > 0.5f); break;
                case "ThumbRPressed":
                    ctrl.SetButtonState(Xbox360Button.RightThumb, value > 0.5f); break;
                case "TriggerLeft":
                    ctrl.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(Math.Clamp(value, 0f, 1f) * 255)); break;
                case "TriggerRight":
                    ctrl.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(Math.Clamp(value, 0f, 1f) * 255)); break;
                case "ThumbLX":
                    ctrl.SetAxisValue(Xbox360Axis.LeftThumbX, AxisToShort(Math.Clamp(value, -1f, 1f))); break;
                case "ThumbLY":
                    ctrl.SetAxisValue(Xbox360Axis.LeftThumbY, AxisToShort(Math.Clamp(value, -1f, 1f))); break;
                case "ThumbRX":
                    ctrl.SetAxisValue(Xbox360Axis.RightThumbX, AxisToShort(Math.Clamp(value, -1f, 1f))); break;
                case "ThumbRY":
                    ctrl.SetAxisValue(Xbox360Axis.RightThumbY, AxisToShort(Math.Clamp(value, -1f, 1f))); break;
                default:
                    // Ignore ou logue se quiser
                    break;
            }
        }

        public void Connect()
        {
            if (!_connected)
            {
                _controller.Connect();
                _connected = true;
                Console.WriteLine("[INFO] Controle virtual conectado via ViGEm.");
            }
        }

        public void EnsureConnected()
        {
            if (_connected)
                return;

            try
            {
                _controller.Connect();
                _connected = true;
                Console.WriteLine("[INFO] Controle virtual conectado via ViGEm (EnsureConnected).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Falha ao conectar controle virtual: {ex.Message}");
                throw;
            }
        }


        public void Disconnect()
        {
            if (_connected)
            {
                _controller.Disconnect();
                _connected = false;
            }
        }

        public bool IsConnected => _connected;

        public void Dispose()
        {
            Disconnect();
            _client?.Dispose();
        }
    }
}
