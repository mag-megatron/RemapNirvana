using Core.Events.Inputs;
using Core.Events.Outputs;

namespace Core.Interfaces
{
    public interface IMappingService
    {
        IEnumerable<MappedOutput> Map(ControllerInput input);
   
    }
}
