using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using AvaloniaUI.Hub;
using Avalonia.Threading;
using System.Collections.Generic;

namespace AvaloniaUI.ViewModels
{
    public partial class MappingItemVM : ObservableObject
    {
        private readonly MappingHubViewModel _root;
        private PhysicalInput _assigned;

        public string ActionName { get; }
        public PhysicalInput Assigned
        {
            get => _assigned;
            set
            {
                if (SetProperty(ref _assigned, value))
                {
                    OnPropertyChanged(nameof(AssignedDisplay));
                    OnPropertyChanged(nameof(ConflictNote));
                    _root.RefreshConflicts();
                }
            }
        }

        public string AssignedDisplay => Assigned == PhysicalInput.None ? "—" : Assigned.ToString();

        public string ConflictNote
            => Assigned != PhysicalInput.None &&
               _root.Items.Any(x => x != this && x.Assigned == Assigned)
               ? "Duplicado"
               : "";


        public PhysicalInput[] AvailableInputs => _root.AvailableInputs;

        public IRelayCommand DetectCommand { get; }
        public IRelayCommand ClearCommand { get; }


        public MappingItemVM(string actionName, PhysicalInput assigned, MappingHubViewModel root)
        {
            ActionName = actionName;
            _assigned = assigned;
            _root = root;

            DetectCommand = new AsyncRelayCommand(DetectAsync);
            ClearCommand = new RelayCommand(() => Assigned = PhysicalInput.None);
        }

        private async Task DetectAsync()
        {
            try
            {
                var ct = _root.CaptureCts?.Token ?? CancellationToken.None;
                var result = await _root.CaptureService.CaptureNextAsync(TimeSpan.FromSeconds(5), ct);
                if (result is { } p) Assigned = p;
            }
            catch { /* silencioso */ }
        }
    }

    public partial class MappingHubViewModel : ObservableObject
    {
        [ObservableProperty] private string filter = "";
        [ObservableProperty] private string conflictSummary = "";

        // ➜ NOVO: lista de perfis + perfil atual
        public ObservableCollection<string> AvailableProfiles { get; } = new();
        [ObservableProperty]
        private string? currentProfileId;

        public ObservableCollection<MappingItemVM> Items { get; } = new();
        public ObservableCollection<MappingItemVM> FilteredItems { get; } = new();

        public PhysicalInput[] AvailableInputs { get; } =
            Enum.GetValues(typeof(PhysicalInput)).Cast<PhysicalInput>().ToArray();

        public IRelayCommand ReloadCommand { get; }
        public IRelayCommand ClearAllCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand NewProfileCommand { get; }

        public bool CanSave => Items.All(i => string.IsNullOrEmpty(i.ConflictNote));

        public IInputCaptureService CaptureService { get; }
        public IMappingStore MappingStore { get; }

        internal CancellationTokenSource? CaptureCts { get; private set; }

        // ➜ NOVO EVENTO
        public event Action? Saved;

