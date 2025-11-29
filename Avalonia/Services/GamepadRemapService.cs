// Services/GamepadRemapService.cs
// Serviï¿½o SDL3 para leitura de gamepad/joystick fï¿½sico (Nirvana Remap)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using SDL;
using static SDL.SDL3;

namespace AvaloniaUI.Services
{
    public sealed record PhysicalDeviceInfo(
        string Name,
        string? Path,
        ushort VendorId,
        ushort ProductId,
        bool IsGamepad,
        bool IsLikelyVirtual);

    public sealed class GamepadRemapService : IDisposable
    {
        // Snapshot (nome -> valor normalizado)
        public event Action<Dictionary<string, double>>? InputBatch;
        // true = tem dispositivo fï¿½sico selecionado (gamepad ou joystick)
        public event Action<bool>? ConnectionChanged;
        public event Action<PhysicalDeviceInfo?>? PhysicalDeviceChanged;

        private unsafe SDL_Gamepad* _pad;
        private unsafe SDL_Joystick* _joy;
        private bool _usingGamepad;

        private CancellationTokenSource? _cts;
        private Task? _pollTask;
        private volatile bool _initialized;

        private readonly Dictionary<string, double> _last =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _frame =
            new(StringComparer.Ordinal);

        private const int PollIntervalMs = 16;      // ~60 Hz
        private const int MinBatchIntervalMs = 4;   // ~250 Hz
        private long _lastBatchTicks;

        // ----- Config de entrada -----
        private const double StickDeadzone = 0.10;      // 10%
        private const double TriggerDeadzone = 0.05;    // 5%
        private const double ChangeEpsilon = 0.003;     // anti-jitter global
        private const double ResponseGamma = 1.35;      // curva leve (1 = linear)

        // Ajustes de eixo (pode virar config depois)
        public bool InvertLY { get; set; } = false;
        public bool InvertRY { get; set; } = false;
        public double SensitivityL { get; set; } = 1.0; // 0.5..2.0
        public double SensitivityR { get; set; } = 1.0;

        // Modo opcional: sï¿½ emite borda de botï¿½o (transiï¿½ï¿½es)
        public bool ButtonsEdgeOnly { get; set; } = false;

        public string? CurrentPadName { get; private set; }
        public string? CurrentPadType { get; private set; }

        // -------------------------------------------------
        // Ranking / filtro de dispositivos
        // -------------------------------------------------

