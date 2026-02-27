# C# Source Export

## Infrastructure\HidHide\HidHideCliService.cs

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Core.Interfaces;

namespace Infrastructure.HidHide
{
    public sealed class HidHideCliService : IHidHideService
    {
        private readonly string _cliPath;

        public HidHideCliService(string? cliPathFromConfig = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _cliPath = string.Empty;
                return;
            }

            if (!string.IsNullOrWhiteSpace(cliPathFromConfig))
            {
                _cliPath = cliPathFromConfig;
            }
            else
            {
                // Ajuste se o seu caminho for outro
                _cliPath = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";
            }
        }

        private bool CliExists => !string.IsNullOrWhiteSpace(_cliPath) && File.Exists(_cliPath);

        private async Task<int> RunAsync(string args)
        {
            if (!CliExists)
                return -1;

            var psi = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return -1;

            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }

        public Task<bool> IsInstalledAsync()
            => Task.FromResult(CliExists);

        public async Task EnableHidingAsync()
        {
            var code = await RunAsync("--cloak-on");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao habilitar: exit {code}");
        }

        public async Task DisableHidingAsync()
        {
            var code = await RunAsync("--cloak-off");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao desabilitar: exit {code}");
        }

        public async Task AddApplicationAsync(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));

            var code = await RunAsync($"--app-reg \"{exePath}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao adicionar app: exit {code}");
        }

        public async Task RemoveApplicationAsync(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));

            var code = await RunAsync($"--app-unreg \"{exePath}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao remover app: exit {code}");
        }

        public async Task AddDeviceAsync(string deviceIdOrPath)
        {
            if (string.IsNullOrWhiteSpace(deviceIdOrPath))
                throw new ArgumentNullException(nameof(deviceIdOrPath));

            var normalized = NormalizeDeviceInstancePath(deviceIdOrPath);
            Debug.WriteLine($"[HidHide] AddDevice normalized: {normalized}");
            var code = await RunAsync($"--dev-hide \"{normalized}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao adicionar device: exit {code} ({normalized})");
        }

        public async Task RemoveDeviceAsync(string deviceIdOrPath)
        {
            if (string.IsNullOrWhiteSpace(deviceIdOrPath))
                throw new ArgumentNullException(nameof(deviceIdOrPath));

            var normalized = NormalizeDeviceInstancePath(deviceIdOrPath);
            Debug.WriteLine($"[HidHide] RemoveDevice normalized: {normalized}");
            var code = await RunAsync($"--dev-unhide \"{normalized}\"");
            if (code != 0)
                throw new InvalidOperationException($"HidHideCLI falhou ao remover device: exit {code} ({normalized})");
        }

        private static string NormalizeDeviceInstancePath(string deviceIdOrPath)
        {
            // HidHide expects a device instance path like "HID\\VID_054C&PID_0CE6&IG_00\\7&..."
            var trimmed = deviceIdOrPath.Trim();

            if (trimmed.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
                return trimmed.ToUpperInvariant();

            var withoutPrefix = trimmed;
            if (withoutPrefix.StartsWith(@"\\?\"))
                withoutPrefix = withoutPrefix.Substring(@"\\?\".Length);

            string? head = null;
            if (withoutPrefix.StartsWith("HID#", StringComparison.OrdinalIgnoreCase))
            {
                head = "HID\\";
                withoutPrefix = withoutPrefix.Substring("HID#".Length);
            }
            else if (withoutPrefix.StartsWith("USB#", StringComparison.OrdinalIgnoreCase))
            {
                head = "USB\\";
                withoutPrefix = withoutPrefix.Substring("USB#".Length);
            }

            if (withoutPrefix.StartsWith("{", StringComparison.Ordinal))
            {
                var endGuid = withoutPrefix.IndexOf("}_", StringComparison.Ordinal);
                if (endGuid >= 0 && endGuid + 2 < withoutPrefix.Length)
                    withoutPrefix = withoutPrefix.Substring(endGuid + 2);
            }

            withoutPrefix = NormalizeVidPidTokens(withoutPrefix);

            var guidIndex = withoutPrefix.LastIndexOf('#');
            if (guidIndex >= 0)
                withoutPrefix = withoutPrefix.Substring(0, guidIndex);

            var normalized = withoutPrefix.Replace('#', '\\');
            if (!string.IsNullOrEmpty(head))
                normalized = head + normalized;

            return normalized.ToUpperInvariant();
        }

        private static string NormalizeVidPidTokens(string input)
        {
            input = NormalizeToken(input, "VID", preferLast4: true);
            input = NormalizeToken(input, "PID", preferLast4: true);
            input = input.Replace("_PID_", "&PID_");
            return input;
        }

        private static string NormalizeToken(string input, string token, bool preferLast4)
        {
            var idx = 0;
            while (true)
            {
                idx = input.IndexOf(token, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0 || idx + token.Length + 1 > input.Length)
                    break;

                var sepIndex = idx + token.Length;
                var sep = input[sepIndex];
                if (sep != '&' && sep != '_')
                {
                    idx = sepIndex;
                    continue;
                }

                var valueStart = sepIndex + 1;
                var valueEnd = valueStart;
                while (valueEnd < input.Length && IsHexChar(input[valueEnd]))
                    valueEnd++;

                var len = valueEnd - valueStart;
                if (len > 4 && preferLast4)
                {
                    var value = input.Substring(valueStart, len);
                    if (len == 8 && value.StartsWith("0002", StringComparison.OrdinalIgnoreCase))
                        value = value.Substring(4);
                    else
                        value = value.Substring(len - 4);

                    input = input.Substring(0, valueStart) + value + input.Substring(valueEnd);
                    valueEnd = valueStart + value.Length;
                }

                if (sep == '&')
                    input = input.Substring(0, sepIndex) + "_" + input.Substring(sepIndex + 1);

                idx = valueEnd;
            }

            return input;
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'a' && c <= 'f') ||
                   (c >= 'A' && c <= 'F');
        }
    }
}

```

## ApplicationLayer\Services\GamepadVirtualizationOrchestrator.cs
```csharp
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
        /// Configura o HidHide para:
        /// - manter o NirvanaRemap visivel ao controle fisico
        /// - esconder os devices fisicos dos jogos
        /// </summary>
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
        /// Remove devices da lista de ocultos (nao mexe no global).
        /// </summary>
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

```

## Avalonia\Views\MainWindow.axaml.cs
```csharp
using Avalonia.Controls;

namespace AvaloniaUI;

public partial class MainWindow : Window
{
    public MainWindow() { 
        InitializeComponent(); 
    }
}

```

## Avalonia\Views\GamepadMonitor.axaml.cs
```csharp
using Avalonia.Controls;

namespace AvaloniaUI.Views;

public partial class GamepadMonitor : UserControl
{
    public GamepadMonitor()
    {
        InitializeComponent();
    }
}

```

## Avalonia\Views\DiagnosticsGamepadWindow.axaml.cs
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using AvaloniaUI.Views;


namespace AvaloniaUI.Views
{
    public partial class DiagnosticsGamepadWindow : Window
    {
        public DiagnosticsGamepadWindow()
        {
            InitializeComponent();
        }
    }
}

```

## Infrastructure\Adapters\XInputs\XInputService.cs
```csharp
// Corrigido para o namespace correto de IGamepadService

using Core.Entities;
using Core.Events.Inputs;
using Core.Interfaces;
// Para EventHandler, Convert, Thread
// Para PropertyInfo (se mantiver a reflexÃ£o)

// Para XInput

// Para Thread

namespace Infrastructure.Adapters.XInputs
{
    /// <summary>
    /// ImplementaÃ§Ã£o de <see cref="IGamepadService"/> que utiliza a API XInput
    /// para monitorizar continuamente o estado de um gamepad e emitir eventos de entrada.
    /// </summary>
    public class XInputService : IGamepadService // Adicionada a implementaÃ§Ã£o da interface
    {
        private readonly XInput _adapter = new();
        private GamepadState _previousState = new(); // Renomeado para clareza
        private volatile bool _isRunning; // volatile para visibilidade entre threads
        private Thread? _pollingThread;   // Renomeado para clareza
        private bool _isConnected;

        private const int XInputPollingIntervalMilliseconds = 20; // Mais configurÃ¡vel, ~50Hz

        /// <summary>
        /// Evento disparado quando uma nova entrada do controlador Ã© recebida.
        /// </summary>
        public event EventHandler<ControllerInput>? InputReceived;
        public event EventHandler<bool>? ConnectionChanged;
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Inicia a escuta por entradas do gamepad XInput.
        /// Uma thread dedicada Ã© iniciada para monitorizar o estado do gamepad.
        /// </summary>
        public void StartListening()
        {
            if (_isRunning) return;

            _isRunning = true;
            // Atualiza o estado anterior para o estado atual antes de comeÃ§ar,
            // para evitar disparar eventos para todos os botÃµes no inÃ­cio se jÃ¡ estiverem ativos.
            _previousState = _adapter.GetState(out _isConnected);
            ConnectionChanged?.Invoke(this, _isConnected);

            _pollingThread = new Thread(ListenLoop)
            {
                IsBackground = true, // Permite que a aplicaÃ§Ã£o feche mesmo que a thread esteja a correr
                Name = "XInputPollingThread" // Ãštil para depuraÃ§Ã£o
            };
            _pollingThread.Start();
        }

        /// <summary>
        /// Para a escuta por entradas do gamepad XInput.
        /// Solicita que a thread de monitorizaÃ§Ã£o pare e aguarda a sua conclusÃ£o.
        /// </summary>
        public void StopListening()
        {
            _isRunning = false;
            _pollingThread?.Join(); // Aguarda a thread terminar
            _pollingThread = null;
            if (_isConnected)
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
        }

        public void UpdateCalibration(CalibrationSettings settings)
        {
            _adapter.ApplyCalibration(settings);
        }

        public event EventHandler<GamepadState>? StateChanged;

        /// <summary>
        /// Loop principal executado pela thread de monitorizaÃ§Ã£o para ler o estado do gamepad,
        /// detetar mudanÃ§as e disparar eventos <see cref="InputReceived"/>.
        /// </summary>
        private void ListenLoop()
        {
            while (_isRunning)
            {
                GamepadState currentState = _adapter.GetState(out var connected);
                if (_isConnected != connected)
                {
                    _isConnected = connected;
                    ConnectionChanged?.Invoke(this, connected);
                }
                StateChanged?.Invoke(this, currentState.Clone());

                // OtimizaÃ§Ã£o: Em vez de reflexÃ£o, comparar cada propriedade diretamente.
                // Isto Ã© mais verboso mas significativamente mais performÃ¡tico num loop frequente.
                if (connected)
                {
                    CompareAndRaiseEvents(currentState);
                }

                _previousState = currentState.Clone(); // Guarda o estado atual para a prÃ³xima iteraÃ§Ã£o

                Thread.Sleep(XInputPollingIntervalMilliseconds); // Controla a frequÃªncia de polling
            }
        }

        /// <summary>
        /// Compara o estado atual do gamepad com o estado anterior e dispara eventos
        /// <see cref="InputReceived"/> para quaisquer controlos que tenham mudado.
        /// (Alternativa otimizada Ã  abordagem de reflexÃ£o).
        /// </summary>
        /// <param name="current">O estado atual do gamepad.</param>
        private void CompareAndRaiseEvents(GamepadState current)
        {
            // BotÃµes Booleanos
            RaiseIfChanged(nameof(GamepadState.ButtonA), _previousState.ButtonA, current.ButtonA);
            RaiseIfChanged(nameof(GamepadState.ButtonB), _previousState.ButtonB, current.ButtonB);
            RaiseIfChanged(nameof(GamepadState.ButtonX), _previousState.ButtonX, current.ButtonX);
            RaiseIfChanged(nameof(GamepadState.ButtonY), _previousState.ButtonY, current.ButtonY);
            RaiseIfChanged(nameof(GamepadState.DPadUp), _previousState.DPadUp, current.DPadUp);
            RaiseIfChanged(nameof(GamepadState.DPadDown), _previousState.DPadDown, current.DPadDown);
            RaiseIfChanged(nameof(GamepadState.DPadLeft), _previousState.DPadLeft, current.DPadLeft);
            RaiseIfChanged(nameof(GamepadState.DPadRight), _previousState.DPadRight, current.DPadRight);
            RaiseIfChanged(nameof(GamepadState.ButtonStart), _previousState.ButtonStart, current.ButtonStart);
            RaiseIfChanged(nameof(GamepadState.ButtonBack), _previousState.ButtonBack, current.ButtonBack);
            RaiseIfChanged(nameof(GamepadState.ButtonLeftShoulder), _previousState.ButtonLeftShoulder, current.ButtonLeftShoulder);
            RaiseIfChanged(nameof(GamepadState.ButtonRightShoulder), _previousState.ButtonRightShoulder, current.ButtonRightShoulder);
            RaiseIfChanged(nameof(GamepadState.ThumbLPressed), _previousState.ThumbLPressed, current.ThumbLPressed);
            RaiseIfChanged(nameof(GamepadState.ThumbRPressed), _previousState.ThumbRPressed, current.ThumbRPressed);

            // Gatilhos (Float)
            RaiseIfChanged(nameof(GamepadState.TriggerLeft), _previousState.TriggerLeft, current.TriggerLeft);
            RaiseIfChanged(nameof(GamepadState.TriggerRight), _previousState.TriggerRight, current.TriggerRight);

            // AnalÃ³gicos (Float)
            RaiseIfChanged(nameof(GamepadState.ThumbLX), _previousState.ThumbLX, current.ThumbLX);
            RaiseIfChanged(nameof(GamepadState.ThumbLY), _previousState.ThumbLY, current.ThumbLY);
            RaiseIfChanged(nameof(GamepadState.ThumbRX), _previousState.ThumbRX, current.ThumbRX);
            RaiseIfChanged(nameof(GamepadState.ThumbRY), _previousState.ThumbRY, current.ThumbRY);
        }

        /// <summary>
        /// Helper para disparar ControllerInput se o valor booleano mudou.
        /// </summary>
        private void RaiseIfChanged(string name, bool previousValue, bool currentValue)
        {
            if (previousValue != currentValue)
            {
                InputReceived?.Invoke(this, new ControllerInput(name, currentValue ? 1.0f : 0.0f));
            }
        }

        /// <summary>
        /// Helper para disparar ControllerInput se o valor float mudou (com uma pequena tolerÃ¢ncia).
        /// </summary>
        private void RaiseIfChanged(string name, float previousValue, float currentValue, float tolerance = 0.0001f)
        {
            if (Math.Abs(previousValue - currentValue) > tolerance)
            {
                InputReceived?.Invoke(this, new ControllerInput(name, currentValue));
            }
        }

        // Se preferir manter a reflexÃ£o (mais conciso, mas menos performÃ¡tico):
        /*
        private void CheckPropertiesWithReflection(GamepadState current)
        {
            var props = typeof(GamepadState).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                object? previousValue = prop.GetValue(_previousState);
                object? currentValue = prop.GetValue(current);

                if (!Equals(previousValue, currentValue))
                {
                    // A conversÃ£o para float pode precisar de mais cuidado dependendo do tipo da propriedade
                    // (bool vs float). Booleans sÃ£o frequentemente convertidos para 1.0f (true) e 0.0f (false).
                    float eventValue = 0f;
                    if (currentValue is bool bVal)
                    {
                        eventValue = bVal ? 1.0f : 0.0f;
                    }
                    else if (currentValue is float fVal)
                    {
                        eventValue = fVal;
                    }
                    // Adicionar mais conversÃµes se GamepadState tiver outros tipos

                    InputReceived?.Invoke(this, new ControllerInput(prop.Name, eventValue));
                }
            }
        }
        */
    }
}
```

## Infrastructure\Adapters\XInputs\XInputInterop.cs
```csharp
using System.Runtime.InteropServices;

namespace Infrastructure.Adapters.XInputs;

    /// <summary>
    /// ContÃ©m estruturas e constantes para interoperabilidade com a API XInput.
    /// Estas definiÃ§Ãµes sÃ£o usadas para comunicaÃ§Ã£o P/Invoke com xinput*.dll.
    /// </summary>
    public static class XInputInterop // SugestÃ£o: tornar estÃ¡tica, pois sÃ³ contÃ©m membros estÃ¡ticos e structs
    {
        /// <summary>
        /// Representa o estado dos botÃµes, gatilhos e analÃ³gicos de um gamepad XInput.
        /// Corresponde Ã  estrutura XINPUT_GAMEPAD da API XInput.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XInputGamepad // Nome original: XinputGamepad
        {
            /// <summary>
            /// Bitmask dos botÃµes do gamepad que estÃ£o atualmente pressionados.
            /// Use as constantes XInputGamepad* para verificar botÃµes especÃ­ficos.
            /// </summary>
            public ushort wButtons;
            /// <summary>
            /// O valor atual do controlo do gatilho esquerdo. O valor estÃ¡ entre 0 e 255.
            /// </summary>
            public byte bLeftTrigger;
            /// <summary>
            /// O valor atual do controlo do gatilho direito. O valor estÃ¡ entre 0 e 255.
            /// </summary>
            public byte bRightTrigger;
            /// <summary>
            /// O valor atual do eixo X do analÃ³gico esquerdo. O valor estÃ¡ entre -32768 e 32767.
            /// Um valor de 0 Ã© considerado centro.
            /// </summary>
            public short sThumbLX;
            /// <summary>
            /// O valor atual do eixo Y do analÃ³gico esquerdo. O valor estÃ¡ entre -32768 e 32767.
            /// Um valor de 0 Ã© considerado centro.
            /// </summary>
            public short sThumbLY;
            /// <summary>
            /// O valor atual do eixo X do analÃ³gico direito. O valor estÃ¡ entre -32768 e 32767.
            /// Um valor de 0 Ã© considerado centro.
            /// </summary>
            public short sThumbRX;
            /// <summary>
            /// O valor atual do eixo Y do analÃ³gico direito. O valor estÃ¡ entre -32768 e 32767.
            /// Um valor de 0 Ã© considerado centro.
            /// </summary>
            public short sThumbRY;
        }

        /// <summary>
        /// Representa o estado de um controlador XInput, incluindo o nÃºmero do pacote e o estado do gamepad.
        /// Corresponde Ã  estrutura XINPUT_STATE da API XInput.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XInputState // Nome original: XinputState
        {
            /// <summary>
            /// NÃºmero do pacote do estado. Indica se o estado do controlador mudou desde a Ãºltima leitura.
            /// Se dwPacketNumber for o mesmo em chamadas consecutivas a XInputGetState, o estado nÃ£o mudou.
            /// </summary>
            public uint dwPacketNumber;
            /// <summary>
            /// Estrutura <see cref="XInputGamepad"/> contendo o estado atual do gamepad.
            /// </summary>
            public XInputGamepad Gamepad;
        }

        // Constantes para os bitmasks dos botÃµes do XInput
        // Estes valores correspondem Ã s definiÃ§Ãµes em XInput.h

        /// <summary>Bitmask para o botÃ£o D-Pad Cima do gamepad XInput.</summary>
        public const ushort XInputGamepadDpadUp = 0x0001; // Nome original: XinputGamepadDpadUp
        
        /// <summary>Bitmask para o botÃ£o D-Pad Baixo do gamepad XInput.</summary>
        public const ushort XInputGamepadDpadDown = 0x0002; // Nome original: XinputGamepadDpadDown
        
        /// <summary>Bitmask para o botÃ£o D-Pad Esquerda do gamepad XInput.</summary>
        public const ushort XInputGamepadDpadLeft = 0x0004; // Nome original: XinputGamepadDpadLeft
        
        /// <summary>Bitmask para o botÃ£o D-Pad Direita do gamepad XInput.</summary>
        public const ushort XInputGamepadDpadRight = 0x0008; // Nome original: XinputGamepadDpadRight
        
        /// <summary>Bitmask para o botÃ£o Start do gamepad XInput.</summary>
        public const ushort XInputGamepadStart = 0x0010; // Nome original: XinputGamepadStart
        
        /// <summary>Bitmask para o botÃ£o Back (Select) do gamepad XInput.</summary>
        public const ushort XInputGamepadBack = 0x0020; // Nome original: XinputGamepadBack
        
        /// <summary>Bitmask para o botÃ£o do analÃ³gico esquerdo (pressionado) do gamepad XInput.</summary>
        public const ushort XInputGamepadLeftThumb = 0x0040; // Nome original: XinputGamepadLeftThumb
        
        /// <summary>Bitmask para o botÃ£o do analÃ³gico direito (pressionado) do gamepad XInput.</summary>
        public const ushort XInputGamepadRightThumb = 0x0080; // Nome original: XinputGamepadRightThumb

        /// <summary>Bitmask para o botÃ£o de ombro esquerdo (LB) do gamepad XInput.</summary>
        public const ushort XInputGamepadLeftShoulder = 0x0100; // Nome original: XinputGamepadLeftShoulder
        
        /// <summary>Bitmask para o botÃ£o de ombro direito (RB) do gamepad XInput.</summary>
        public const ushort XInputGamepadRightShoulder = 0x0200; // Nome original: XinputGamepadRightShoulder
        
        // Os botÃµes A, B, X, Y sÃ£o frequentemente referidos sem a palavra "Gamepad" no nome da constante em exemplos,
        // mas manter a consistÃªncia com XInputGamepadDpadUp, etc., Ã© bom.
        
        /// <summary>Bitmask para o botÃ£o A do gamepad XInput.</summary>
        public const ushort XInputGamepadA = 0x1000; // Nome original: XinputGamepadA
        
        /// <summary>Bitmask para o botÃ£o B do gamepad XInput.</summary>
        public const ushort XInputGamepadB = 0x2000; // Nome original: XinputGamepadB
        
        /// <summary>Bitmask para o botÃ£o X do gamepad XInput.</summary>
        public const ushort XInputGamepadX = 0x4000; // Nome original: XinputGamepadX
        
        /// <summary>Bitmask para o botÃ£o Y do gamepad XInput.</summary>
        public const ushort XInputGamepadY = 0x8000; // Nome original: XinputGamepadY
    }

```

## Infrastructure\Adapters\XInputs\XInput.cs
```csharp
using System; // Para Math.Abs
using System.Runtime.InteropServices;
using Core.Entities; // Para GamepadState
using static Infrastructure.Adapters.XInputs.XInputInterop; // Para XInputState e constantes dos botÃµes

namespace Infrastructure.Adapters.XInputs
{
    /// <summary>
    /// Adaptador para interagir com a API XInput para ler o estado de gamepads.
    /// Utiliza P/Invoke para chamar funÃ§Ãµes da xinput1_4.dll.
    /// </summary>
    public class XInput // "partial" sugere que pode haver outra parte desta classe noutro ficheiro.
    {
        // Nota: xinput1_4.dll Ã© para Windows 8+. Para compatibilidade com Windows 7, seria xinput1_3.dll ou xinput9_1_0.dll.
        // Se precisar de suportar mÃºltiplas versÃµes, podem ser necessÃ¡rias estratÃ©gias de carregamento mais avanÃ§adas.
        /// <summary>
        /// ObtÃ©m o estado atual do controlador XInput especificado.
        /// </summary>
        /// <param name="dwUserIndex">Ãndice do utilizador (controlador). Pode ser de 0 a 3.</param>
        /// <param name="pState">Recebe o estado atual do controlador.</param>
        /// <returns>
        /// Se a funÃ§Ã£o for bem-sucedida, o valor de retorno Ã© <c>ERROR_SUCCESS</c> (0).
        /// Se o controlador nÃ£o estiver conectado, o valor de retorno Ã© <c>ERROR_DEVICE_NOT_CONNECTED</c> (1167).
        /// </returns>
       
        private delegate int XInputGetStateDelegate(int dwUserIndex, out XInputInterop.XInputState pState);
        private static readonly XInputGetStateDelegate _xInputGetState;

        // Permite que testes definam um estado personalizado quando nenhuma
        // biblioteca XInput real estÃ¡ disponÃ­vel.
        public static Func<XInputInterop.XInputState>? TestStateProvider { get; set; }
  

        static XInput()
        {
            string[] libs = {"xinput1_4.dll", "xinput9_1_0.dll", "xinput1_3.dll"};
            foreach (var lib in libs)
            {
                if (NativeLibrary.TryLoad(lib, out var handle))
                {
                    if (NativeLibrary.TryGetExport(handle, "XInputGetState", out var func))
                    {
                        _xInputGetState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(func);
                        return;
                    }
                }
            }

            // Quando nenhuma biblioteca estÃ¡ disponÃ­vel (por exemplo, em ambientes
            // de teste ou sistemas nÃ£o Windows), fornece uma implementaÃ§Ã£o de
            // fallback que permite que os testes definam um estado personalizado.
            _xInputGetState = (int _, out XInputInterop.XInputState state) =>
            {
                if (TestStateProvider is not null)
                {
                    state = TestStateProvider();
                    return ERROR_SUCCESS;
                }

                state = default;
                return ERROR_DEVICE_NOT_CONNECTED;
            };
        }

        public static int XInputGetState(int dwUserIndex, out XInputInterop.XInputState pState)
            => _xInputGetState(dwUserIndex, out pState);

        
        /// <summary>
        /// CÃ³digo de retorno da API XInput que indica sucesso na operaÃ§Ã£o.
        /// </summary>
        public const int ERROR_SUCCESS = 0; // Nome original: ErrorSuccess

        /// <summary>
        /// CÃ³digo de retorno da API XInput que indica que o controlador nÃ£o estÃ¡ conectado.
        /// </summary>
        public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

        /// <summary>
        /// ObtÃ©m o estado atual do primeiro gamepad XInput conectado (Ã­ndice 0)
        /// e mapeia-o para um objeto <see cref="GamepadState"/>.
        /// </summary>
        /// <returns>
        /// Um objeto <see cref="GamepadState"/> representando o estado atual do gamepad.
        /// Se o gamepad nÃ£o estiver conectado, retorna um estado com valores padrÃ£o (geralmente tudo `false` ou `0f`).
        /// </returns>
        private CalibrationSettings _calibration = new(); // default

        public void ApplyCalibration(CalibrationSettings settings)
        {
            _calibration = settings;
        }
        
        public GamepadState GetState(out bool isConnected)
        {
            // Tenta obter o estado para o primeiro controlador (Ã­ndice 0)
            var result = XInputGetState(0, out var xState);
            isConnected = result == ERROR_SUCCESS;
            var gamepad = xState.Gamepad;
            
            var rawLT = gamepad.bLeftTrigger;
            var rawRT = gamepad.bRightTrigger;
            
            var rawLX = xState.Gamepad.sThumbLX;
            var rawLY = xState.Gamepad.sThumbLY;   
            var rawRX = xState.Gamepad.sThumbRX;
            var rawRY = xState.Gamepad.sThumbRY; 
            
            var normLT = NormalizeTrigger(rawLT, _calibration.LeftTriggerStart, _calibration.LeftTriggerEnd);
            var normRT = NormalizeTrigger(rawRT, _calibration.RightTriggerStart, _calibration.RightTriggerEnd);

            var normalizedLX = NormalizeAxis(rawLX, _calibration.LeftStickDeadzoneInner, _calibration.LeftStickDeadzoneOuter, _calibration.LeftStickSensitivity);
            var normalizedLY = NormalizeAxis(rawLY, _calibration.LeftStickDeadzoneInner, _calibration.LeftStickDeadzoneOuter, _calibration.LeftStickSensitivity);
            var normalizedRX = NormalizeAxis(rawRX, _calibration.RightStickDeadzoneInner, _calibration.RightStickDeadzoneOuter, _calibration.RightStickSensitivity);
            var normalizedRY = NormalizeAxis(rawRY, _calibration.RightStickDeadzoneInner, _calibration.RightStickDeadzoneOuter, _calibration.RightStickSensitivity);

            if (!isConnected)
            {
                // Se o controlador nÃ£o estiver conectado ou ocorrer um erro,
                // retorna um estado padrÃ£o simples (vazio).
                // Poderia tambÃ©m lanÃ§ar uma exceÃ§Ã£o ou ter uma propriedade IsConnected.
                return new GamepadState(); // Retorna um estado "em repouso"
            }
          

            return new GamepadState
            {
                // Usar as constantes definidas em XInputInterop para maior clareza e seguranÃ§a
                ButtonA = (gamepad.wButtons & XInputGamepadA) != 0,
                ButtonB = (gamepad.wButtons & XInputGamepadB) != 0,
                ButtonX = (gamepad.wButtons & XInputGamepadX) != 0,
                ButtonY = (gamepad.wButtons & XInputGamepadY) != 0,

                DPadUp = (gamepad.wButtons & XInputGamepadDpadUp) != 0,
                DPadDown = (gamepad.wButtons & XInputGamepadDpadDown) != 0,
                DPadLeft = (gamepad.wButtons & XInputGamepadDpadLeft) != 0,
                DPadRight = (gamepad.wButtons & XInputGamepadDpadRight) != 0,

                ButtonStart = (gamepad.wButtons & XInputGamepadStart) != 0,
                ButtonBack = (gamepad.wButtons & XInputGamepadBack) != 0,

                ButtonLeftShoulder = (gamepad.wButtons & XInputGamepadLeftShoulder) != 0,
                ButtonRightShoulder = (gamepad.wButtons & XInputGamepadRightShoulder) != 0,

                ThumbLPressed = (gamepad.wButtons & XInputGamepadLeftThumb) != 0,
                ThumbRPressed = (gamepad.wButtons & XInputGamepadRightThumb) != 0,

                // Normaliza os valores dos gatilhos de 0-255 (byte) para 0.0f-1.0f (float)
                TriggerLeft = normLT,
                TriggerRight = normRT,
                    
                // Normaliza os valores dos analÃ³gicos de -32768/32767 (short) para -1.0f-1.0f (float)
                // e aplica uma zona morta (deadzone).
                // A divisÃ£o por 32768f ou 32767f Ã© uma escolha comum para normalizaÃ§Ã£o de short.
                ThumbLX = normalizedLX,
                ThumbLY = normalizedLY, // Inverter Y se necessÃ¡rio
                ThumbRX = normalizedRX,
                ThumbRY =  normalizedRY,  // Inverter Y se necessÃ¡rio
            };
        }

        public GamepadState GetState() => GetState(out _);
        private float NormalizeAxis(short value, float deadzoneInner, float deadzoneOuter, float sensitivity)
        {
            var normalized = Math.Clamp((float)value / 32767f, -1f, 1f);
            var magnitude = Math.Abs(normalized);

            if (magnitude < deadzoneInner)
                return 0f;

            var scaled = (magnitude - deadzoneInner) / (deadzoneOuter - deadzoneInner);
            scaled = Math.Clamp(scaled, 0f, 1f) * Math.Sign(normalized);
            return scaled * sensitivity;
        }
        private float NormalizeTrigger(byte rawValue, float start, float end)
        {
            var value = rawValue / 255f;
            var range = end - start;

            if (range <= 0f) return 0f;
            if (value < start) return 0f;
            if (value > end) return 1f;

            return (value - start) / range;
        }

        /// <summary>
        /// Normaliza o valor de um eixo do analÃ³gico (short) para um float entre -1.0 e 1.0.
        /// </summary>
        /// <param name="value">O valor short do eixo do analÃ³gico (geralmente -32768 a 32767).</param>
        /// <param name="inverted">Opcional. Se verdadeiro, inverte o valor (Ãºtil para eixos Y).</param>
        /// <returns>Um valor float normalizado.</returns>
        private static float NormalizeThumbValue(short value, bool inverted = false)
        {
            var normalizedValue = value / 32768f; // Usar 32768f Ã© comum para cobrir o intervalo completo.
            switch (value)
            {
                // Outra opÃ§Ã£o Ã© Math.Max(-1f, value / 32767f) para garantir -1 a 1.
                case -32768 when !inverted:
                    return -1f; // Caso especial para o valor mÃ­nimo de short
                case -32768 when inverted:
                    return 1f;   // Se invertido
            }

            // Ajuste para garantir que o valor mÃ¡ximo seja 1.0f e o mÃ­nimo -1.0f
            if (normalizedValue > 1.0f) normalizedValue = 1.0f;
            if (normalizedValue < -1.0f) normalizedValue = -1.0f;

            return inverted ? -normalizedValue : normalizedValue;
        }


        /// <summary>
        /// Aplica uma "zona morta" (deadzone) a um valor de entrada do analÃ³gico.
        /// Se o valor absoluto da entrada for menor que a zona morta, retorna 0.
        /// </summary>
        /// <param name="value">O valor de entrada do analÃ³gico (normalizado, entre -1.0 e 1.0).</param>
        /// <param name="deadzone">O limiar da zona morta (ex: 0.1f para 10% de zona morta).</param>
        /// <returns>O valor com a zona morta aplicada, ou 0f se dentro da zona morta.</returns>
        private static float ApplyDeadzone(float value, float deadzone)
        {
            // Garante que deadzone seja positivo
            deadzone = Math.Abs(deadzone);
            return Math.Abs(value) < deadzone ? 0f :
                // Opcional: reescalar o valor para que comece em 0 apÃ³s a zona morta, mapeando [deadzone, 1.0] para [0, 1.0]
                // float remappedValue = (Math.Abs(value) - deadzone) / (1.0f - deadzone);
                // return Math.Sign(value) * remappedValue;
                value; // Retorna o valor original se fora da zona morta (sem reescalonamento)
        }
    }
}
```

## Avalonia\Models\Profile.cs
```csharp
using LiteDB;
using System.ComponentModel;

namespace AvaloniaUI.Models
{
    /// <summary>
    /// Representa um perfil nomeado (ex.: "Default", "FPS", "RPG").
    /// 
    /// Hoje ele nÃ£o faz o mapeamento em si â€” o mapeamento real
    /// Ã© feito pelos arquivos JSON via IMappingStore/JsonMappingStore.
    /// Este modelo pode ser usado apenas para UI/listagem de perfis,
    /// se vocÃª ainda quiser trabalhar com LiteDB.
    /// </summary>
    public class Profile : INotifyPropertyChanged
    {
        [BsonId]
        public string Name { get; set; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

```

## Avalonia\Models\InputMapping.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaUI.Models
{
    public class InputMapping
    {
        public required string InputName { get; set; }
        public required string MappedTo { get; set; }
    }
}
```

## Avalonia\ViewModels\MappingHubViewModel.cs
```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using AvaloniaUI.Hub;
using Avalonia.Threading;
using System.Collections.Generic;

namespace AvaloniaUI.ViewModels
{
    public partial class MappingItemVM : ObservableObject
    {
        private readonly MappingHubViewModel _root;
        private PhysicalInput _assigned;

        public string ActionName { get; }
        public PhysicalInput Assigned
        {
            get => _assigned;
            set
            {
                if (SetProperty(ref _assigned, value))
                {
                    OnPropertyChanged(nameof(AssignedDisplay));
                    OnPropertyChanged(nameof(ConflictNote));
                    OnPropertyChanged(nameof(HasConflict));
                    _root.RefreshConflicts();
                }
            }
        }

        public string AssignedDisplay => Assigned == PhysicalInput.None ? "-" : Assigned.ToString();

        public string ConflictNote
            => Assigned != PhysicalInput.None &&
               _root.Items.Any(x => x != this && x.Assigned == Assigned)
               ? "Duplicado"
               : "";

        public bool HasConflict => !string.IsNullOrEmpty(ConflictNote);

        public PhysicalInput[] AvailableInputs => _root.AvailableInputs;

        public IRelayCommand DetectCommand { get; }
        public IRelayCommand ClearCommand { get; }


        public MappingItemVM(string actionName, PhysicalInput assigned, MappingHubViewModel root)
        {
            ActionName = actionName;
            _assigned = assigned;
            _root = root;

            DetectCommand = new AsyncRelayCommand(DetectAsync);
            ClearCommand = new RelayCommand(() => Assigned = PhysicalInput.None);
        }

        private async Task DetectAsync()
        {
            try
            {
                var ct = _root.CaptureCts?.Token ?? CancellationToken.None;
                var result = await _root.CaptureService.CaptureNextAsync(TimeSpan.FromSeconds(5), ct);
                if (result is { } p) Assigned = p;
            }
            catch { /* silencioso */ }
        }

        internal void RefreshConflictState()
        {
            OnPropertyChanged(nameof(ConflictNote));
            OnPropertyChanged(nameof(HasConflict));
        }
    }

    public partial class MappingHubViewModel : ObservableObject
    {
        [ObservableProperty] private string filter = "";
        [ObservableProperty] private string conflictSummary = "";
        [ObservableProperty] private string newProfileName = "";

        // ? NOVO: lista de perfis + perfil atual
        public ObservableCollection<string> AvailableProfiles { get; } = new();
        [ObservableProperty]
        private string? currentProfileId;

        public ObservableCollection<MappingItemVM> Items { get; } = new();
        public ObservableCollection<MappingItemVM> FilteredItems { get; } = new();

        public PhysicalInput[] AvailableInputs { get; } =
            Enum.GetValues(typeof(PhysicalInput)).Cast<PhysicalInput>().ToArray();

        // Evita loop ao alterar CurrentProfileId internamente (ex.: ao recarregar a lista).
        private bool suppressProfileChangedHandling;

        public IRelayCommand ReloadCommand { get; }
        public IRelayCommand ClearAllCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand NewProfileCommand { get; }
        public IRelayCommand DeleteProfileCommand { get; }

        public bool CanSave => Items.All(i => string.IsNullOrEmpty(i.ConflictNote));
        public bool CanDeleteCurrentProfile => !IsDefaultProfile(CurrentProfileId);

        public IInputCaptureService CaptureService { get; }
        public IMappingStore MappingStore { get; }

        internal CancellationTokenSource? CaptureCts { get; private set; }

        // ? NOVO EVENTO
        public event Action? Saved;

        public MappingHubViewModel(IInputCaptureService captureService, IMappingStore mappingStore)
        {
            CaptureService = captureService;
            MappingStore = mappingStore;

            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Filter)) ApplyFilter();
            };

            ReloadCommand = new AsyncRelayCommand(
    async () =>
    {
        // 1. Carrega do disco em thread de fundo
        await LoadAsync().ConfigureAwait(false);

        // 2. Volta para a UI thread para mexer em bindings/comandos
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // reaplica filtro se houver
            if (!string.IsNullOrWhiteSpace(Filter))
                ApplyFilter();

            // recalcula conflitos (pode disparar NotifyCanExecuteChanged)
            RefreshConflicts();

            // notifica MainViewModel para recarregar MappingEngine/ViGEm
            Saved?.Invoke();
        });
    });


            ClearAllCommand = new RelayCommand(() =>
            {
                foreach (var it in Items) it.Assigned = PhysicalInput.None;
                RefreshConflicts();
            });

            SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
            CancelCommand = new RelayCommand(() => { /* fechar/navegar no host */ });

            NewProfileCommand = new AsyncRelayCommand(CreateNewProfileAsync);
            DeleteProfileCommand = new AsyncRelayCommand(DeleteCurrentProfileAsync, () => CanDeleteCurrentProfile);

            _ = LoadAsync();
        }

        private async Task CreateNewProfileAsync()
        {
            var baseName = GetDesiredProfileName();
            int idx = 1;
            string candidate;

            do
            {
                candidate = $"{baseName}_{idx}";
                idx++;
            } while (IsDefaultProfile(candidate) || AvailableProfiles.Contains(candidate, StringComparer.OrdinalIgnoreCase));

            if (!IsDefaultProfile(baseName) && !AvailableProfiles.Contains(baseName, StringComparer.OrdinalIgnoreCase))
                candidate = baseName;

            // Garante que o arquivo do novo perfil exista (gera defaults se preciso)
            await MappingStore.LoadAsync(candidate, CancellationToken.None);

            suppressProfileChangedHandling = true;
            CurrentProfileId = candidate;
            suppressProfileChangedHandling = false;

            // Recarrega a lista do disco (inclui o novo arquivo)
            await LoadProfilesAsync(CurrentProfileId);

            // zera os itens atuais (vai recarregar com defaults) e aplica em tempo real
            Items.Clear();
            await LoadAsync(refreshProfiles: false, raiseSaved: true);

            NewProfileName = "";
        }

        private async Task DeleteCurrentProfileAsync()
        {
            var profileId = CurrentProfileId;
            if (IsDefaultProfile(profileId))
                return;

            var deleted = await MappingStore.DeleteProfileAsync(profileId, CancellationToken.None);
            if (!deleted)
                return;

            await LoadProfilesAsync();

            if (!string.Equals(CurrentProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                await LoadAsync(refreshProfiles: false, raiseSaved: true);
        }

        private void ApplyFilter()
        {
            var term = (Filter ?? "").Trim();
            FilteredItems.Clear();

            var query = string.IsNullOrEmpty(term)
                ? Items
                : Items.Where(i => i.ActionName.Contains(term, StringComparison.OrdinalIgnoreCase));

            foreach (var i in query) FilteredItems.Add(i);
        }

        public void RefreshConflicts()
        {
            var dups = Items
                .Where(i => i.Assigned != PhysicalInput.None)
                .GroupBy(i => i.Assigned)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var item in Items)
            {
                item.RefreshConflictState();
            }

            ConflictSummary = dups.Count == 0
                ? "Sem conflitos"
                : $"Conflitos: {string.Join(", ", dups.Select(g => $"{g.Key} x{g.Count()}"))}";

            OnPropertyChanged(nameof(CanSave));
            (SaveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }

        partial void OnCurrentProfileIdChanged(string? value)
        {
            OnPropertyChanged(nameof(CanDeleteCurrentProfile));
            (DeleteProfileCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();

            if (suppressProfileChangedHandling)
                return;

            // Selecao de perfil via UI: recarrega o mapeamento para esse perfil
            _ = LoadAsync(refreshProfiles: false, raiseSaved: true);
        }

        private async Task LoadProfilesAsync(string? preferredProfileId = null)
        {
            var desired = preferredProfileId ?? CurrentProfileId;

            AvailableProfiles.Clear();
            var profiles = await MappingStore.ListProfilesAsync(CancellationToken.None);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in profiles)
            {
                if (seen.Add(p))
                    AvailableProfiles.Add(p);
            }

            string? next = null;
            if (!string.IsNullOrWhiteSpace(desired) &&
                AvailableProfiles.Contains(desired, StringComparer.OrdinalIgnoreCase))
            {
                next = desired;
            }
            else if (AvailableProfiles.Count > 0)
            {
                next = AvailableProfiles[0];
            }

            if (!string.Equals(CurrentProfileId, next, StringComparison.Ordinal))
            {
                suppressProfileChangedHandling = true;
                CurrentProfileId = next;
                suppressProfileChangedHandling = false;
            }
        }

        private async Task LoadAsync(bool refreshProfiles = true, bool raiseSaved = false)
        {
            CaptureCts?.Cancel();
            CaptureCts = new CancellationTokenSource();

            if (refreshProfiles || AvailableProfiles.Count == 0)
                await LoadProfilesAsync(CurrentProfileId);

            if (string.IsNullOrWhiteSpace(CurrentProfileId))
                return;

            var actions = MappingStore.GetDefaultActions();
            Dictionary<string, PhysicalInput> loaded;

            try
            {
                loaded = new Dictionary<string, PhysicalInput>(StringComparer.OrdinalIgnoreCase);
                foreach (var (action, assigned) in await MappingStore.LoadAsync(CurrentProfileId, CaptureCts.Token))
                {
                    // Ultimo binding para a mesma acao vence (evita excecao de chave duplicada)
                    loaded[action] = assigned;
                }
            }
            catch
            {
                // Se der falha (arquivo bloqueado/corrompido), mantem a UI funcional com defaults
                loaded = new Dictionary<string, PhysicalInput>(StringComparer.OrdinalIgnoreCase);
            }

            // Prepara lista fora do thread de UI
            var newItems = actions
                .Select(a =>
                {
                    loaded.TryGetValue(a, out var phys);
                    return new MappingItemVM(a, phys, this);
                })
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items.Clear();
                FilteredItems.Clear();

                foreach (var item in newItems)
                    Items.Add(item);

                ApplyFilter();
                RefreshConflicts();
            }, DispatcherPriority.Background);

            if (raiseSaved)
                Saved?.Invoke();
        }

        private async Task SaveAsync()
        {
            var map = Items.Select(i => (i.ActionName, i.Assigned)).ToArray();

            // salva no perfil atual (pode ser null => "mapping.json" default)
            await MappingStore.SaveAsync(CurrentProfileId, map, CancellationToken.None);

            // atualiza lista e garante que o perfil atual continua selecionado
            await LoadProfilesAsync(CurrentProfileId);

            // avisa o MainViewModel pra recarregar o engine
            Saved?.Invoke();
        }

        private static bool IsDefaultProfile(string? profileId)
        {
            return string.IsNullOrWhiteSpace(profileId)
                || profileId.Equals("mapping", StringComparison.OrdinalIgnoreCase)
                || profileId.Equals("default", StringComparison.OrdinalIgnoreCase);
        }

        private string GetDesiredProfileName()
        {
            var raw = (NewProfileName ?? "").Trim();
            if (raw.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                raw = System.IO.Path.GetFileNameWithoutExtension(raw);

            var sanitized = string.Join("_",
                raw.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            if (string.IsNullOrWhiteSpace(sanitized))
                return "perfil";

            return sanitized;
        }
    }
}



