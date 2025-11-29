using Core.Events.Outputs;
using Core.Interfaces;

namespace Infrastructure.Adapters.Outputs
{
    /// <summary>
    /// Implementação de <see cref="IOutputService"/> que escreve as saídas mapeadas
    /// para a consola do sistema. Útil para fins de depuração ou aplicações simples.
    /// </summary>
    public class ConsoleOutput : IOutputService
    {
        private readonly string _format;
        private bool _connected = false;

        /// <summary>
        /// Inicializa um novo ConsoleOutput, com formatação opcional de valor.
        /// </summary>
        /// <param name="format">Formato numérico para o valor, padrão é "F4".</param>
        public ConsoleOutput(string format = "F4")
        {
            _format = format;
        }

        public void Connect()
        {
            _connected = true;
            Console.WriteLine("[ConsoleOutput] Conectado.");
        }

        public void Disconnect()
        {
            _connected = false;
            Console.WriteLine("[ConsoleOutput] Desconectado.");
        }

        public bool IsConnected => _connected;

        /// <summary>
        /// Aplica a saída mapeada escrevendo o seu nome e valor na consola.
        /// </summary>
        /// <param name="output">A <see cref="MappedOutput"/> a ser processada.</param>
        public void Apply(MappedOutput output)
        {
            Console.WriteLine($"[ConsoleOutput] Apply: {output.OutputName} = {output.Value}");
        }
        public void ApplyAll(Dictionary<string, float> outputState)
        {
            Console.WriteLine("[ConsoleOutput] ApplyAll:");
            foreach (var kvp in outputState)
                Console.WriteLine($"  {kvp.Key} = {kvp.Value}");
        }

    

    }
}