        // 1 = maior prioridade; nï¿½meros maiores => menos prioridade
        private static int RankController(SDL_GamepadType type, string? name, ushort vendor, ushort product)
        {
            name ??= string.Empty;

            // Flydigi VADER4 dongle DInput (exemplo de ï¿½preferido absolutoï¿½)
            if (vendor == 0x04B4 && product == 0x2412)
                return 0;

            // Xbox / XInput
            if (type == SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOXONE ||
                type == SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOX360)
                return 1;

            // PS5 / PS4
            if (type == SDL_GamepadType.SDL_GAMEPAD_TYPE_PS5 ||
                type == SDL_GamepadType.SDL_GAMEPAD_TYPE_PS4)
                return 2;

            // Flydigi / Vader por nome
            if (name.Contains("Flydigi", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Vader", StringComparison.OrdinalIgnoreCase))
                return 3;

            // Resto
            return 4;
        }

        private static bool IsLikelyVirtual(string? name, ushort vendor, ushort product, string? path)
        {
            name ??= string.Empty;
            path ??= string.Empty;

            // 1) pistas evidentes no nome
            if (name.Contains("vigem", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("virtual", StringComparison.OrdinalIgnoreCase))
                return true;

            // 2) pista no path (driver VIGEM etc.)
            if (path.Contains("vigem", StringComparison.OrdinalIgnoreCase))
                return true;

            // 3) Caso especï¿½fico: ViGEm X360 (045E:028E)
            //    No teu setup:
            //      - 045E:028E = sempre o controle virtual
            //      - fï¿½sico Machenike = 2345:E00B
            if (vendor == 0x045E && product == 0x028E)
                return true;

            // Fora isso, assume fï¿½sico
            return false;
        }




        // -------------------------------------------------
        // Ciclo de vida
        // -------------------------------------------------

        public void StartAsync()
        {
            Stop(); // zera tudo antes

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _pollTask = Task.Run(() =>
            {
                InitSdlAndPad();
                PollLoop(token);
            }, token);
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _pollTask?.Wait(250);
            }
            catch
            {
                // ignorar exceï¿½ï¿½es de cancelamento
            }
            finally
            {
                _cts = null;
                _pollTask = null;
            }

            ClosePad();

            if (_initialized)
            {
                SDL_Quit();
                _initialized = false;
            }

            _last.Clear();
            _frame.Clear();

            _usingGamepad = false;
            CurrentPadName = null;
            CurrentPadType = null;

            ConnectionChanged?.Invoke(false);
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        // -------------------------------------------------
        // SDL init / teardown
        // -------------------------------------------------

        private unsafe void InitSdlAndPad()
        {
            // Permitir eventos de joystick/gamepad mesmo em segundo plano
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            SDL_SetHint("SDL_GAMECONTROLLER_ALLOW_BACKGROUND_EVENTS", "1");

            // RawInput em thread dedicada ajuda a manter eventos quando a janela perde foco
            SDL_SetHint("SDL_JOYSTICK_THREAD", "1");
            // Rï¿½tulos A/B/X/Y de acordo com o layout atual
            SDL_SetHint("SDL_GAMECONTROLLER_USE_BUTTON_LABELS", "1");

            // HIDAPI/RAWINPUT ajudam muito na detecï¿½ï¿½o no Windows
            SDL_SetHint("SDL_JOYSTICK_HIDAPI", "1");
            SDL_SetHint("SDL_JOYSTICK_RAWINPUT", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_XBOX", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS4", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");

            var ok = SDL_Init(
                SDL_InitFlags.SDL_INIT_EVENTS |
                SDL_InitFlags.SDL_INIT_JOYSTICK |  // joystick cru
                SDL_InitFlags.SDL_INIT_GAMEPAD     // gamepad alto nï¿½vel
            );

            if (!ok)
                throw new InvalidOperationException("SDL_Init falhou (EVENTS|JOYSTICK|GAMEPAD).");

            _initialized = true;

            // Pequena folga para o subsistema registrar dispositivos
            SDL_Delay(20);
            SDL_PumpEvents();

            OpenFirstPad();
        }

        private unsafe void ClosePad()
        {
            if (_pad != null)
            {
                SDL_CloseGamepad(_pad);
                _pad = null;
            }

            if (_joy != null)
            {
                SDL_CloseJoystick(_joy);
                _joy = null;
            }
        }

        // -------------------------------------------------
        // Seleï¿½ï¿½o de dispositivo (Gamepad ? Joystick)
        // -------------------------------------------------

        private unsafe void OpenFirstPad()
        {
            ClosePad();

            _usingGamepad = false;
            CurrentPadName = null;
            CurrentPadType = null;
            PhysicalDeviceChanged?.Invoke(null);

            // 1) Tenta primeiro usando API de GAMEPAD
            int count = 0;
            SDL_JoystickID* ids = SDL_GetGamepads(&count);

            Debug.WriteLine($"[INPUT] SDL_GetGamepads ? count={count}, ids={(ids == null ? "null" : "ok")}");

            SDL_Gamepad* bestPad = null;
            int bestRank = int.MaxValue;
            string? bestPadPath = null;
            ushort bestVendor = 0;
            ushort bestProduct = 0;
            string? bestName = null;

            if (ids == null || count == 0)
            {
                Debug.WriteLine("[INPUT] SDL_GetGamepads nï¿½o retornou nenhum device (ids == null ou count == 0). " +
                                "Vou tentar fallback para SDL_GetJoysticks.");
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    SDL_JoystickID jid = ids[i];

                    SDL_Gamepad* cand = SDL_OpenGamepad(jid);
                    if (cand == null)
                    {
                        Debug.WriteLine($"[INPUT] SDL_OpenGamepad falhou para jid={jid}");
                        continue;
                    }

                    var type = SDL_GetGamepadType(cand);
                    string? name = SDL_GetGamepadName(cand);
                    string? path = SDL_GetGamepadPath(cand);
                    ushort vendor = SDL_GetGamepadVendor(cand);
                    ushort product = SDL_GetGamepadProduct(cand);

                    Debug.WriteLine($"[INPUT] GAMEPAD: {name} | tipo={type} | vid=0x{vendor:X4} pid=0x{product:X4} | path={path}");

                    if (IsLikelyVirtual(name, vendor, product, path))
                    {
                        Debug.WriteLine($"[INPUT] Ignorando gamepad virtual: {name} ({vendor:X4}:{product:X4})");
                        SDL_CloseGamepad(cand);
                        continue;
                    }

                    int rank = RankController(type, name, vendor, product);
                    if (rank < bestRank)
                    {
                        if (bestPad != null)
                            SDL_CloseGamepad(bestPad);

                        bestPad = cand;
                        bestRank = rank;
                        bestPadPath = path;
                        bestVendor = vendor;
                        bestProduct = product;
                        bestName = name;
                    }
                    else
                    {
                        SDL_CloseGamepad(cand);
                    }
                }
            }

            if (ids != null)
                SDL_free(ids);

            if (bestPad != null)
            {
                _pad = bestPad;
                _usingGamepad = true;

                string? chosenName = bestName ?? SDL_GetGamepadName(_pad);
                var chosenType = SDL_GetGamepadType(_pad);
                string? chosenPath = bestPadPath ?? SDL_GetGamepadPath(_pad);
                ushort chosenVendor = bestVendor != 0 ? bestVendor : SDL_GetGamepadVendor(_pad);
                ushort chosenProduct = bestProduct != 0 ? bestProduct : SDL_GetGamepadProduct(_pad);
                bool likelyVirtual = IsLikelyVirtual(chosenName, chosenVendor, chosenProduct, chosenPath);

                CurrentPadName = chosenName;
                CurrentPadType = chosenType.ToString();

                Debug.WriteLine($"[INPUT] Gamepad em uso: {chosenName} | tipo={chosenType}");
                ConnectionChanged?.Invoke(true);
                PhysicalDeviceChanged?.Invoke(new PhysicalDeviceInfo(
                    chosenName ?? "Gamepad",
                    chosenPath,
                    chosenVendor,
                    chosenProduct,
                    IsGamepad: true,
                    IsLikelyVirtual: likelyVirtual));
                return;
            }

            // 2) Se nï¿½o achou nenhum gamepad "oficial", tenta JOYSTICK cru
            int jCount = 0;
            SDL_JoystickID* jids = SDL_GetJoysticks(&jCount);
            Debug.WriteLine($"[INPUT] SDL_GetJoysticks ? count={jCount}, jids={(jids == null ? "null" : "ok")}");


            SDL_Joystick* bestJoy = null;
            int bestJoyRank = int.MaxValue;
            string? bestJoyPath = null;
            ushort bestJoyVendor = 0;
            ushort bestJoyProduct = 0;
            string? bestJoyName = null;

            if (jids != null && jCount > 0)
            {
                for (int i = 0; i < jCount; i++)
                {
                    SDL_JoystickID jid = jids[i];
                    SDL_Joystick* candJoy = SDL_OpenJoystick(jid);
                    if (candJoy == null)
                    {
                        Debug.WriteLine($"[INPUT] SDL_OpenJoystick falhou para jid={jid}");
                        continue;
                    }

                    string? name = SDL_GetJoystickName(candJoy);
                    string? path = SDL_GetJoystickPath(candJoy);
                    ushort vendor = SDL_GetJoystickVendor(candJoy);
                    ushort product = SDL_GetJoystickProduct(candJoy);

                    Debug.WriteLine($"[INPUT] JOYSTICK: {name} | vid=0x{vendor:X4} pid=0x{product:X4} | path={path}");

                    if (IsLikelyVirtual(name, vendor, product, path))
                    {
                        SDL_CloseJoystick(candJoy);
                        continue;
                    }

                    // Preferência explícita para Flydigi, mas sem bloquear outros
                    int rank = 10;
                    if (vendor == 0x04B4 && product == 0x2412)
                        rank = 0;
                    else if (!string.IsNullOrEmpty(name) &&
                             (name.Contains("Flydigi", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("VADER", StringComparison.OrdinalIgnoreCase)))
                        rank = 1;

                    if (rank < bestJoyRank)
                    {
                        if (bestJoy != null)
                            SDL_CloseJoystick(bestJoy);

                        bestJoy = candJoy;
                        bestJoyRank = rank;
                        bestJoyPath = path;
                        bestJoyVendor = vendor;
                        bestJoyProduct = product;
                        bestJoyName = name;
                    }
                    else
                    {
                        SDL_CloseJoystick(candJoy);
                    }
                }
            }

            if (jids != null)
                SDL_free(jids);

            if (bestJoy != null)
            {
                _joy = bestJoy;
                _usingGamepad = false;

                string? nam = bestJoyName ?? SDL_GetJoystickName(_joy);
                ushort ven = bestJoyVendor != 0 ? bestJoyVendor : SDL_GetJoystickVendor(_joy);
                ushort prod = bestJoyProduct != 0 ? bestJoyProduct : SDL_GetJoystickProduct(_joy);
                string? joyPath = bestJoyPath ?? SDL_GetJoystickPath(_joy);
                bool likelyVirtual = IsLikelyVirtual(nam, ven, prod, joyPath);

                CurrentPadName = nam;
                CurrentPadType = "Joystick";

                Debug.WriteLine($"[INPUT] Joystick em uso: {nam} | vid=0x{ven:X4} pid=0x{prod:X4}");
                ConnectionChanged?.Invoke(true);
                PhysicalDeviceChanged?.Invoke(new PhysicalDeviceInfo(
                    nam ?? "Joystick",
                    joyPath,
                    ven,
                    prod,
                    IsGamepad: false,
                    IsLikelyVirtual: likelyVirtual));
            }
            else
            {
                CurrentPadName = null;
                CurrentPadType = null;
                Debug.WriteLine("[INPUT] Nenhum gamepad/joystick físico selecionado.");
                ConnectionChanged?.Invoke(false);
                PhysicalDeviceChanged?.Invoke(null);
            }
        }

        // -------------------------------------------------
        // Loop principal
        // -------------------------------------------------

        private unsafe void PollLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                PumpEvents(); // processa add/remove
                // Garante que o estado de joystick/gamepad continue sendo atualizado
                // mesmo quando a janela perde o foco (sem depender apenas de eventos).
                SDL_UpdateJoysticks();
                SDL_UpdateGamepads();

               

                if (_usingGamepad && _pad != null)
                {
                    // Leitura via API de GAMEPAD
                    SnapshotAxes();
                    SnapshotButtons();
                }
                else if (!_usingGamepad && _joy != null)
                {
                    // Leitura via JOYSTICK cru (DInput, tipo Flydigi dongle)
                    SnapshotAxes_Joystick();
                    SnapshotButtons_Joystick();
                }
                else
                {
                    // Nenhum device vï¿½lido no momento
                    // (ConnectionChanged(false) jï¿½ ï¿½ tratado em OpenFirstPad)
                }

                FlushBatch(); // dispara InputBatch
                Thread.Sleep(PollIntervalMs);
            }
        }


        // -------------------------------------------------
        // Eventos SDL
        // -------------------------------------------------

        private unsafe void PumpEvents()
        {
            SDL_Event e = default;
            while (SDL_PollEvent(&e))
                HandleEvent(e);
        }

        private unsafe void HandleEvent(in SDL_Event e)
        {
            var type = (SDL_EventType)e.type;
            switch (type)
            {
                case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                case SDL_EventType.SDL_EVENT_JOYSTICK_ADDED:
                case SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED:
                    OpenFirstPad();
                    break;

                case SDL_EventType.SDL_EVENT_QUIT:
                    // nada especï¿½fico por enquanto
                    break;
            }
        }

        // -------------------------------------------------
        // Snapshot de entradas
        // -------------------------------------------------

        private double ApplyStickTuning(string axisName, double v)
        {
            if (axisName == "LY" && InvertLY) v = -v;
            if (axisName == "RY" && InvertRY) v = -v;

            if (axisName == "LX" || axisName == "LY")
                v = Clamp(v * SensitivityL, -1, 1);
            if (axisName == "RX" || axisName == "RY")
                v = Clamp(v * SensitivityR, -1, 1);

            return v;
        }

        private unsafe void SnapshotAxes()
        {
            if (_usingGamepad)
                SnapshotAxes_Gamepad();
            else
                SnapshotAxes_Joystick();
        }

        private unsafe void SnapshotAxes_Gamepad()
        {
            var pad = _pad;
            if (pad == null) return;

            // LEFT STICK
            short lx = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX);
            short ly = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY);
            // RIGHT STICK
            short rx = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX);
            short ry = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY);
            // TRIGGERS
            short lt = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
            short rt = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);

