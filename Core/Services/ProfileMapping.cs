using System.Collections.Generic; // Para List e IEnumerable
using System.Linq; // Para LINQ (Where, Select)
using Core.Events.Inputs;
using Core.Events.Outputs;
using Core.Interfaces;

namespace Core.Services
{
    /// <summary>
    /// Gere uma coleção de regras de mapeamento (<see cref="IMapping"/>) para um perfil específico
    /// e aplica essas regras a uma entrada de controlador.
    /// </summary>
    public class ProfileMapping
    {
        private readonly List<IMapping> _mappings = new();

        /// <summary>
        /// Adiciona uma regra de mapeamento à coleção do perfil.
        /// </summary>
        /// <param name="mapping">A regra de mapeamento (<see cref="IMapping"/>) a ser adicionada.</param>
        /// <remarks>
        /// Anteriormente, este método aceitava 'SimpleMapping'. Foi generalizado para aceitar qualquer 'IMapping'
        /// para maior flexibilidade e para alinhar com o tipo da lista interna '_mappings'.
        /// </remarks>
        public void AddMapping(IMapping mapping) // Alterado de SimpleMapping para IMapping
        {
            _mappings.Add(mapping);
        }

        /// <summary>
        /// Aplica todas as regras de mapeamento configuradas à entrada do controlador fornecida.
        /// </summary>
        /// <param name="input">O evento de entrada do controlador a ser processado.</param>
        /// <returns>
        /// Uma coleção de <see cref="MappedOutput"/> resultante dos mapeamentos que corresponderam à entrada.
        /// Pode ser vazia se nenhuma regra de mapeamento corresponder.
        /// </returns>
        public IEnumerable<MappedOutput> Apply(ControllerInput input)
        {
            return _mappings
                .Where(m => m.Matches(input)) // Filtra apenas os mapeamentos que correspondem
                .Select(m => m.Map(input));    // Transforma as correspondências em saídas mapeadas
        }

        // Opcional: Método para limpar mapeamentos ou obter os mapeamentos atuais, se necessário.
        /// <summary>
        /// Remove todos os mapeamentos deste perfil.
        /// </summary>
        public void ClearMappings()
        {
            _mappings.Clear();
        }

        /// <summary>
        /// Obtém uma cópia somente leitura da lista atual de mapeamentos.
        /// </summary>
        /// <returns>Uma lista somente leitura de <see cref="IMapping"/>.</returns>
        public IReadOnlyList<IMapping> GetMappings()
        {
            return _mappings.AsReadOnly();
        }
    }
}