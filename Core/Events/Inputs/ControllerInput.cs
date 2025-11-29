namespace Core.Events.Inputs;

    /// <summary>
    /// Representa um evento de entrada individual de um controlador.
    /// </summary>
    /// <param name="inputName">O nome identificador da entrada (ex: "ButtonA", "ThumbLX").</param>
    /// <param name="value">O valor associado à entrada (ex: 1.0 para botão pressionado, -0.5 para um eixo).</param>
    public class ControllerInput
    {
        public string Name { get; }
        public float Value { get; }
        public float? ValueY { get; }  // Para analógicos que têm X e Y (sticks)
        public float? ValueZ { get; }  // Para casos futuros

        // Para botões ou gatilhos (simples)
        public ControllerInput(string name, float value)
        {
            Name = name;
            Value = value;
        }

        // Para analógicos: sticks com dois eixos
        public ControllerInput(string name, float valueX, float valueY)
        {
            Name = name;
            Value = valueX;
            ValueY = valueY;
        }

        // (Opcional) Para eixos com três dimensões (caso precise no futuro)
        public ControllerInput(string name, float valueX, float valueY, float valueZ)
        {
            Name = name;
            Value = valueX;
            ValueY = valueY;
            ValueZ = valueZ;
        }
    }