```

## Avalonia\ViewModels\MainViewModel.cs
```csharp
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
        private readonly Dictionary<string, double> _currentInputMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _virtualOutputMap = new(StringComparer.OrdinalIgnoreCase);
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
        public IReadOnlyDictionary<string, double> CurrentInputMap => _currentInputMap;
        public IReadOnlyDictionary<string, double> VirtualOutputMap => _virtualOutputMap;

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
                : $"Controle f¡sico ativo: {friendly}";

            if (!_currentDeviceLikelyVirtual)
                AutoAddPhysicalToHideList(info);
        }

        private void AutoAddPhysicalToHideList(PhysicalDeviceInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.Path))
                return;

            // evita duplicar e nunca adiciona o virtual
            if (_currentDeviceLikelyVirtual || DevicesToHide.Contains(info.Path))
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (!DevicesToHide.Contains(info.Path))
                    DevicesToHide.Add(info.Path);
            });
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

                    _virtualOutputMap[name] = value;
                }

                OnPropertyChanged(nameof(VirtualOutputMap));
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

                _currentInputMap[name] = val;
            }

            OnPropertyChanged(nameof(CurrentInputMap));
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





```

## Avalonia\ViewModels\DiagnosticsGamepadViewModel.cs
```csharp
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

```

## Infrastructure\Adapters\Outputs\ViGEmOutput.cs
```csharp
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

