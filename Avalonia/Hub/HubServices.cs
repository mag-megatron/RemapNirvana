using AvaloniaUI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaUI.Hub
{
    // Enum “físico” simplificado — ajuste aos teus nomes/necessidades
    public enum PhysicalInput
    {
        None,
        ButtonSouth, ButtonEast, ButtonWest, ButtonNorth, // A/B/X/Y
        DPadUp, DPadDown, DPadLeft, DPadRight,
        LeftBumper, RightBumper,
        LeftTrigger, RightTrigger,
        LeftStickClick, RightStickClick,
        Start, Back,
        LeftStickX_Pos, LeftStickX_Neg, LeftStickY_Pos, LeftStickY_Neg,
        RightStickX_Pos, RightStickX_Neg, RightStickY_Pos, RightStickY_Neg
    }

    public interface IInputCaptureService
    {
        /// Captura o próximo input físico (timeout aplicável).
        Task<PhysicalInput?> CaptureNextAsync(TimeSpan timeout, CancellationToken ct);
    }


    /// Ponte: lê snapshots do GamepadRemapService e resolve “próximo input”
    public sealed class SdlCaptureService : IInputCaptureService
    {
        private readonly GamepadRemapService _svc;

        // thresholds de ativação
        private const double ButtonOn = 0.5;  // 0/1 do teu serviço
        private const double TriggerOn = 0.50; // 0..1
        private const double AxisOn = 0.60; // -1..1

        public SdlCaptureService(GamepadRemapService svc) => _svc = svc;

        public Task<PhysicalInput?> CaptureNextAsync(TimeSpan timeout, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<PhysicalInput?>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Complete(PhysicalInput? p)
            {
                try { _svc.InputBatch -= OnBatch; } catch { }
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(p);
            }

            void OnBatch(Dictionary<string, double> snap)
            {
                var btn = DetectButton(snap);
                if (btn != PhysicalInput.None) { Complete(btn); return; }

                var trg = DetectTriggers(snap);
                if (trg != PhysicalInput.None) { Complete(trg); return; }

                var axis = DetectAxes(snap);
                if (axis != PhysicalInput.None) { Complete(axis); return; }
            }

            _svc.InputBatch += OnBatch;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            cts.Token.Register(() => Complete(null));

            return tcs.Task;
        }

        private static PhysicalInput DetectButton(IReadOnlyDictionary<string, double> s)
        {
            bool on(string k) => s.TryGetValue(k, out var v) && v >= ButtonOn;

            if (on("A")) return PhysicalInput.ButtonSouth;
            if (on("B")) return PhysicalInput.ButtonEast;
            if (on("X")) return PhysicalInput.ButtonWest;
            if (on("Y")) return PhysicalInput.ButtonNorth;

            if (on("LB")) return PhysicalInput.LeftBumper;
            if (on("RB")) return PhysicalInput.RightBumper;

            if (on("View")) return PhysicalInput.Back;
            if (on("Menu")) return PhysicalInput.Start;

            if (on("L3")) return PhysicalInput.LeftStickClick;
            if (on("R3")) return PhysicalInput.RightStickClick;

            if (on("DUp")) return PhysicalInput.DPadUp;
            if (on("DDown")) return PhysicalInput.DPadDown;
            if (on("DLeft")) return PhysicalInput.DPadLeft;
            if (on("DRight")) return PhysicalInput.DPadRight;

            return PhysicalInput.None;
        }

        private static PhysicalInput DetectTriggers(IReadOnlyDictionary<string, double> s)
        {
            bool onT(string k) => s.TryGetValue(k, out var v) && v >= TriggerOn;

            if (onT("LT")) return PhysicalInput.LeftTrigger;
            if (onT("RT")) return PhysicalInput.RightTrigger;

            return PhysicalInput.None;
        }

        private static PhysicalInput DetectAxes(IReadOnlyDictionary<string, double> s)
        {
            bool pos(string k) => s.TryGetValue(k, out var v) && v >= AxisOn;
            bool neg(string k) => s.TryGetValue(k, out var v) && v <= -AxisOn;

            if (pos("LX")) return PhysicalInput.LeftStickX_Pos;
            if (neg("LX")) return PhysicalInput.LeftStickX_Neg;

            if (pos("LY")) return PhysicalInput.LeftStickY_Pos;
            if (neg("LY")) return PhysicalInput.LeftStickY_Neg;

            if (pos("RX")) return PhysicalInput.RightStickX_Pos;
            if (neg("RX")) return PhysicalInput.RightStickX_Neg;

            if (pos("RY")) return PhysicalInput.RightStickY_Pos;
            if (neg("RY")) return PhysicalInput.RightStickY_Neg;

            return PhysicalInput.None;
        }
    }

    /// Persistência simples em JSON (AppData/NirvanaRemap/mapping.json)
   
}
