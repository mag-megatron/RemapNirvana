using Core.Events.Outputs;
using System.Collections.Generic;
// Para MappedOutput

namespace Core.Interfaces
{
    /// <summary>
    /// Define o contrato para serviços que aplicam saídas mapeadas.
    /// As implementações são responsáveis por executar a ação correspondente
    /// a um <see cref="MappedOutput"/>.
    /// </summary>
    public interface IOutputService // Corrigido de 'class' para 'interface'
    {
        /// <summary>
        /// Aplica a saída mapeada especificada.
        /// A implementação deve interpretar o <paramref name="output"/> e executar a ação correspondente
        /// (ex: simular um pressionar de tecla, mover o cursor do rato, etc.).
        /// </summary>
        /// <param name="output">A saída mapeada a ser aplicada.</param>
        void Apply(MappedOutput output);
        void ApplyAll(Dictionary<string, float> outputState);
        void Connect();
        void Disconnect();

        bool IsConnected { get; }
    }
}