```

## Infrastructure\Adapters\Outputs\ConsoleOutput.cs
```csharp
using Core.Events.Outputs;
using Core.Interfaces;

namespace Infrastructure.Adapters.Outputs
{
    /// <summary>
    /// ImplementaÃ§Ã£o de <see cref="IOutputService"/> que escreve as saÃ­das mapeadas
    /// para a consola do sistema. Ãštil para fins de depuraÃ§Ã£o ou aplicaÃ§Ãµes simples.
    /// </summary>
    public class ConsoleOutput : IOutputService
    {
        private readonly string _format;
        private bool _connected = false;

        /// <summary>
        /// Inicializa um novo ConsoleOutput, com formataÃ§Ã£o opcional de valor.
        /// </summary>
        /// <param name="format">Formato numÃ©rico para o valor, padrÃ£o Ã© "F4".</param>
        public ConsoleOutput(string format = "F4")
        {
            _format = format;
        }

        public void Connect()
        {
            _connected = true;
            Console.WriteLine("[ConsoleOutput] Conectado.");
        }

        public void Disconnect()
        {
            _connected = false;
            Console.WriteLine("[ConsoleOutput] Desconectado.");
        }

        public bool IsConnected => _connected;

        /// <summary>
        /// Aplica a saÃ­da mapeada escrevendo o seu nome e valor na consola.
        /// </summary>
        /// <param name="output">A <see cref="MappedOutput"/> a ser processada.</param>
        public void Apply(MappedOutput output)
        {
            Console.WriteLine($"[ConsoleOutput] Apply: {output.OutputName} = {output.Value}");
        }
        public void ApplyAll(Dictionary<string, float> outputState)
        {
            Console.WriteLine("[ConsoleOutput] ApplyAll:");
            foreach (var kvp in outputState)
                Console.WriteLine($"  {kvp.Key} = {kvp.Value}");
        }

    

    }
}

