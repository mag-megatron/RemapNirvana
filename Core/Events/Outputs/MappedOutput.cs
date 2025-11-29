namespace Core.Events.Outputs
{
    /// <summary>
    /// Representa o resultado de um mapeamento de uma entrada do controlador.
    /// </summary>
    /// <param name="outputName">O nome identificador da saída mapeada (ex: "TeclaW", "AcaoPular").</param>
    /// <param name="value">O valor associado à saída mapeada.</param>
    public class MappedOutput(string outputName, float value)
    {
        /// <summary>
        /// Obtém o nome identificador da saída mapeada.
        /// </summary>
        public string OutputName { get; } = outputName;

        /// <summary>
        /// Obtém o valor associado à saída mapeada.
        /// </summary>
        public float Value { get; } = value;
    }
}