// Corrigido para o namespace correto de IGamepadService

using Core.Entities;
using Core.Events.Inputs;
using Core.Interfaces;
// Para EventHandler, Convert, Thread
// Para PropertyInfo (se mantiver a reflexão)

// Para XInput

// Para Thread

namespace Infrastructure.Adapters.XInputs
{
    /// <summary>
    /// Implementação de <see cref="IGamepadService"/> que utiliza a API XInput
    /// para monitorizar continuamente o estado de um gamepad e emitir eventos de entrada.
    /// </summary>
    public class XInputService : IGamepadService // Adicionada a implementação da interface
    {
        private readonly XInput _adapter = new();
        private GamepadState _previousState = new(); // Renomeado para clareza
        private volatile bool _isRunning; // volatile para visibilidade entre threads
        private Thread? _pollingThread;   // Renomeado para clareza
        private bool _isConnected;

        private const int XInputPollingIntervalMilliseconds = 20; // Mais configurável, ~50Hz

        /// <summary>
        /// Evento disparado quando uma nova entrada do controlador é recebida.
        /// </summary>
        public event EventHandler<ControllerInput>? InputReceived;
        public event EventHandler<bool>? ConnectionChanged;
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Inicia a escuta por entradas do gamepad XInput.
        /// Uma thread dedicada é iniciada para monitorizar o estado do gamepad.
        /// </summary>
        public void StartListening()
        {
            if (_isRunning) return;

            _isRunning = true;
            // Atualiza o estado anterior para o estado atual antes de começar,
            // para evitar disparar eventos para todos os botões no início se já estiverem ativos.
            _previousState = _adapter.GetState(out _isConnected);
            ConnectionChanged?.Invoke(this, _isConnected);

            _pollingThread = new Thread(ListenLoop)
            {
                IsBackground = true, // Permite que a aplicação feche mesmo que a thread esteja a correr
                Name = "XInputPollingThread" // Útil para depuração
            };
            _pollingThread.Start();
        }

        /// <summary>
        /// Para a escuta por entradas do gamepad XInput.
        /// Solicita que a thread de monitorização pare e aguarda a sua conclusão.
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
        /// Loop principal executado pela thread de monitorização para ler o estado do gamepad,
        /// detetar mudanças e disparar eventos <see cref="InputReceived"/>.
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

                // Otimização: Em vez de reflexão, comparar cada propriedade diretamente.
                // Isto é mais verboso mas significativamente mais performático num loop frequente.
                if (connected)
                {
                    CompareAndRaiseEvents(currentState);
                }

                _previousState = currentState.Clone(); // Guarda o estado atual para a próxima iteração

                Thread.Sleep(XInputPollingIntervalMilliseconds); // Controla a frequência de polling
            }
        }

        /// <summary>
        /// Compara o estado atual do gamepad com o estado anterior e dispara eventos
        /// <see cref="InputReceived"/> para quaisquer controlos que tenham mudado.
        /// (Alternativa otimizada à abordagem de reflexão).
        /// </summary>
        /// <param name="current">O estado atual do gamepad.</param>
        private void CompareAndRaiseEvents(GamepadState current)
        {
            // Botões Booleanos
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

            // Analógicos (Float)
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
        /// Helper para disparar ControllerInput se o valor float mudou (com uma pequena tolerância).
        /// </summary>
        private void RaiseIfChanged(string name, float previousValue, float currentValue, float tolerance = 0.0001f)
        {
            if (Math.Abs(previousValue - currentValue) > tolerance)
            {
                InputReceived?.Invoke(this, new ControllerInput(name, currentValue));
            }
        }

        // Se preferir manter a reflexão (mais conciso, mas menos performático):
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
                    // A conversão para float pode precisar de mais cuidado dependendo do tipo da propriedade
                    // (bool vs float). Booleans são frequentemente convertidos para 1.0f (true) e 0.0f (false).
                    float eventValue = 0f;
                    if (currentValue is bool bVal)
                    {
                        eventValue = bVal ? 1.0f : 0.0f;
                    }
                    else if (currentValue is float fVal)
                    {
                        eventValue = fVal;
                    }
                    // Adicionar mais conversões se GamepadState tiver outros tipos

                    InputReceived?.Invoke(this, new ControllerInput(prop.Name, eventValue));
                }
            }
        }
        */
    }
}