```

## Infrastructure\Adapters\Outputs\AxisUtils.cs
```csharp
namespace Infrastructure.Adapters.Outputs
{
    /// <summary>
    /// Helper utilities for axis conversions used by output adapters.
    /// </summary>
    internal static class AxisUtils
    {
        /// <summary>
        /// Converts a normalized float in range [-1,1] to a short value in
        /// [-32768,32767] as expected by ViGEm/DirectInput/XInput APIs.
        /// </summary>
        public static short FloatToShort(float value)
        {
            value = Math.Clamp(value, -1f, 1f);
            return value >= 0f
                ? (short)(value * 32767)
                : (short)(value * 32768);
        }

        /// <summary>
        /// Maps a circular stick vector to a square response while preserving radius.
        /// This boosts diagonals for games that interpret stick input as square.
        /// </summary>
        public static (float x, float y) CircleToSquare(float x, float y)
        {
            var max = MathF.Max(MathF.Abs(x), MathF.Abs(y));
            if (max <= 0f)
                return (0f, 0f);

            var r = MathF.Sqrt(x * x + y * y);
            if (r <= 0f)
                return (0f, 0f);

            var scale = r / max;
            var nx = Math.Clamp(x * scale, -1f, 1f);
            var ny = Math.Clamp(y * scale, -1f, 1f);
            return (nx, ny);
        }
    }
}

```

## GlobalUsings.cs
```csharp
global using Xunit;
```

## Avalonia\Hub\IMappingStore.cs
```csharp
// AvaloniaUI.Hub/IMappingStore.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaUI.Hub
{
    public interface IMappingStore
    {
        string[] GetDefaultActions();

        Task<(string action, PhysicalInput assigned)[]> LoadAsync(
            string? profileId,
            CancellationToken ct);

        Task SaveAsync(
            string? profileId,
            (string action, PhysicalInput assigned)[] map,
            CancellationToken ct);

        Task<string[]> ListProfilesAsync(CancellationToken ct);

        Task<bool> DeleteProfileAsync(string? profileId, CancellationToken ct);
    }
}

```

## Avalonia\Hub\HubServices.cs
```csharp
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

```

## Avalonia\Services\SdlManager.cs
```csharp
using System;
using ApplicationLayer.Services;
using Avalonia;
using Avalonia.Logging;
using AvaloniaUI;
using AvaloniaUI.Hub;
using AvaloniaUI.Services;
using Core.Interfaces;
using Infrastructure.HidHide;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDL;
using static SDL.SDL3;

public sealed class SdlManager
{
    private static readonly Lazy<SdlManager> _instance = new(() => new SdlManager());
    public static SdlManager Instance => _instance.Value;

    private bool _initialized = false;

    private SdlManager() { }

    public void Initialize()
    {
        if (_initialized)
            return;

        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        SDL_SetHint(SDL_HINT_AUTO_UPDATE_JOYSTICKS, "1");
        SDL_SetHint(SDL_HINT_JOYSTICK_THREAD, "1");

        SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD | SDL_InitFlags.SDL_INIT_JOYSTICK);
        _initialized = true;
    }
}

```

## Avalonia\Services\RawVirtualizationRunner.cs
```csharp
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
    /// Bridge headless que pega snapshots fÃ­sicos (SDL) e aplica na saÃ­da ViGEm.
    /// Usado quando a app Ã© iniciada com --raw.
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

            Console.WriteLine("[RAW] Capturando entradas fÃ­sicas e emitindo via ViGEm. Ctrl+C para sair.");

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
                // CÃ³pia leve para nÃ£o bloquear o loop de captura nem depender
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

