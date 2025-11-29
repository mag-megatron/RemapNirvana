using Core.Events.Inputs;
using Core.Events.Outputs;

namespace Core.Interfaces;

/// <summary>
/// Define um contrato para regras de mapeamento de entradas de controlador para saídas mapeadas.
/// </summary>
public interface IMapping
{
    /// <summary>
    /// Verifica se a entrada do controlador fornecida corresponde à condição de mapeamento.
    /// </summary>
    /// <param name="input">O evento de entrada do controlador a ser verificado.</param>
    /// <returns><c>true</c> se a entrada corresponder à regra de mapeamento; caso contrário, <c>false</c>.</returns>
    bool Matches(ControllerInput input);

    /// <summary>
    /// Mapeia a entrada do controlador fornecida para uma saída mapeada.
    /// </summary>
    /// <param name="input">O evento de entrada do controlador a ser mapeado.</param>
    /// <returns>Um <see cref="MappedOutput"/> representando o resultado do mapeamento.</returns>
    /// <remarks>
    /// Este método geralmente é chamado após <see cref="Matches"/> retornar <c>true</c> para a mesma entrada.
    /// </remarks>
    MappedOutput Map(ControllerInput input);
}