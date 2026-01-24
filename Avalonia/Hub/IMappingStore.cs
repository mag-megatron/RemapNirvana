// AvaloniaUI.Hub/IMappingStore.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaUI.Hub
{
    public interface IMappingStore
    {
        string[] GetDefaultActions();

        Task<(string action, PhysicalInput assigned)[]> LoadAsync(
            string? profileId,
            CancellationToken ct);

        Task SaveAsync(
            string? profileId,
            (string action, PhysicalInput assigned)[] map,
            CancellationToken ct);

        Task<string[]> ListProfilesAsync(CancellationToken ct);

        Task<bool> DeleteProfileAsync(string? profileId, CancellationToken ct);
    }
}