```

## Avalonia\Services\JsonMappingStore.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaUI.Hub;

namespace AvaloniaUI.Services
{
    /// <summary>
    /// Implementacao de IMappingStore que armazena mapeamentos em arquivos JSON
    /// na pasta de dados da aplicacao.
    /// </summary>
    public sealed class JsonMappingStore : IMappingStore
    {
        private readonly string _dir;
        private readonly string _defaultPath;

        // Opcoes de serializacao JSON: indentado para leitura humana, camelCase para propriedades.
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Inicializa uma nova instancia do JsonMappingStore.
        /// </summary>
        /// <param name="filePath">Caminho de arquivo opcional para o mapeamento padrao. Se nulo, usa o caminho padrao.</param>
        public JsonMappingStore(string? filePath = null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _dir = Path.Combine(appData, "NirvanaRemap");

            // Garante que o diretorio exista.
            Directory.CreateDirectory(_dir);

            _defaultPath = filePath ?? Path.Combine(_dir, "mapping.json");
        }

        // ----------------------------------------------------------
        // Acoes Padrao
        // ----------------------------------------------------------

        /// <summary>
        /// Obtem a lista de nomes de acoes padrao que o sistema reconhece.
        /// </summary>
        /// <returns>Um array de strings com os nomes das acoes.</returns>
        public string[] GetDefaultActions()
        {
            return GetBuiltInDefaultMapping()
                .Select(x => x.action)
                .Distinct()
                .ToArray();
        }



        private static (string action, PhysicalInput assigned)[] GetBuiltInDefaultMapping()
        {
            return new[]
            {
        // BOTOES
        ("ButtonA", PhysicalInput.ButtonSouth),
        ("ButtonB", PhysicalInput.ButtonEast),
        ("ButtonX", PhysicalInput.ButtonWest),
        ("ButtonY", PhysicalInput.ButtonNorth),

        ("ButtonLeftShoulder", PhysicalInput.LeftBumper),
        ("ButtonRightShoulder", PhysicalInput.RightBumper),

        ("ButtonStart", PhysicalInput.Start),
        ("ButtonBack", PhysicalInput.Back),

        ("ThumbLPressed", PhysicalInput.LeftStickClick),
        ("ThumbRPressed", PhysicalInput.RightStickClick),

        ("DPadUp", PhysicalInput.DPadUp),
        ("DPadDown", PhysicalInput.DPadDown),
        ("DPadLeft", PhysicalInput.DPadLeft),
        ("DPadRight", PhysicalInput.DPadRight),

        // TRIGGERS
        ("TriggerLeft", PhysicalInput.LeftTrigger),
        ("TriggerRight", PhysicalInput.RightTrigger),

        // STICKS analogicos (mapeia ambos os lados para o mesmo eixo)
        ("ThumbLX", PhysicalInput.LeftStickX_Pos),
        ("ThumbLX", PhysicalInput.LeftStickX_Neg),
        ("ThumbLY", PhysicalInput.LeftStickY_Pos),
        ("ThumbLY", PhysicalInput.LeftStickY_Neg),

        ("ThumbRX", PhysicalInput.RightStickX_Pos),
        ("ThumbRX", PhysicalInput.RightStickX_Neg),
        ("ThumbRY", PhysicalInput.RightStickY_Pos),
        ("ThumbRY", PhysicalInput.RightStickY_Neg)
    };
        }
        // ----------------------------------------------------------
        // Listagem de Perfis
        // ----------------------------------------------------------

        /// <summary>
        /// Lista os perfis de mapeamento disponiveis (ate 5 arquivos JSON, excluindo o default).
        /// </summary>
        /// <param name="ct">Token de cancelamento.</param>
        /// <returns>Um Task contendo um array de IDs de perfil (nomes de arquivo sem extensao).</returns>
        public Task<string[]> ListProfilesAsync(CancellationToken ct)
        {
            // Pega todos os arquivos .json na pasta.
            var files = Directory
                .GetFiles(_dir, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => !IsDefaultProfile(name))
                .ToList();

            // Garante que o perfil "mapping" (default) esteja sempre na primeira posicao da lista
            // e seja chamado de "mapping" ou "default".
            var defaultProfileName = "mapping";
            files.Insert(0, defaultProfileName);

            // Remove duplicatas preservando a ordem (default primeiro)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = files.Where(name => seen.Add(name)).ToArray();

            return Task.FromResult(ordered);
        }

        public Task<bool> DeleteProfileAsync(string? profileId, CancellationToken ct)
        {
            if (IsDefaultProfile(profileId))
                return Task.FromResult(false);

            ct.ThrowIfCancellationRequested();

            var path = ResolvePath(profileId);
            if (!File.Exists(path))
                return Task.FromResult(false);

            try
            {
                File.Delete(path);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        // ----------------------------------------------------------
        // Helpers de Caminho
        // ----------------------------------------------------------

        /// <summary>
        /// Resolve o ID do perfil para um caminho de arquivo seguro.
        /// </summary>
        /// <param name="profileId">O ID do perfil (nome de arquivo).</param>
        /// <returns>O caminho de arquivo completo para o perfil.</returns>
        private string ResolvePath(string? profileId)
        {
            // Trata IDs vazios ou "default/mapping" como o caminho padrao.
            if (IsDefaultProfile(profileId))
            {
                return _defaultPath;
            }

            // Sanitiza o nome de arquivo para remover caracteres invalidos.
            var safe = string.Join("_", profileId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            // Caso o nome sanitizado fique vazio.
            if (string.IsNullOrWhiteSpace(safe))
            {
                safe = "mapping_alt";
            }

            return Path.Combine(_dir, safe + ".json");
        }

        private static bool IsDefaultProfile(string? profileId)
        {
            return string.IsNullOrWhiteSpace(profileId)
                || profileId.Equals("mapping", StringComparison.OrdinalIgnoreCase)
                || profileId.Equals("default", StringComparison.OrdinalIgnoreCase);
        }

        // ----------------------------------------------------------
        // Load / Save com Perfil
        // ----------------------------------------------------------

        /// <summary>
        /// Carrega o mapeamento de um perfil especifico de forma assincrona.
        /// </summary>
        /// <param name="profileId">O ID do perfil a carregar.</param>
        /// <param name="ct">Token de cancelamento.</param>
        /// <returns>Um array de tuplas (acao, input fisico) do mapeamento carregado.</returns>
        public async Task<(string action, PhysicalInput assigned)[]> 
        LoadAsync(
                string? profileId,
                CancellationToken ct
                 )
        {
            var path = ResolvePath(profileId);

            // 1) Se nao existe, gera default e salva
            if (!File.Exists(path))
            {
                var def = GetBuiltInDefaultMapping();
                await SaveAsync(profileId, def, ct);
                return def;
            }

            try
            {
                List<Entry> data;
                await using (var fs = File.OpenRead(path))
                {
                    data = await JsonSerializer
                        .DeserializeAsync<List<Entry>>(fs, _json, ct)
                        .ConfigureAwait(false) ?? new();
                }

                var migrated = MigrateLegacyActions(data);
                var axisCompleted = EnsureAxisPairs(migrated.entries);
                var defaultsCompleted = MergeWithDefaults(axisCompleted.entries);

                if (migrated.changed || axisCompleted.changed || defaultsCompleted.changed)
                {
                    var tuples = defaultsCompleted.entries
                        .Select(e => (e.Action, e.Assigned))
                        .ToArray();

                    // Atualiza o arquivo para evitar reprocessamento futuro
                    await SaveAsync(profileId, tuples, ct);
                    return tuples;
                }

                return data.Select(e => (e.Action, e.Assigned)).ToArray();
            }
            catch (JsonException)
            {
                // 2) JSON zoado: faz backup e recria o default
                try
                {
                    var backup = path + ".bak";
                    if (File.Exists(backup))
                        File.Delete(backup);
                    File.Move(path, backup);
                }
                catch
                {
                    // se falhar o backup, ignora e segue
                }

                var def = GetBuiltInDefaultMapping();
                await SaveAsync(profileId, def, ct);
                return def;
            }
            catch
            {
                // 3) Qualquer outro erro (IO/lock): usa default em mem?ria para manter a UI funcional
                var def = GetBuiltInDefaultMapping();
                try
                {
                    await SaveAsync(profileId, def, ct);
                }
                catch
                {
                    // ignora falha de grava??o
                }
                return def;
            }
        }

        /// <summary>
        /// Salva o mapeamento fornecido para um perfil especifico de forma assincrona.
        /// </summary>
        /// <param name="profileId">O ID do perfil onde salvar.</param>
        /// <param name="map">O array de tuplas (acao, input fisico) a ser salvo.</param>
        /// <param name="ct">Token de cancelamento.</param>
        public async Task SaveAsync(
            string? profileId,
            (string action, PhysicalInput assigned)[] map,
            CancellationToken ct)
        {
            var path = ResolvePath(profileId);

            // Normaliza eixos para sempre salvar ambos os lados (pos/neg),
            // evitando inversoes a cada carregamento.
            var normalized = NormalizeAxisEntries(map);

            // Converte o array de tuplas para uma lista de objetos Entry para serializacao.
            var list = new List<Entry>(normalized.Count);
            foreach (var (a, p) in normalized)
            {
                list.Add(new Entry { Action = a, Assigned = p });
            }

            // Cria/sobrescreve o arquivo. O 'using var' garante o descarte.
            await using var fs = File.Create(path);
            await JsonSerializer.SerializeAsync(fs, list, _json, ct);
        }

        // ----------------------------------------------------------
        // Classe auxiliar de serializacao
        // ----------------------------------------------------------

        /// <summary>
        /// Representa uma unica entrada de mapeamento para fins de serializacao/desserializacao JSON.
        /// </summary>
        private sealed class Entry
        {
            public string Action { get; set; } = "";
            public PhysicalInput Assigned { get; set; } = PhysicalInput.None;
        }

        // Converte nomes antigos de acoes de eixo (LX+/LX-/...) para os novos
        // nomes continuos (ThumbLX/ThumbLY/...), preservando compatibilidade.
        private static (bool changed, List<Entry> entries) MigrateLegacyActions(List<Entry> data)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LX+"] = "ThumbLX",
                ["LX-"] = "ThumbLX",
                ["LY+"] = "ThumbLY",
                ["LY-"] = "ThumbLY",
                ["RX+"] = "ThumbRX",
                ["RX-"] = "ThumbRX",
                ["RY+"] = "ThumbRY",
                ["RY-"] = "ThumbRY",
            };

            var changed = false;
            var result = new List<Entry>(data.Count);

            foreach (var e in data)
            {
                var action = map.TryGetValue(e.Action, out var newer)
                    ? newer
                    : e.Action;

                if (!changed && !string.Equals(action, e.Action, StringComparison.Ordinal))
                    changed = true;

                result.Add(new Entry
                {
                    Action = action,
                    Assigned = e.Assigned
                });
            }

            return (changed, result);
        }

        // Garante que, se um eixo analogico estiver presente, ambos os lados (pos/neg)
        // tenham entradas, evitando que apenas direita/baixo funcionem.
        private static (bool changed, List<Entry> entries) EnsureAxisPairs(List<Entry> data)
        {
            var pairs = new (string action, PhysicalInput pos, PhysicalInput neg)[]
            {
                ("ThumbLX", PhysicalInput.LeftStickX_Pos, PhysicalInput.LeftStickX_Neg),
                ("ThumbLY", PhysicalInput.LeftStickY_Pos, PhysicalInput.LeftStickY_Neg),
                ("ThumbRX", PhysicalInput.RightStickX_Pos, PhysicalInput.RightStickX_Neg),
                ("ThumbRY", PhysicalInput.RightStickY_Pos, PhysicalInput.RightStickY_Neg),
            };

            var changed = false;
            var list = new List<Entry>(data);

            foreach (var (action, pos, neg) in pairs)
            {
                bool hasPos = list.Any(e => string.Equals(e.Action, action, StringComparison.OrdinalIgnoreCase) && e.Assigned == pos);
                bool hasNeg = list.Any(e => string.Equals(e.Action, action, StringComparison.OrdinalIgnoreCase) && e.Assigned == neg);

                // So corrige se o eixo ja esta mapeado para algum lado (evita recriar se o usuario limpou ambos)
                if (!(hasPos || hasNeg))
                    continue;

                if (!hasPos)
                {
                    list.Add(new Entry { Action = action, Assigned = pos });
                    changed = true;
                }

                if (!hasNeg)
                {
                    list.Add(new Entry { Action = action, Assigned = neg });
                    changed = true;
                }
            }

            return (changed, list);
        }

        // Garante que todas as acoes padrao tenham algum binding (default completo).
        // Se um perfil existente estiver faltando acoes, preenche com o mapeamento built-in.
        private static (bool changed, List<Entry> entries) MergeWithDefaults(List<Entry> data)
        {
            var changed = false;
            var dict = new Dictionary<string, PhysicalInput>(StringComparer.OrdinalIgnoreCase);

            // preserva ultimo binding encontrado para cada acao, ignorando entradas None (considera como ausente)
            foreach (var e in data)
            {
                if (e.Assigned == PhysicalInput.None)
                {
                    changed = true; // vamos preencher com default
                    continue;
                }

                dict[e.Action] = e.Assigned;
            }

            foreach (var (action, phys) in GetBuiltInDefaultMapping())
            {
                if (!dict.ContainsKey(action))
                {
                    dict[action] = phys;
                    changed = true;
                }
            }

            var result = dict.Select(kv => new Entry { Action = kv.Key, Assigned = kv.Value }).ToList();
            return (changed, result);
        }

        // ----------------------------------------------------------
        // Normalizacao de eixos antes de salvar
        // ----------------------------------------------------------

        private static bool IsAxisAction(string action) =>
            string.Equals(action, "ThumbLX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbLY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbRX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbRY", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetAxisPair(PhysicalInput input, out PhysicalInput counterpart)
        {
            switch (input)
            {
                case PhysicalInput.LeftStickX_Pos:
                    counterpart = PhysicalInput.LeftStickX_Neg; return true;
                case PhysicalInput.LeftStickX_Neg:
                    counterpart = PhysicalInput.LeftStickX_Pos; return true;
                case PhysicalInput.LeftStickY_Pos:
                    counterpart = PhysicalInput.LeftStickY_Neg; return true;
                case PhysicalInput.LeftStickY_Neg:
                    counterpart = PhysicalInput.LeftStickY_Pos; return true;
                case PhysicalInput.RightStickX_Pos:
                    counterpart = PhysicalInput.RightStickX_Neg; return true;
                case PhysicalInput.RightStickX_Neg:
                    counterpart = PhysicalInput.RightStickX_Pos; return true;
                case PhysicalInput.RightStickY_Pos:
                    counterpart = PhysicalInput.RightStickY_Neg; return true;
                case PhysicalInput.RightStickY_Neg:
                    counterpart = PhysicalInput.RightStickY_Pos; return true;
                default:
                    counterpart = default;
                    return false;
            }
        }

        private static List<(string action, PhysicalInput assigned)> NormalizeAxisEntries(
            (string action, PhysicalInput assigned)[] map)
        {
            var result = new List<(string, PhysicalInput)>();

            void AddIfMissing(string action, PhysicalInput input)
            {
                if (!result.Any(e =>
                        string.Equals(e.Item1, action, StringComparison.OrdinalIgnoreCase) &&
                        e.Item2 == input))
                {
                    result.Add((action, input));
                }
            }

            foreach (var (action, assigned) in map)
            {
                if (IsAxisAction(action) && TryGetAxisPair(assigned, out var other))
                {
                    // Deixa o lado selecionado por ultimo para que ele prevaleca na UI
                    AddIfMissing(action, other);
                    AddIfMissing(action, assigned);
                }
                else
                {
                    AddIfMissing(action, assigned);
                }
            }

            return result;
        }
    }
}


```