#if DEBUG
            Debug.WriteLine($"[AX] LX={lx} LY={ly} RX={rx} RY={ry} LT={lt} RT={rt}");
#endif

            EmitIfChanged("LX", ApplyStickTuning("LX", ShapeSigned(NormalizeAxisSigned(lx))));
            EmitIfChanged("LY", ApplyStickTuning("LY", ShapeSigned(NormalizeAxisSigned(ly))));
            EmitIfChanged("RX", ApplyStickTuning("RX", ShapeSigned(NormalizeAxisSigned(rx))));
            EmitIfChanged("RY", ApplyStickTuning("RY", ShapeSigned(NormalizeAxisSigned(ry))));

            EmitIfChanged("LT", ShapeUnsigned(NormalizeAxisUnsigned(lt)));
            EmitIfChanged("RT", ShapeUnsigned(NormalizeAxisUnsigned(rt)));
        }

        private unsafe void SnapshotAxes_Joystick()
        {
            var joy = _joy;
            if (joy == null) return;

            // Layout DInput "padrï¿½o" ï¿½ ajuste se o Flydigi usar outra ordem
            short ax0 = SDL_GetJoystickAxis(joy, 0); // LX
            short ax1 = SDL_GetJoystickAxis(joy, 1); // LY
            short ax2 = SDL_GetJoystickAxis(joy, 2); // RX
            short ax3 = SDL_GetJoystickAxis(joy, 3); // RY

            // Se o controle tiver triggers em eixos separados, ajuste aqui (eixos 4/5 etc)
            short lt = 0;
            short rt = 0;

            EmitIfChanged("LX", ApplyStickTuning("LX", ShapeSigned(NormalizeAxisSigned(ax0))));
            EmitIfChanged("LY", ApplyStickTuning("LY", ShapeSigned(NormalizeAxisSigned(ax1))));
            EmitIfChanged("RX", ApplyStickTuning("RX", ShapeSigned(NormalizeAxisSigned(ax2))));
            EmitIfChanged("RY", ApplyStickTuning("RY", ShapeSigned(NormalizeAxisSigned(ax3))));

            EmitIfChanged("LT", ShapeUnsigned(NormalizeAxisUnsigned(lt)));
            EmitIfChanged("RT", ShapeUnsigned(NormalizeAxisUnsigned(rt)));
        }

        private unsafe void SnapshotButtons()
        {
            if (_usingGamepad)
                SnapshotButtons_Gamepad();
            else
                SnapshotButtons_Joystick();
        }

        private unsafe void SnapshotButtons_Gamepad()
        {
            var pad = _pad;
            if (pad == null) return;

            EmitButton("A", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH);
            EmitButton("B", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST);
            EmitButton("X", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST);
            EmitButton("Y", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH);

            EmitButton("LB", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER);
            EmitButton("RB", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER);

            EmitButton("View", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK);
            EmitButton("Menu", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START);

            EmitButton("L3", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK);
            EmitButton("R3", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK);

            EmitButton("DUp", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP);
            EmitButton("DDown", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN);
            EmitButton("DLeft", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT);
            EmitButton("DRight", SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT);

        }

        private unsafe void SnapshotButtons_Joystick()
        {
            var joy = _joy;
            if (joy == null) return;

            // Mapeamento tï¿½pico de joystick ? aï¿½ï¿½es (ajuste se necessï¿½rio)
            MapJoyButton("A", 0);
            MapJoyButton("B", 1);
            MapJoyButton("X", 2);
            MapJoyButton("Y", 3);

            MapJoyButton("LB", 4);
            MapJoyButton("RB", 5);

            MapJoyButton("View", 6);  // Back
            MapJoyButton("Menu", 7);  // Start

            MapJoyButton("L3", 8);
            MapJoyButton("R3", 9);

            // D-Pad pode ser botï¿½o ou HAT; aqui supomos botï¿½es
            MapJoyButton("DUp", 10);
            MapJoyButton("DDown", 11);
            MapJoyButton("DLeft", 12);
            MapJoyButton("DRight", 13);
        }

        private unsafe void MapJoyButton(string logicalName, int buttonIndex)
        {
            var joy = _joy;
            if (joy == null) return;

            bool pressed = SDL_GetJoystickButton(joy, buttonIndex); // SDLBool ? bool
            double val = pressed ? 1.0 : 0.0;

            if (!ButtonsEdgeOnly)
            {
                EmitIfChanged(logicalName, val);
                return;
            }

            if (!_last.TryGetValue(logicalName, out var old)) old = 0.0;

            if ((old < 0.5 && pressed) || (old > 0.5 && !pressed))
            {
                _last[logicalName] = val;
                _frame[logicalName] = val;
            }
        }

        private unsafe void EmitButton(string logicalName, SDL_GamepadButton btn)
        {
            var pad = _pad;
            if (pad == null) return;

            bool pressed = SDL_GetGamepadButton(pad, btn); // SDLBool ? bool
            double val = pressed ? 1.0 : 0.0;

#if DEBUG
           
            if (pressed)
                Debug.WriteLine($"[BTN(GP)] {logicalName} PRESSED");
#endif

            if (!ButtonsEdgeOnly)
            {
                EmitIfChanged(logicalName, val);
                return;
            }

            if (!_last.TryGetValue(logicalName, out var old)) old = 0.0;

            if ((old < 0.5 && pressed) || (old > 0.5 && !pressed))
            {
                _last[logicalName] = val;
                _frame[logicalName] = val;
            }
          

        }

        // -------------------------------------------------
        // Batch de mudanï¿½as
        // -------------------------------------------------

        private void EmitIfChanged(string name, double value)
        {
            if (_last.TryGetValue(name, out var old) &&
                Math.Abs(old - value) < ChangeEpsilon)
                return;

            _last[name] = value;
            _frame[name] = value;
        }

        private void FlushBatch()
        {
            if (_frame.Count == 0) return;

            var now = Environment.TickCount64;
            if (now - _lastBatchTicks < MinBatchIntervalMs) return;

            _lastBatchTicks = now;

            var snap = new Dictionary<string, double>(_frame);
            _frame.Clear();

#if DEBUG
            Debug.WriteLine("[BATCH] " + string.Join(", ", snap));
#endif

            InputBatch?.Invoke(snap);
        }

        // -------------------------------------------------
        // Helpers de normalizaï¿½ï¿½o e shaping
        // -------------------------------------------------

        private static double NormalizeAxisSigned(short raw)
            => raw >= 0 ? (raw / 32767.0) : (raw / 32768.0); // -32768..32767 ? -1..+1

        private static double NormalizeAxisUnsigned(short raw)
            => raw <= 0 ? 0.0 : (raw / 32767.0);             // 0..32767 ? 0..1

        private static double ShapeSigned(double v)
        {
            if (Math.Abs(v) < StickDeadzone) return 0.0;
            var sign = Math.Sign(v);
            var m = (Math.Abs(v) - StickDeadzone) / (1.0 - StickDeadzone);
            m = Math.Pow(m, ResponseGamma);
            return Clamp(sign * m, -1.0, 1.0);
        }

        private static double ShapeUnsigned(double v)
        {
            if (v < TriggerDeadzone) return 0.0;
            var m = (v - TriggerDeadzone) / (1.0 - TriggerDeadzone);
            m = Math.Pow(m, ResponseGamma);
            return Clamp(m, 0.0, 1.0);
        }

        private static double Clamp(double x, double min, double max)
            => x < min ? min : (x > max ? max : x);
    }
}