        public MappingHubViewModel(IInputCaptureService captureService, IMappingStore mappingStore)
        {
            CaptureService = captureService;
            MappingStore = mappingStore;

            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Filter)) ApplyFilter();
            };

            ReloadCommand = new AsyncRelayCommand(
    async () =>
    {
        // 1. Carrega do disco em thread de fundo
        await LoadAsync().ConfigureAwait(false);

        // 2. Volta para a UI thread para mexer em bindings/comandos
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // reaplica filtro se houver
            if (!string.IsNullOrWhiteSpace(Filter))
                ApplyFilter();

            // recalcula conflitos (pode disparar NotifyCanExecuteChanged)
            RefreshConflicts();

            // notifica MainViewModel para recarregar MappingEngine/ViGEm
            Saved?.Invoke();
        });
    });


            ClearAllCommand = new RelayCommand(() =>
            {
                foreach (var it in Items) it.Assigned = PhysicalInput.None;
                RefreshConflicts();
            });

            SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
            CancelCommand = new RelayCommand(() => { /* fechar/navegar no host */ });

            NewProfileCommand = new RelayCommand(CreateNewProfile);

            _ = LoadAsync();
        }

        private void CreateNewProfile()
        {
            // gera um nome simples: perfil_1, perfil_2...
            string baseName = "perfil";
            int idx = 1;
            string candidate;

            do
            {
                candidate = $"{baseName}_{idx}";
                idx++;
            } while (AvailableProfiles.Contains(candidate, StringComparer.OrdinalIgnoreCase));

            CurrentProfileId = candidate;

            // zera os itens atuais (vai recarregar com tudo None)
            Items.Clear();
            _ = LoadAsync();
        }

        private void ApplyFilter()
        {
            var term = (Filter ?? "").Trim();
            FilteredItems.Clear();

            var query = string.IsNullOrEmpty(term)
                ? Items
                : Items.Where(i => i.ActionName.Contains(term, StringComparison.OrdinalIgnoreCase));

            foreach (var i in query) FilteredItems.Add(i);
        }

        public void RefreshConflicts()
        {
            var dups = Items
                .Where(i => i.Assigned != PhysicalInput.None)
                .GroupBy(i => i.Assigned)
                .Where(g => g.Count() > 1)
                .ToList();

            ConflictSummary = dups.Count == 0
                ? "Sem conflitos"
                : $"Conflitos: {string.Join(", ", dups.Select(g => $"{g.Key} ×{g.Count()}"))}";

            OnPropertyChanged(nameof(CanSave));
            (SaveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }

        private async Task LoadProfilesAsync()
        {
            AvailableProfiles.Clear();
            var profiles = await MappingStore.ListProfilesAsync(CancellationToken.None);
            foreach (var p in profiles)
                AvailableProfiles.Add(p);

            if (string.IsNullOrWhiteSpace(CurrentProfileId) && AvailableProfiles.Count > 0)
                CurrentProfileId = AvailableProfiles[0];
        }

        private async Task LoadAsync()
        {
            CaptureCts?.Cancel();
            CaptureCts = new CancellationTokenSource();

            if (AvailableProfiles.Count == 0 || string.IsNullOrWhiteSpace(CurrentProfileId))
                await LoadProfilesAsync();

            var actions = MappingStore.GetDefaultActions();
            Dictionary<string, PhysicalInput> loaded;

            try
            {
                loaded = new Dictionary<string, PhysicalInput>(StringComparer.OrdinalIgnoreCase);
                foreach (var (action, assigned) in await MappingStore.LoadAsync(CurrentProfileId, CaptureCts.Token))
                {
                    // último binding para a mesma ação vence (evita exceção de chave duplicada)
                    loaded[action] = assigned;
                }
            }
            catch
            {
                // Se der falha (arquivo bloqueado/corrompido), mantém a UI funcional com defaults
                loaded = new Dictionary<string, PhysicalInput>(StringComparer.OrdinalIgnoreCase);
            }

            // Prepara lista fora do thread de UI
            var newItems = actions
                .Select(a =>
                {
                    loaded.TryGetValue(a, out var phys);
                    return new MappingItemVM(a, phys, this);
                })
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items.Clear();
                FilteredItems.Clear();

                foreach (var item in newItems)
                    Items.Add(item);

                ApplyFilter();
                RefreshConflicts();
            }, DispatcherPriority.Background);
        }

        private async Task SaveAsync()
        {
            var map = Items.Select(i => (i.ActionName, i.Assigned)).ToArray();

            // salva no perfil atual (pode ser null => "mapping.json" default)
            await MappingStore.SaveAsync(CurrentProfileId, map, CancellationToken.None);

            // 🔄 recarrega lista de perfis SEM reatribuir a coleção
            var list = await MappingStore.ListProfilesAsync(CancellationToken.None);

            AvailableProfiles.Clear();
            foreach (var p in list)
                AvailableProfiles.Add(p);

            // se o perfil atual sumiu (foi renomeado, etc), garante um válido
            if (AvailableProfiles.Count > 0 &&
                (string.IsNullOrWhiteSpace(CurrentProfileId) ||
                 !AvailableProfiles.Contains(CurrentProfileId)))
            {
                CurrentProfileId = AvailableProfiles[0];
            }

            // avisa o MainViewModel pra recarregar o engine
            Saved?.Invoke();
        }
    }
}