## Avalonia\Services\GamepadRemapService.cs
```csharp
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
        private Dictionary<string, double> _frame =
            new(StringComparer.Ordinal);
        private Dictionary<string, double> _emitBuffer =
            new(StringComparer.Ordinal);

        private const int PollIntervalMs = 8;       // ~120 Hz para reduzir latÛncia
        private const int MinBatchIntervalMs = 2;   // evita tempestade de eventos
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
            _emitBuffer.Clear();

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
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            var sw = Stopwatch.StartNew();
            long nextTick = PollIntervalMs;

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
                    // Nenhum device valido no momento
                    // (ConnectionChanged(false) ja e tratado em OpenFirstPad)
                }

                FlushBatch(); // dispara InputBatch

                var remaining = nextTick - sw.ElapsedMilliseconds;
                if (remaining > 0)
                {
                    if (token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(remaining)))
                    {
                        break;
                    }
                }
                else
                {
                    // re-sincroniza quando o loop ficar atrasado
                    nextTick = sw.ElapsedMilliseconds;
                }

                nextTick += PollIntervalMs;
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

            var snap = _emitBuffer;
            _emitBuffer = _frame;
            _frame = snap;
            _frame.Clear();

#if DEBUG
            Debug.WriteLine("[BATCH] " + string.Join(", ", _emitBuffer));
#endif

            InputBatch?.Invoke(_emitBuffer);
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














```

## Avalonia\Converters\ValueToWidthConverter.cs
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaUI.Converters;

public class ValueToWidthConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 &&
            values[0] is double value &&
            values[1] is double maxWidth &&
            !double.IsNaN(maxWidth) &&
            !double.IsInfinity(maxWidth))
        {
            return Math.Max(0, value * maxWidth);
        }

        return 0d;
    }
}

```

## Avalonia\Converters\StickOffsetConverter.cs
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AvaloniaUI.Converters;

/// <summary>
/// Converts LX/LY (or RX/RY) values into a TranslateTransform to move the thumb indicator.
/// </summary>
public class StickOffsetConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double x = Extract(values, 0);
        double y = Extract(values, 1);

        var radius = 14.0;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            radius = parsed;

        x = Math.Clamp(x, -1, 1) * radius;
        // SDL already reports Y increasing when the stick is pushed down.
        // Keep the sign as-is so the UI reflects the emitted direction.
        y = Math.Clamp(y, -1, 1) * radius;

        return new TranslateTransform(x, y);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;

    private static double Extract(IList<object?> values, int index)
    {
        if (index >= values.Count)
            return 0;

        return values[index] switch
        {
            double d => d,
            float f => f,
            _ => 0
        };
    }
}

```

## Avalonia\Converters\SelectedProfileColorConverter.cs
```csharp
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AvaloniaUI.Converters;

public class SelectedProfileColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.Parse("#3c8dbc"))
            : new SolidColorBrush(Color.Parse("#1e1e1e"));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

```

## Avalonia\Converters\ProfileActionTextConverter.cs
```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using AvaloniaUI.Models;
using System.Linq;
using Avalonia.Controls;

namespace AvaloniaUI.Converters
{
    public class ProfileActionTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Profile profile)
                return "";

            var itemsControl = parameter as ItemsControl;
            if (itemsControl?.Items is System.Collections.IEnumerable profiles)
            {
                var first = profiles.Cast<Profile>().FirstOrDefault();
                return profile == first ? "+" : "â€“";
            }

            return profile.Name == "PadrÃ£o" ? "+" : "â€“";
        }


        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

```

## Avalonia\Converters\InputValueConverter.cs
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using AvaloniaUI.ViewModels;

namespace AvaloniaUI.Converters;

