using Core.Events.Inputs;
using Core.Events.Outputs;
using Core.Interfaces; // Necessário para IMapping

namespace Core.Services;

    /// <summary>
    /// Representa uma regra de mapeamento simples que compara o nome da entrada do controlador
    /// com um nome de entrada esperado e, se corresponder, mapeia para um nome de saída especificado.
    /// </summary>
    /// <param name="inputName">O nome da entrada do controlador que este mapeamento deve corresponder.</param>
    /// <param name="outputName">O nome da saída para a qual a entrada será mapeada se houver correspondência.</param>
    
    public class SimpleMapping(string inputName, string outputName) : IMapping // Adicionada a implementação da interface
    {
        // inputName e outputName são capturados pelo construtor primário
        // e podem ser acedidos dentro da classe. Se precisares deles
        // como propriedades públicas, terias que declará-los explicitamente:
        // public string InputName { get; } = inputName;
        // public string OutputName { get; } = outputName;

        /// <summary>
        /// Nome da entrada que este mapeamento verifica.
        /// </summary>
        public string InputName { get; } = inputName;

        /// <summary>
        /// Nome da saída que será gerada em caso de correspondência.
        /// </summary>
        public string OutputName { get; } = outputName;

        /// <summary>
        /// Verifica se o nome da entrada do controlador fornecida corresponde ao <c>inputName</c> esperado.
        /// </summary>
        /// <param name="input">O evento de entrada do controlador a ser verificado.</param>
        /// <returns><c>true</c> se o <see cref="ControllerInput.InputName"/> da entrada corresponder ao <c>inputName</c> configurado para este mapeamento; caso contrário, <c>false</c>.</returns>
        
        public bool Matches(ControllerInput input) => input.Name == InputName;

        /// <summary>
        /// Cria uma <see cref="MappedOutput"/> com o <c>outputName</c> configurado e o valor da entrada original.
        /// </summary>
        /// <param name="input">O evento de entrada do controlador a ser mapeado.</param>
        /// <returns>Um novo <see cref="MappedOutput"/> com o nome de saída especificado e o valor da entrada.</returns>
        public MappedOutput Map(ControllerInput input) => new(OutputName, input.Value);
    }
