using System; // Para Math.Abs
using System.Runtime.InteropServices;
using Core.Entities; // Para GamepadState
using static Infrastructure.Adapters.XInputs.XInputInterop; // Para XInputState e constantes dos botões

namespace Infrastructure.Adapters.XInputs
{
    /// <summary>
    /// Adaptador para interagir com a API XInput para ler o estado de gamepads.
    /// Utiliza P/Invoke para chamar funções da xinput1_4.dll.
    /// </summary>
    public class XInput // "partial" sugere que pode haver outra parte desta classe noutro ficheiro.
    {
        // Nota: xinput1_4.dll é para Windows 8+. Para compatibilidade com Windows 7, seria xinput1_3.dll ou xinput9_1_0.dll.
        // Se precisar de suportar múltiplas versões, podem ser necessárias estratégias de carregamento mais avançadas.
        /// <summary>
        /// Obtém o estado atual do controlador XInput especificado.
        /// </summary>
        /// <param name="dwUserIndex">Índice do utilizador (controlador). Pode ser de 0 a 3.</param>
        /// <param name="pState">Recebe o estado atual do controlador.</param>
        /// <returns>
        /// Se a função for bem-sucedida, o valor de retorno é <c>ERROR_SUCCESS</c> (0).
        /// Se o controlador não estiver conectado, o valor de retorno é <c>ERROR_DEVICE_NOT_CONNECTED</c> (1167).
        /// </returns>
       
        private delegate int XInputGetStateDelegate(int dwUserIndex, out XInputInterop.XInputState pState);
        private static readonly XInputGetStateDelegate _xInputGetState;

        // Permite que testes definam um estado personalizado quando nenhuma
        // biblioteca XInput real está disponível.
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

            // Quando nenhuma biblioteca está disponível (por exemplo, em ambientes
            // de teste ou sistemas não Windows), fornece uma implementação de
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
        /// Código de retorno da API XInput que indica sucesso na operação.
        /// </summary>
        public const int ERROR_SUCCESS = 0; // Nome original: ErrorSuccess

        /// <summary>
        /// Código de retorno da API XInput que indica que o controlador não está conectado.
        /// </summary>
        public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

        /// <summary>
        /// Obtém o estado atual do primeiro gamepad XInput conectado (índice 0)
        /// e mapeia-o para um objeto <see cref="GamepadState"/>.
        /// </summary>
        /// <returns>
        /// Um objeto <see cref="GamepadState"/> representando o estado atual do gamepad.
        /// Se o gamepad não estiver conectado, retorna um estado com valores padrão (geralmente tudo `false` ou `0f`).
        /// </returns>
        private CalibrationSettings _calibration = new(); // default

        public void ApplyCalibration(CalibrationSettings settings)
        {
            _calibration = settings;
        }
        
        public GamepadState GetState(out bool isConnected)
        {
            // Tenta obter o estado para o primeiro controlador (índice 0)
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
                // Se o controlador não estiver conectado ou ocorrer um erro,
                // retorna um estado padrão simples (vazio).
                // Poderia também lançar uma exceção ou ter uma propriedade IsConnected.
                return new GamepadState(); // Retorna um estado "em repouso"
            }
          

            return new GamepadState
            {
                // Usar as constantes definidas em XInputInterop para maior clareza e segurança
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
                    
                // Normaliza os valores dos analógicos de -32768/32767 (short) para -1.0f-1.0f (float)
                // e aplica uma zona morta (deadzone).
                // A divisão por 32768f ou 32767f é uma escolha comum para normalização de short.
                ThumbLX = normalizedLX,
                ThumbLY = normalizedLY, // Inverter Y se necessário
                ThumbRX = normalizedRX,
                ThumbRY =  normalizedRY,  // Inverter Y se necessário
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
        /// Normaliza o valor de um eixo do analógico (short) para um float entre -1.0 e 1.0.
        /// </summary>
        /// <param name="value">O valor short do eixo do analógico (geralmente -32768 a 32767).</param>
        /// <param name="inverted">Opcional. Se verdadeiro, inverte o valor (útil para eixos Y).</param>
        /// <returns>Um valor float normalizado.</returns>
        private static float NormalizeThumbValue(short value, bool inverted = false)
        {
            var normalizedValue = value / 32768f; // Usar 32768f é comum para cobrir o intervalo completo.
            switch (value)
            {
                // Outra opção é Math.Max(-1f, value / 32767f) para garantir -1 a 1.
                case -32768 when !inverted:
                    return -1f; // Caso especial para o valor mínimo de short
                case -32768 when inverted:
                    return 1f;   // Se invertido
            }

            // Ajuste para garantir que o valor máximo seja 1.0f e o mínimo -1.0f
            if (normalizedValue > 1.0f) normalizedValue = 1.0f;
            if (normalizedValue < -1.0f) normalizedValue = -1.0f;

            return inverted ? -normalizedValue : normalizedValue;
        }


        /// <summary>
        /// Aplica uma "zona morta" (deadzone) a um valor de entrada do analógico.
        /// Se o valor absoluto da entrada for menor que a zona morta, retorna 0.
        /// </summary>
        /// <param name="value">O valor de entrada do analógico (normalizado, entre -1.0 e 1.0).</param>
        /// <param name="deadzone">O limiar da zona morta (ex: 0.1f para 10% de zona morta).</param>
        /// <returns>O valor com a zona morta aplicada, ou 0f se dentro da zona morta.</returns>
        private static float ApplyDeadzone(float value, float deadzone)
        {
            // Garante que deadzone seja positivo
            deadzone = Math.Abs(deadzone);
            return Math.Abs(value) < deadzone ? 0f :
                // Opcional: reescalar o valor para que comece em 0 após a zona morta, mapeando [deadzone, 1.0] para [0, 1.0]
                // float remappedValue = (Math.Abs(value) - deadzone) / (1.0f - deadzone);
                // return Math.Sign(value) * remappedValue;
                value; // Retorna o valor original se fora da zona morta (sem reescalonamento)
        }
    }
}