/// <summary>
/// Lookup helper: given a collection of InputStatus and a name, returns the value (or 0).
/// </summary>
public class InputValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string name || string.IsNullOrWhiteSpace(name))
            return 0d;

        switch (value)
        {
            case IEnumerable<InputStatus> list:
                return list.FirstOrDefault(i => name.Equals(i.Name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0d;
            case IReadOnlyDictionary<string, double> dict:
                return dict.TryGetValue(name, out var v) ? v : 0d;
            default:
                return 0d;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Returns an opacity/intensity level for a given input name.
/// Digital: 1 when pressed, otherwise BaseLevel.
/// Analog: BaseLevel..1 scaled by absolute value.
/// </summary>
public class InputLevelConverter : IValueConverter
{
    private const double BaseLevel = 0.18;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string param || string.IsNullOrWhiteSpace(param))
            return BaseLevel;

        var parts = param.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];
        var mode = parts.Length > 1 ? parts[1] : "digital";

        double val = 0.0;
        switch (value)
        {
            case IEnumerable<InputStatus> list:
                val = list.FirstOrDefault(i => name.Equals(i.Name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0.0;
                break;
            case IReadOnlyDictionary<string, double> dict:
                val = dict.TryGetValue(name, out var v) ? v : 0.0;
                break;
        }

        var abs = Math.Clamp(Math.Abs(val), 0.0, 1.0);

        var isAnalog = string.Equals(mode, "analog", StringComparison.OrdinalIgnoreCase);
        if (isAnalog)
            return BaseLevel + abs * (1.0 - BaseLevel);

        return val >= 0.5 ? 1.0 : BaseLevel;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

```

## Avalonia\Converters\GamepadConnectionTextConverter.cs
```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaUI.Converters;

public class GamepadConnectionTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // trate nulos explicitamente
        if (value is not bool connected)
            return "Aguardando controle fÃ­sico";

        return connected
            ? "Controle fÃ­sico conectado"
            : "Aguardando controle fÃ­sico";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

```

## Core\Services\SimpleMapping.cs
```csharp
using Core.Events.Inputs;
using Core.Events.Outputs;
using Core.Interfaces; // NecessÃ¡rio para IMapping

namespace Core.Services;

    /// <summary>
    /// Representa uma regra de mapeamento simples que compara o nome da entrada do controlador
    /// com um nome de entrada esperado e, se corresponder, mapeia para um nome de saÃ­da especificado.
    /// </summary>
    /// <param name="inputName">O nome da entrada do controlador que este mapeamento deve corresponder.</param>
    /// <param name="outputName">O nome da saÃ­da para a qual a entrada serÃ¡ mapeada se houver correspondÃªncia.</param>
    
    public class SimpleMapping(string inputName, string outputName) : IMapping // Adicionada a implementaÃ§Ã£o da interface
    {
        // inputName e outputName sÃ£o capturados pelo construtor primÃ¡rio
        // e podem ser acedidos dentro da classe. Se precisares deles
        // como propriedades pÃºblicas, terias que declarÃ¡-los explicitamente:
        // public string InputName { get; } = inputName;
        // public string OutputName { get; } = outputName;

        /// <summary>
        /// Nome da entrada que este mapeamento verifica.
        /// </summary>
        public string InputName { get; } = inputName;

        /// <summary>
        /// Nome da saÃ­da que serÃ¡ gerada em caso de correspondÃªncia.
        /// </summary>
        public string OutputName { get; } = outputName;

        /// <summary>
        /// Verifica se o nome da entrada do controlador fornecida corresponde ao <c>inputName</c> esperado.
        /// </summary>
        /// <param name="input">O evento de entrada do controlador a ser verificado.</param>
        /// <returns><c>true</c> se o <see cref="ControllerInput.InputName"/> da entrada corresponder ao <c>inputName</c> configurado para este mapeamento; caso contrÃ¡rio, <c>false</c>.</returns>
        
        public bool Matches(ControllerInput input) => input.Name == InputName;

        /// <summary>
        /// Cria uma <see cref="MappedOutput"/> com o <c>outputName</c> configurado e o valor da entrada original.
        /// </summary>
        /// <param name="input">O evento de entrada do controlador a ser mapeado.</param>
        /// <returns>Um novo <see cref="MappedOutput"/> com o nome de saÃ­da especificado e o valor da entrada.</returns>
        public MappedOutput Map(ControllerInput input) => new(OutputName, input.Value);
    }

```

## Core\Services\ProfileMapping.cs
```csharp
using System.Collections.Generic; // Para List e IEnumerable
using System.Linq; // Para LINQ (Where, Select)
using Core.Events.Inputs;
using Core.Events.Outputs;
using Core.Interfaces;

namespace Core.Services
{
    /// <summary>
    /// Gere uma coleÃ§Ã£o de regras de mapeamento (<see cref="IMapping"/>) para um perfil especÃ­fico
    /// e aplica essas regras a uma entrada de controlador.
    /// </summary>
    public class ProfileMapping
    {
        private readonly List<IMapping> _mappings = new();

        /// <summary>
        /// Adiciona uma regra de mapeamento Ã  coleÃ§Ã£o do perfil.
        /// </summary>
        /// <param name="mapping">A regra de mapeamento (<see cref="IMapping"/>) a ser adicionada.</param>
        /// <remarks>
        /// Anteriormente, este mÃ©todo aceitava 'SimpleMapping'. Foi generalizado para aceitar qualquer 'IMapping'
        /// para maior flexibilidade e para alinhar com o tipo da lista interna '_mappings'.
        /// </remarks>
        public void AddMapping(IMapping mapping) // Alterado de SimpleMapping para IMapping
        {
            _mappings.Add(mapping);
        }

        /// <summary>
        /// Aplica todas as regras de mapeamento configuradas Ã  entrada do controlador fornecida.
        /// </summary>
        /// <param name="input">O evento de entrada do controlador a ser processado.</param>
        /// <returns>
        /// Uma coleÃ§Ã£o de <see cref="MappedOutput"/> resultante dos mapeamentos que corresponderam Ã  entrada.
        /// Pode ser vazia se nenhuma regra de mapeamento corresponder.
        /// </returns>
        public IEnumerable<MappedOutput> Apply(ControllerInput input)
        {
            return _mappings
                .Where(m => m.Matches(input)) // Filtra apenas os mapeamentos que correspondem
                .Select(m => m.Map(input));    // Transforma as correspondÃªncias em saÃ­das mapeadas
        }

        // Opcional: MÃ©todo para limpar mapeamentos ou obter os mapeamentos atuais, se necessÃ¡rio.
        /// <summary>
        /// Remove todos os mapeamentos deste perfil.
        /// </summary>
        public void ClearMappings()
        {
            _mappings.Clear();
        }

        /// <summary>
        /// ObtÃ©m uma cÃ³pia somente leitura da lista atual de mapeamentos.
        /// </summary>
        /// <returns>Uma lista somente leitura de <see cref="IMapping"/>.</returns>
        public IReadOnlyList<IMapping> GetMappings()
        {
            return _mappings.AsReadOnly();
        }
    }
}
```

## Avalonia\App.axaml.cs
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaUI
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var services = Program.Services;

                var mainVm = services.GetRequiredService<AvaloniaUI.ViewModels.MainViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainVm
                };
            }

#if DEBUG
            this.AttachDevTools();
#endif


            base.OnFrameworkInitializationCompleted();
        }
    }
}

```

## Avalonia\Program.cs
```csharp
using ApplicationLayer.Services;
using Avalonia;
using Avalonia.Logging;
using AvaloniaUI;
using AvaloniaUI.Hub;
using AvaloniaUI.Services;
using Core.Interfaces;
using Infrastructure.HidHide;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDL;
using static SDL.SDL3;

internal static class Program
{
    public static ServiceProvider Services = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        InitLogging();

        // Logs principais do processo
        var baseDir = AppContext.BaseDirectory;
        Trace.Listeners.Add(new TextWriterTraceListener(
            Path.Combine(baseDir, "nirvana-input_main.log")));
        Trace.AutoFlush = true;

        Debug.WriteLine("==== Nirvana Remap iniciado ====");
        Debug.WriteLine($"BaseDirectory = {baseDir}");

        Services = ConfigureServices();

        // Modo headless (sem UI), só capturando e emitindo ViGEm
        if (args.Any(a => string.Equals(a, "--raw", StringComparison.OrdinalIgnoreCase)))
        {
            RunHeadlessAsync().GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static async Task RunHeadlessAsync()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await using var scope = Services.CreateAsyncScope();
        var runner = scope.ServiceProvider
                          .GetRequiredService<AvaloniaUI.Services.RawVirtualizationRunner>();

        Console.WriteLine("[RAW] Argumento --raw detectado. Rodando sem UI…");
        await runner.RunAsync(cts.Token);
    }

    private static ServiceProvider ConfigureServices()
    {
        var sc = new ServiceCollection();

        // Infra / Core
        sc.AddSingleton<IHidHideService>(_ => new HidHideCliService());
        sc.AddSingleton<GamepadVirtualizationOrchestrator>();
        sc.AddSingleton<Infrastructure.Adapters.Outputs.ViGEmOutput>();

        // Captura física + runner headless
        sc.AddSingleton<AvaloniaUI.Services.GamepadRemapService>();
        sc.AddSingleton<AvaloniaUI.Services.RawVirtualizationRunner>();

        // Hub / storage / captura abstrata (se você ainda usa)
        sc.AddSingleton<IMappingStore, JsonMappingStore>();
        sc.AddSingleton<IInputCaptureService, SdlCaptureService>();

        // Views
       // sc.AddTransient<AvaloniaUI.Views.MappingHubView>();
       // sc.AddTransient<AvaloniaUI.Views.MappingHubWindow>();
        sc.AddTransient<AvaloniaUI.Views.DiagnosticsGamepadWindow>();

        // ViewModels
        sc.AddSingleton<AvaloniaUI.ViewModels.MainViewModel>();
        sc.AddSingleton<AvaloniaUI.ViewModels.MappingHubViewModel>();
        sc.AddSingleton<AvaloniaUI.ViewModels.DiagnosticsGamepadViewModel>();

        // Log específico de DI
        Trace.Listeners.Add(new TextWriterTraceListener(
            Path.Combine(AppContext.BaseDirectory, "nirvana-input_sc.log")));
        Trace.AutoFlush = true;

        return sc.BuildServiceProvider();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .LogToTrace(LogEventLevel.Information);

    private static void InitLogging()
    {
        // Se você tiver alguma configuração extra de logging, deixa aqui.
        // Por enquanto estamos só com Trace + Debug.
    }
}

```

## Avalonia\ProgramCore\MappingEngine.cs
```csharp
using AvaloniaUI.Hub;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Continuous mapping engine that takes the SDL snapshot (name -> double),
/// applies the configured physical-to-logical map, and produces a ViGEm-compatible
/// output state (name -> float).
///
/// Rules:
/// - Buttons are always digital (0 or 1).
/// - Triggers: TriggerLeft/TriggerRight remain analog 0..1; any other action is digital.
/// - Sticks: ThumbLX/ThumbLY/ThumbRX/ThumbRY remain analog -1..1; any other action is digital.
/// </summary>
namespace AvaloniaUI.ProgramCore
{
    /// <summary>
    /// Holds the loaded physical-to-logical bindings and converts input snapshots
    /// into the aggregated output state expected by ViGEm or other output services.
    /// </summary>
    public class MappingEngine
    {
        private readonly IMappingStore _store;

        // physical input -> logical action (ButtonA, LX+, etc.)
        private Dictionary<PhysicalInput, string> _map = new();
        // full physical state; incoming snapshots are deltas
        private readonly Dictionary<string, double> _physicalState = new(StringComparer.Ordinal);

        public MappingEngine(IMappingStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Loads the mapping JSON into the in-memory dictionary; when profileId is null,
        /// the default mapping file is used. The last mapping for the same physical input wins.
        /// </summary>
        public async Task LoadAsync(string? profileId = null, CancellationToken ct = default)
        {
            var loaded = await _store.LoadAsync(profileId, ct);

            var dict = new Dictionary<PhysicalInput, string>();

            foreach (var (action, assigned) in loaded)
            {
                if (assigned == PhysicalInput.None)
                    continue;

                if (dict.TryGetValue(assigned, out var existing))
                {
                    Debug.WriteLine(
                        $"[MappingEngine] Duplicate physical input: {assigned} was '{existing}', overwriting with '{action}'.");
                }

                dict[assigned] = action;
            }

            _map = dict;
        }

        // ------------------------------------------------------------
        // 1. Interpret snapshot -> active logical actions
        // ------------------------------------------------------------

        /// <summary>
        /// Builds the final output state from the incoming snapshot and the configured
        /// physical-to-action mapping.
        /// </summary>
        public Dictionary<string, float> BuildOutput(IReadOnlyDictionary<string, double> snap)
        {
            // Snapshots are deltas; keep a full state to avoid clearing inputs absent from this batch.
            foreach (var (name, value) in snap)
            {
                _physicalState[name] = value;
            }

            var state = (IReadOnlyDictionary<string, double>)_physicalState;
            var o = InitOutputState();

            // Detect axes that have only one side mapped; in that case use the full axis (both signs).
            var axisSides = new Dictionary<string, (bool hasPos, bool hasNeg)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _map)
            {
                if (!IsAxisOutput(kv.Value))
                    continue;

                switch (kv.Key)
                {
                    case PhysicalInput.LeftStickX_Pos:
                    case PhysicalInput.LeftStickY_Pos:
                    case PhysicalInput.RightStickX_Pos:
                    case PhysicalInput.RightStickY_Pos:
                        axisSides[kv.Value] = axisSides.TryGetValue(kv.Value, out var s)
                            ? (true, s.hasNeg)
                            : (true, false);
                        break;
                    case PhysicalInput.LeftStickX_Neg:
                    case PhysicalInput.LeftStickY_Neg:
                    case PhysicalInput.RightStickX_Neg:
                    case PhysicalInput.RightStickY_Neg:
                        axisSides[kv.Value] = axisSides.TryGetValue(kv.Value, out var s2)
                            ? (s2.hasPos, true)
                            : (false, true);
                        break;
                }
            }

            foreach (var kv in _map)
            {
                var phys = kv.Key;
                var action = kv.Value;
                var sides = axisSides.TryGetValue(action, out var s) ? s : (false, false);
                bool fullAxis = IsAxisOutput(action) && !(s.hasPos && s.hasNeg);

                switch (phys)
                {
                    // --------------------------------------------------
                    // BUTTONS: always digital
                    // --------------------------------------------------
                    case PhysicalInput.ButtonSouth:      // A
                        MapFromButton("A", action, state, o);
                        break;
                    case PhysicalInput.ButtonEast:       // B
                        MapFromButton("B", action, state, o);
                        break;
                    case PhysicalInput.ButtonWest:       // X
                        MapFromButton("X", action, state, o);
                        break;
                    case PhysicalInput.ButtonNorth:      // Y
                        MapFromButton("Y", action, state, o);
                        break;

                    case PhysicalInput.LeftBumper:       // LB
                        MapFromButton("LB", action, state, o);
                        break;
                    case PhysicalInput.RightBumper:      // RB
                        MapFromButton("RB", action, state, o);
                        break;

                    case PhysicalInput.LeftStickClick:   // L3
                        MapFromButton("L3", action, state, o);
                        break;
                    case PhysicalInput.RightStickClick:  // R3
                        MapFromButton("R3", action, state, o);
                        break;

                    case PhysicalInput.Back:             // View
                        MapFromButton("View", action, state, o);
                        break;
                    case PhysicalInput.Start:            // Menu
                        MapFromButton("Menu", action, state, o);
                        break;

                    case PhysicalInput.DPadUp:
                        MapFromButton("DUp", action, state, o);
                        break;
                    case PhysicalInput.DPadDown:
                        MapFromButton("DDown", action, state, o);
                        break;
                    case PhysicalInput.DPadLeft:
                        MapFromButton("DLeft", action, state, o);
                        break;
                    case PhysicalInput.DPadRight:
                        MapFromButton("DRight", action, state, o);
                        break;

                    // --------------------------------------------------
                    // TRIGGERS: can be analog or digital
                    // --------------------------------------------------
                    case PhysicalInput.LeftTrigger:
                        MapFromTrigger("LT", action, state, o);
                        break;
                    case PhysicalInput.RightTrigger:
                        MapFromTrigger("RT", action, state, o);
                        break;

                    // --------------------------------------------------
                    // AXES: can be analog or digital
                    // --------------------------------------------------
                    case PhysicalInput.LeftStickX_Pos:
                        MapFromAxis("LX", +1, action, state, o, fullAxis);
                        break;
                    case PhysicalInput.LeftStickX_Neg:
                        MapFromAxis("LX", -1, action, state, o, fullAxis);
                        break;

                    case PhysicalInput.LeftStickY_Pos:
                        MapFromAxis("LY", +1, action, state, o, fullAxis);
                        break;
                    case PhysicalInput.LeftStickY_Neg:
                        MapFromAxis("LY", -1, action, state, o, fullAxis);
                        break;

                    case PhysicalInput.RightStickX_Pos:
                        MapFromAxis("RX", +1, action, state, o, fullAxis);
                        break;
                    case PhysicalInput.RightStickX_Neg:
                        MapFromAxis("RX", -1, action, state, o, fullAxis);
                        break;

                    case PhysicalInput.RightStickY_Pos:
                        MapFromAxis("RY", +1, action, state, o, fullAxis);
                        break;
                    case PhysicalInput.RightStickY_Neg:
                        MapFromAxis("RY", -1, action, state, o, fullAxis);
                        break;

                    default:
                        break;
                }
            }

            return o;
        }


        // ------------------------------------------------------------
        // 2. Build XInput state from logical actions
        // ------------------------------------------------------------
        /// <summary>
        /// Creates a zeroed ViGEm/XInput-compatible output state.
        /// </summary>
        public Dictionary<string, float> InitOutputState()
        {
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                // default zeros:
                ["ButtonA"] = 0,
                ["ButtonB"] = 0,
                ["ButtonX"] = 0,
                ["ButtonY"] = 0,

                ["ButtonLeftShoulder"] = 0,
                ["ButtonRightShoulder"] = 0,

                ["ButtonStart"] = 0,
                ["ButtonBack"] = 0,

                ["ThumbLPressed"] = 0,
                ["ThumbRPressed"] = 0,

                ["DPadUp"] = 0,
                ["DPadDown"] = 0,
                ["DPadLeft"] = 0,
                ["DPadRight"] = 0,

                ["TriggerLeft"] = 0,
                ["TriggerRight"] = 0,

                ["ThumbLX"] = 0,
                ["ThumbLY"] = 0,
                ["ThumbRX"] = 0,
                ["ThumbRY"] = 0,
            };
        }

        private static bool IsTriggerOutput(string action) =>
            string.Equals(action, "TriggerLeft", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "TriggerRight", StringComparison.OrdinalIgnoreCase);

        private static bool IsAxisOutput(string action) =>
            string.Equals(action, "ThumbLX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbLY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbRX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbRY", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------
        // Map from BUTTON (snapKey -> 0/1)
        // ------------------------------------------------------------
        private static void MapFromButton(
            string snapKey,
            string action,
            IReadOnlyDictionary<string, double> snap,
            Dictionary<string, float> o)
        {
            if (!snap.TryGetValue(snapKey, out var v))
                return;

            var pressed = v >= 0.5;
            if (!pressed)
                return;

            if (!o.ContainsKey(action))
                return; // unknown action -> ignore

            o[action] = 1f;
        }

        // ------------------------------------------------------------
        // Map from TRIGGER (snapKey -> 0..1)
        // ------------------------------------------------------------
        private static void MapFromTrigger(
            string snapKey,
            string action,
            IReadOnlyDictionary<string, double> snap,
            Dictionary<string, float> o)
        {
            var v = snap.TryGetValue(snapKey, out var raw) ? raw : 0.0;

            if (IsTriggerOutput(action))
            {
                if (!o.ContainsKey(action)) return;
                var f = (float)Math.Clamp(v, 0.0, 1.0);
                // take the largest value (if multiple mappings target the same trigger)
                if (f > o[action])
                    o[action] = f;
            }
            else
            {
                // treat as digital button
                if (v >= 0.5 && o.ContainsKey(action))
                    o[action] = 1f;
            }
        }

        // ------------------------------------------------------------
        // Map from AXIS (snapKey -> -1..1)
        // direction: +1 (Pos) or -1 (Neg)
        // ------------------------------------------------------------
        private static void MapFromAxis(
            string snapKey,
            int direction,
            string action,
            IReadOnlyDictionary<string, double> snap,
            Dictionary<string, float> o,
            bool fullAxis)
        {
            if (!snap.TryGetValue(snapKey, out var v))
                return;

            if (IsAxisOutput(action))
            {
                if (!o.ContainsKey(action)) return;

                float contrib;
                if (fullAxis)
                {
                    contrib = (float)v;
                }
                else
                {
                    // filter by correct side of the axis
                    if (direction > 0 && v <= 0.0) return;
                    if (direction < 0 && v >= 0.0) return;

                    contrib = (float)v;
                    if (direction > 0)
                        contrib = Math.Max(0f, contrib);
                    else
                        contrib = Math.Min(0f, contrib);
                }

                var old = o[action];
                // prefer the value with the largest magnitude if multiple sources map to the same axis
                if (Math.Abs(contrib) > Math.Abs(old))
                    o[action] = contrib;
            }
            else
            {
                // digital mode with threshold
                const double threshold = 0.6;
                bool active = direction > 0
                    ? v >= threshold
                    : v <= -threshold;

                if (active && o.ContainsKey(action))
                    o[action] = 1f;
            }
        }
    }
}

```

## Core\Interfaces\IOutputService.cs
```csharp
using Core.Events.Outputs;
using System.Collections.Generic;
// Para MappedOutput

namespace Core.Interfaces
{
    /// <summary>
    /// Define o contrato para serviÃ§os que aplicam saÃ­das mapeadas.
    /// As implementaÃ§Ãµes sÃ£o responsÃ¡veis por executar a aÃ§Ã£o correspondente
    /// a um <see cref="MappedOutput"/>.
    /// </summary>
    public interface IOutputService // Corrigido de 'class' para 'interface'
    {
        /// <summary>
        /// Aplica a saÃ­da mapeada especificada.
        /// A implementaÃ§Ã£o deve interpretar o <paramref name="output"/> e executar a aÃ§Ã£o correspondente
        /// (ex: simular um pressionar de tecla, mover o cursor do rato, etc.).
        /// </summary>
        /// <param name="output">A saÃ­da mapeada a ser aplicada.</param>
        void Apply(MappedOutput output);
        void ApplyAll(Dictionary<string, float> outputState);
        void Connect();
        void Disconnect();

        bool IsConnected { get; }
    }
}
```

## Core\Interfaces\IMappingService.cs
```csharp
using Core.Events.Inputs;
using Core.Events.Outputs;

namespace Core.Interfaces
{
    public interface IMappingService
    {
        IEnumerable<MappedOutput> Map(ControllerInput input);
   
    }
}

```

## Core\Interfaces\IMapping.cs
```csharp
using Core.Events.Inputs;
using Core.Events.Outputs;

namespace Core.Interfaces;

/// <summary>
/// Define um contrato para regras de mapeamento de entradas de controlador para saÃ­das mapeadas.
/// </summary>
public interface IMapping
{
    /// <summary>
    /// Verifica se a entrada do controlador fornecida corresponde Ã  condiÃ§Ã£o de mapeamento.
    /// </summary>
    /// <param name="input">O evento de entrada do controlador a ser verificado.</param>
    /// <returns><c>true</c> se a entrada corresponder Ã  regra de mapeamento; caso contrÃ¡rio, <c>false</c>.</returns>
    bool Matches(ControllerInput input);

    /// <summary>
    /// Mapeia a entrada do controlador fornecida para uma saÃ­da mapeada.
    /// </summary>
    /// <param name="input">O evento de entrada do controlador a ser mapeado.</param>
    /// <returns>Um <see cref="MappedOutput"/> representando o resultado do mapeamento.</returns>
    /// <remarks>
    /// Este mÃ©todo geralmente Ã© chamado apÃ³s <see cref="Matches"/> retornar <c>true</c> para a mesma entrada.
    /// </remarks>
    MappedOutput Map(ControllerInput input);
}
```

## Core\Interfaces\IHidHideService.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{

    public interface IHidHideService
    {
        Task<bool> IsInstalledAsync();

        Task EnableHidingAsync();
        Task DisableHidingAsync();

        Task AddApplicationAsync(string exePath);
        Task RemoveApplicationAsync(string exePath);

        Task AddDeviceAsync(string deviceIdOrPath);
        Task RemoveDeviceAsync(string deviceIdOrPath);
    }
}

```

## Core\Interfaces\IGamepadService.cs
```csharp
using Core.Entities;
using Core.Events.Inputs;
// NecessÃ¡rio para EventHandler

// Para ControllerInput

namespace Core.Interfaces
{
    /// <summary>
    /// Define o contrato para serviÃ§os que fornecem entradas de gamepad.
    /// As implementaÃ§Ãµes sÃ£o responsÃ¡veis por detetar e emitir eventos de <see cref="ControllerInput"/>.
    /// </summary>
    public interface IGamepadService // Corrigido de 'class' para 'interface'
    {
        /// <summary>
        /// Evento disparado quando uma nova entrada do controlador Ã© recebida.
        /// Os subscritores receberÃ£o um objeto <see cref="ControllerInput"/> com os detalhes da entrada.
        /// </summary>
        event EventHandler<ControllerInput> InputReceived;

        /// <summary>
        /// Evento disparado quando o estado de conexÃ£o do gamepad muda.
        /// O valor booleano indica se o dispositivo estÃ¡ conectado (<c>true</c>) ou nÃ£o.
        /// </summary>
        event EventHandler<bool> ConnectionChanged;

        /// <summary>
        /// Indica se o gamepad estÃ¡ atualmente conectado.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Evento disparado a cada polling com o estado completo e cru do gamepad (evento "baixo nÃ­vel").
        /// </summary>
        event EventHandler<GamepadState> StateChanged;

        /// <summary>
        /// Inicia a escuta por entradas do gamepad.
        /// A implementaÃ§Ã£o deve comeÃ§ar a monitorizar o dispositivo de gamepad
        /// e disparar o evento <see cref="InputReceived"/> quando apropriado.
        /// </summary>
        void StartListening();

        /// <summary>
        /// Para a escuta por entradas do gamepad.
        /// A implementaÃ§Ã£o deve libertar quaisquer recursos associados Ã  escuta do dispositivo.
        /// </summary>
        void StopListening(); // Modificadores de acesso explÃ­citos nÃ£o sÃ£o necessÃ¡rios para membros de interface (sÃ£o public por defeito)
                              // mas podem ser adicionados para clareza se desejado.
        void UpdateCalibration(CalibrationSettings settings); // ðŸ”§ adicionado                    
    }
}
```

## Core\Interfaces\ICalibrationService.cs
```csharp
using Core.Entities;

namespace Core.Interfaces;

public interface ICalibrationService
{
    float ApplyDeadzone(float input, float deadzone);
    float AdjustSensitivity(float input, float sensitivity);
    GamepadState Calibrate(GamepadState rawState, CalibrationSettings settings);
}
```

## Core\Entities\GamepadState.cs
```csharp
// MemberwiseClone implicitamente disponÃ­vel via 'using System'.

namespace Core.Entities;
/// <summary>
/// Representa o estado instantÃ¢neo de todos os botÃµes e eixos de um gamepad.
/// </summary>
public class GamepadState : IEquatable<GamepadState>
{
    /// <summary>
    /// ObtÃ©m ou define o estado dos botÃµes.
    /// </summary>
    public bool ButtonA { get; set; }
    public bool ButtonB { get; set; }
    public bool ButtonX { get; set; }
    public bool ButtonY { get; set; }

    /// <summary>
    /// ObtÃ©m ou define o estado dos D-Pads.
    /// </summary>
    public bool DPadUp { get; set; }
    public bool DPadDown { get; set; }
    public bool DPadLeft { get; set; }
    public bool DPadRight { get; set; }

    /// <summary>
    /// ObtÃ©m ou define o estado do botÃ£o Start e Back (ou Select).
    /// </summary>
    public bool ButtonStart { get; set; }
    public bool ButtonBack { get; set; }

    /// <summary>
    /// ObtÃ©m ou define o estado do botÃ£o de ombro esquerdo (LB)
    /// e direito (RB).
    /// </summary>
    public bool ButtonLeftShoulder { get; set; }
    public bool ButtonRightShoulder { get; set; }

    /// <summary>
    /// ObtÃ©m ou define o estado do botÃ£o do analÃ³gico esquerdo (pressionado)
    /// e direito (pressionado).
    /// </summary>
    public bool ThumbLPressed { get; set; }
    public bool ThumbRPressed { get; set; }

    /// <summary>
    /// ObtÃ©m ou define o valor do gatilho esquerdo (LT) e direito (RT).
    /// Varia de 0.0 (solto) a 1.0 (pressionado).
    /// </summary>
    public float TriggerLeft { get; set; }
    public float TriggerRight { get; set; }

    /// <summary>
    /// ObtÃ©m ou define a posiÃ§Ã£o no eixo X e Y dos analÃ³gicos esquerdo e direito.
    /// Varia de -1.0 (esquerda) a 1.0 (direita).
    /// </summary>
    public float ThumbLX { get; set; }
    public float ThumbLY { get; set; }
    public float ThumbRX { get; set; }
    public float ThumbRY { get; set; }

    public bool Equals(GamepadState? other)
    {
        if (other is null) return false;

        return ButtonA == other.ButtonA &&
               ButtonB == other.ButtonB &&
               ButtonX == other.ButtonX &&
               ButtonY == other.ButtonY &&
               DPadUp == other.DPadUp &&
               DPadDown == other.DPadDown &&
               DPadLeft == other.DPadLeft &&
               DPadRight == other.DPadRight &&
               ButtonStart == other.ButtonStart &&
               ButtonBack == other.ButtonBack &&
               ButtonLeftShoulder == other.ButtonLeftShoulder &&
               ButtonRightShoulder == other.ButtonRightShoulder &&
               ThumbLPressed == other.ThumbLPressed &&
               ThumbRPressed == other.ThumbRPressed &&
               TriggerLeft.Equals(other.TriggerLeft) &&
               TriggerRight.Equals(other.TriggerRight) &&
               ThumbLX.Equals(other.ThumbLX) &&
               ThumbLY.Equals(other.ThumbLY) &&
               ThumbRX.Equals(other.ThumbRX) &&
               ThumbRY.Equals(other.ThumbRY);
    }

    public override bool Equals(object? obj) => Equals(obj as GamepadState);
    public override string ToString()
    {
        return $"""
        A:{ButtonA} B:{ButtonB} X:{ButtonX} Y:{ButtonY}
        DPad â†‘:{DPadUp} â†“:{DPadDown} â†:{DPadLeft} â†’:{DPadRight}
        Start:{ButtonStart} Back:{ButtonBack}
        LB:{ButtonLeftShoulder} RB:{ButtonRightShoulder}
        L3:{ThumbLPressed} R3:{ThumbRPressed}
        LT:{TriggerLeft:F2} RT:{TriggerRight:F2}
        LX:{ThumbLX:F2} LY:{ThumbLY:F2} RX:{ThumbRX:F2} RY:{ThumbRY:F2}
    """;
    }

    public override int GetHashCode()
    {
        var hash1 = HashCode.Combine(
            ButtonA, ButtonB, ButtonX, ButtonY,
            DPadUp, DPadDown, DPadLeft, DPadRight
        );

        var hash2 = HashCode.Combine(
            ButtonStart, ButtonBack,
            ButtonLeftShoulder, ButtonRightShoulder,
            ThumbLPressed, ThumbRPressed,
            TriggerLeft, TriggerRight
        );

        var hash3 = HashCode.Combine(
            ThumbLX, ThumbLY, ThumbRX, ThumbRY
        );

        return HashCode.Combine(hash1, hash2, hash3);
    }



    /// <summary>
    /// Cria uma cÃ³pia superficial (shallow copy) do estado atual do gamepad.
    /// </summary>
    /// <returns>Um novo objeto <see cref="GamepadState"/> com os mesmos valores do atual.</returns>
    public GamepadState Clone()
    {
        return (GamepadState)this.MemberwiseClone();
    }
}
```

## Core\Entities\CalibrationSettings.cs
```csharp
namespace Core.Entities;

/// <summary>
/// Representa as configurações de calibração para sticks analógicos e gatilhos de um gamepad.
/// </summary>
public class CalibrationSettings
{
    // Deadzones
    public float LeftStickDeadzoneInner { get; set; } = 0.1f;
    public float LeftStickDeadzoneOuter { get; set; } = 1.0f;

    public float RightStickDeadzoneInner { get; set; } = 0.1f;
    public float RightStickDeadzoneOuter { get; set; } = 1.0f;

    // Sensibilidade
    public float LeftStickSensitivity { get; set; } = 1.0f;
    public float RightStickSensitivity { get; set; } = 1.0f;

    // Gatilhos
    public float LeftTriggerStart { get; set; } = 0.0f;
    public float LeftTriggerEnd { get; set; } = 1.0f;
    
    public float RightTriggerStart { get; set; } = 0.0f;
    public float RightTriggerEnd { get; set; } = 1.0f;

    // Extra: ajustes por perfil futuramente
}
```

## Core\Events\Outputs\MappedOutput.cs
```csharp
namespace Core.Events.Outputs
{
    /// <summary>
    /// Representa o resultado de um mapeamento de uma entrada do controlador.
    /// </summary>
    /// <param name="outputName">O nome identificador da saÃ­da mapeada (ex: "TeclaW", "AcaoPular").</param>
    /// <param name="value">O valor associado Ã  saÃ­da mapeada.</param>
    public class MappedOutput(string outputName, float value)
    {
        /// <summary>
        /// ObtÃ©m o nome identificador da saÃ­da mapeada.
        /// </summary>
        public string OutputName { get; } = outputName;

        /// <summary>
        /// ObtÃ©m o valor associado Ã  saÃ­da mapeada.
        /// </summary>
        public float Value { get; } = value;
    }
}
```

## Core\Events\Inputs\ControllerInput.cs
```csharp
namespace Core.Events.Inputs;

    /// <summary>
    /// Representa um evento de entrada individual de um controlador.
    /// </summary>
    /// <param name="inputName">O nome identificador da entrada (ex: "ButtonA", "ThumbLX").</param>
    /// <param name="value">O valor associado Ã  entrada (ex: 1.0 para botÃ£o pressionado, -0.5 para um eixo).</param>
    public class ControllerInput
    {
        public string Name { get; }
        public float Value { get; }
        public float? ValueY { get; }  // Para analÃ³gicos que tÃªm X e Y (sticks)
        public float? ValueZ { get; }  // Para casos futuros

        // Para botÃµes ou gatilhos (simples)
        public ControllerInput(string name, float value)
        {
            Name = name;
            Value = value;
        }

        // Para analÃ³gicos: sticks com dois eixos
        public ControllerInput(string name, float valueX, float valueY)
        {
            Name = name;
            Value = valueX;
            ValueY = valueY;
        }

        // (Opcional) Para eixos com trÃªs dimensÃµes (caso precise no futuro)
        public ControllerInput(string name, float valueX, float valueY, float valueZ)
        {
            Name = name;
            Value = valueX;
            ValueY = valueY;
            ValueZ = valueZ;
        }
    }


```


