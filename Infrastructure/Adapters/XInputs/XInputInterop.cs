using System.Runtime.InteropServices;

namespace Infrastructure.Adapters.XInputs;

    /// <summary>
    /// Contém estruturas e constantes para interoperabilidade com a API XInput.
    /// Estas definições são usadas para comunicação P/Invoke com xinput*.dll.
    /// </summary>
    public static class XInputInterop // Sugestão: tornar estática, pois só contém membros estáticos e structs
    {
        /// <summary>
        /// Representa o estado dos botões, gatilhos e analógicos de um gamepad XInput.
        /// Corresponde à estrutura XINPUT_GAMEPAD da API XInput.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XInputGamepad // Nome original: XinputGamepad
        {
            /// <summary>
            /// Bitmask dos botões do gamepad que estão atualmente pressionados.
            /// Use as constantes XInputGamepad* para verificar botões específicos.
            /// </summary>
            public ushort wButtons;
            /// <summary>
            /// O valor atual do controlo do gatilho esquerdo. O valor está entre 0 e 255.
            /// </summary>
            public byte bLeftTrigger;
            /// <summary>
            /// O valor atual do controlo do gatilho direito. O valor está entre 0 e 255.
            /// </summary>
            public byte bRightTrigger;
            /// <summary>
            /// O valor atual do eixo X do analógico esquerdo. O valor está entre -32768 e 32767.
            /// Um valor de 0 é considerado centro.
            /// </summary>
            public short sThumbLX;
            /// <summary>
            /// O valor atual do eixo Y do analógico esquerdo. O valor está entre -32768 e 32767.
            /// Um valor de 0 é considerado centro.
            /// </summary>
            public short sThumbLY;
            /// <summary>
            /// O valor atual do eixo X do analógico direito. O valor está entre -32768 e 32767.
            /// Um valor de 0 é considerado centro.
            /// </summary>
            public short sThumbRX;
            /// <summary>
            /// O valor atual do eixo Y do analógico direito. O valor está entre -32768 e 32767.
            /// Um valor de 0 é considerado centro.
            /// </summary>
            public short sThumbRY;
        }

        /// <summary>
        /// Representa o estado de um controlador XInput, incluindo o número do pacote e o estado do gamepad.
        /// Corresponde à estrutura XINPUT_STATE da API XInput.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XInputState // Nome original: XinputState
        {
            /// <summary>
            /// Número do pacote do estado. Indica se o estado do controlador mudou desde a última leitura.
            /// Se dwPacketNumber for o mesmo em chamadas consecutivas a XInputGetState, o estado não mudou.
            /// </summary>
            public uint dwPacketNumber;
            /// <summary>
            /// Estrutura <see cref="XInputGamepad"/> contendo o estado atual do gamepad.
            /// </summary>
            public XInputGamepad Gamepad;
        }

        // Constantes para os bitmasks dos botões do XInput
        // Estes valores correspondem às definições em XInput.h

        /// <summary>Bitmask para o botão D-Pad Cima do gamepad XInput.</summary>
        public const ushort XInputGamepadDpadUp = 0x0001; // Nome original: XinputGamepadDpadUp
        
        /// <summary>Bitmask para o botão D-Pad Baixo do gamepad XInput.</summary>
        public const ushort XInputGamepadDpadDown = 0x0002; // Nome original: XinputGamepadDpadDown
        
        /// <summary>Bitmask para o botão D-Pad Esquerda do gamepad XInput.</summary>
        public const ushort XInputGamepadDpadLeft = 0x0004; // Nome original: XinputGamepadDpadLeft
        
        /// <summary>Bitmask para o botão D-Pad Direita do gamepad XInput.</summary>
        public const ushort XInputGamepadDpadRight = 0x0008; // Nome original: XinputGamepadDpadRight
        
        /// <summary>Bitmask para o botão Start do gamepad XInput.</summary>
        public const ushort XInputGamepadStart = 0x0010; // Nome original: XinputGamepadStart
        
        /// <summary>Bitmask para o botão Back (Select) do gamepad XInput.</summary>
        public const ushort XInputGamepadBack = 0x0020; // Nome original: XinputGamepadBack
        
        /// <summary>Bitmask para o botão do analógico esquerdo (pressionado) do gamepad XInput.</summary>
        public const ushort XInputGamepadLeftThumb = 0x0040; // Nome original: XinputGamepadLeftThumb
        
        /// <summary>Bitmask para o botão do analógico direito (pressionado) do gamepad XInput.</summary>
        public const ushort XInputGamepadRightThumb = 0x0080; // Nome original: XinputGamepadRightThumb

        /// <summary>Bitmask para o botão de ombro esquerdo (LB) do gamepad XInput.</summary>
        public const ushort XInputGamepadLeftShoulder = 0x0100; // Nome original: XinputGamepadLeftShoulder
        
        /// <summary>Bitmask para o botão de ombro direito (RB) do gamepad XInput.</summary>
        public const ushort XInputGamepadRightShoulder = 0x0200; // Nome original: XinputGamepadRightShoulder
        
        // Os botões A, B, X, Y são frequentemente referidos sem a palavra "Gamepad" no nome da constante em exemplos,
        // mas manter a consistência com XInputGamepadDpadUp, etc., é bom.
        
        /// <summary>Bitmask para o botão A do gamepad XInput.</summary>
        public const ushort XInputGamepadA = 0x1000; // Nome original: XinputGamepadA
        
        /// <summary>Bitmask para o botão B do gamepad XInput.</summary>
        public const ushort XInputGamepadB = 0x2000; // Nome original: XinputGamepadB
        
        /// <summary>Bitmask para o botão X do gamepad XInput.</summary>
        public const ushort XInputGamepadX = 0x4000; // Nome original: XinputGamepadX
        
        /// <summary>Bitmask para o botão Y do gamepad XInput.</summary>
        public const ushort XInputGamepadY = 0x8000; // Nome original: XinputGamepadY
    }
