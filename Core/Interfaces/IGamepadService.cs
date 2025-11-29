using Core.Entities;
using Core.Events.Inputs;
// Necessário para EventHandler

// Para ControllerInput

namespace Core.Interfaces
{
    /// <summary>
    /// Define o contrato para serviços que fornecem entradas de gamepad.
    /// As implementações são responsáveis por detetar e emitir eventos de <see cref="ControllerInput"/>.
    /// </summary>
    public interface IGamepadService // Corrigido de 'class' para 'interface'
    {
        /// <summary>
        /// Evento disparado quando uma nova entrada do controlador é recebida.
        /// Os subscritores receberão um objeto <see cref="ControllerInput"/> com os detalhes da entrada.
        /// </summary>
        event EventHandler<ControllerInput> InputReceived;

        /// <summary>
        /// Evento disparado quando o estado de conexão do gamepad muda.
        /// O valor booleano indica se o dispositivo está conectado (<c>true</c>) ou não.
        /// </summary>
        event EventHandler<bool> ConnectionChanged;

        /// <summary>
        /// Indica se o gamepad está atualmente conectado.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Evento disparado a cada polling com o estado completo e cru do gamepad (evento "baixo nível").
        /// </summary>
        event EventHandler<GamepadState> StateChanged;

        /// <summary>
        /// Inicia a escuta por entradas do gamepad.
        /// A implementação deve começar a monitorizar o dispositivo de gamepad
        /// e disparar o evento <see cref="InputReceived"/> quando apropriado.
        /// </summary>
        void StartListening();

        /// <summary>
        /// Para a escuta por entradas do gamepad.
        /// A implementação deve libertar quaisquer recursos associados à escuta do dispositivo.
        /// </summary>
        void StopListening(); // Modificadores de acesso explícitos não são necessários para membros de interface (são public por defeito)
                              // mas podem ser adicionados para clareza se desejado.
        void UpdateCalibration(CalibrationSettings settings); // 🔧 adicionado                    
    }
}