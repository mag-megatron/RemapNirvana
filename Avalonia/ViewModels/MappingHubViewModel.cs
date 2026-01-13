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
                    OnPropertyChanged(nameof(HasConflict));
                    _root.RefreshConflicts();
                }
            }
        }

        public string AssignedDisplay => Assigned == PhysicalInput.None ? "-" : Assigned.ToString();

        public string ConflictNote
            => Assigned != PhysicalInput.None &&
               _root.Items.Any(x => x != this && x.Assigned == Assigned)
               ? "Duplicado"
               : "";

        public bool HasConflict => !string.IsNullOrEmpty(ConflictNote);

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

        internal void RefreshConflictState()
        {
            OnPropertyChanged(nameof(ConflictNote));
            OnPropertyChanged(nameof(HasConflict));
        }
    }

    public partial class MappingHubViewModel : ObservableObject
    {
        [ObservableProperty] private string filter = "";
        [ObservableProperty] private string conflictSummary = "";

        // ? NOVO: lista de perfis + perfil atual
        public ObservableCollection<string> AvailableProfiles { get; } = new();
        [ObservableProperty]
        private string? currentProfileId;

        public ObservableCollection<MappingItemVM> Items { get; } = new();
        public ObservableCollection<MappingItemVM> FilteredItems { get; } = new();

        public PhysicalInput[] AvailableInputs { get; } =
            Enum.GetValues(typeof(PhysicalInput)).Cast<PhysicalInput>().ToArray();

        // Evita loop ao alterar CurrentProfileId internamente (ex.: ao recarregar a lista).
        private bool suppressProfileChangedHandling;

        public IRelayCommand ReloadCommand { get; }
        public IRelayCommand ClearAllCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand NewProfileCommand { get; }

        public bool CanSave => Items.All(i => string.IsNullOrEmpty(i.ConflictNote));

        public IInputCaptureService CaptureService { get; }
        public IMappingStore MappingStore { get; }

        internal CancellationTokenSource? CaptureCts { get; private set; }

        // ? NOVO EVENTO
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

            NewProfileCommand = new AsyncRelayCommand(CreateNewProfileAsync);

            _ = LoadAsync();
        }

        private async Task CreateNewProfileAsync()
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

            // Garante que o arquivo do novo perfil exista (gera defaults se preciso)
            await MappingStore.LoadAsync(candidate, CancellationToken.None);

            suppressProfileChangedHandling = true;
            CurrentProfileId = candidate;
            suppressProfileChangedHandling = false;

            // Recarrega a lista do disco (inclui o novo arquivo)
            await LoadProfilesAsync(CurrentProfileId);

            // zera os itens atuais (vai recarregar com defaults) e aplica em tempo real
            Items.Clear();
            await LoadAsync(refreshProfiles: false, raiseSaved: true);
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

            foreach (var item in Items)
            {
                item.RefreshConflictState();
            }

            ConflictSummary = dups.Count == 0
                ? "Sem conflitos"
                : $"Conflitos: {string.Join(", ", dups.Select(g => $"{g.Key} x{g.Count()}"))}";

            OnPropertyChanged(nameof(CanSave));
            (SaveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }

        partial void OnCurrentProfileIdChanged(string? value)
        {
            if (suppressProfileChangedHandling)
                return;

            // Selecao de perfil via UI: recarrega o mapeamento para esse perfil
            _ = LoadAsync(refreshProfiles: false, raiseSaved: true);
        }

        private async Task LoadProfilesAsync(string? preferredProfileId = null)
        {
            var desired = preferredProfileId ?? CurrentProfileId;

            AvailableProfiles.Clear();
            var profiles = await MappingStore.ListProfilesAsync(CancellationToken.None);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in profiles)
            {
                if (seen.Add(p))
                    AvailableProfiles.Add(p);
            }

            string? next = null;
            if (!string.IsNullOrWhiteSpace(desired) &&
                AvailableProfiles.Contains(desired, StringComparer.OrdinalIgnoreCase))
            {
                next = desired;
            }
            else if (AvailableProfiles.Count > 0)
            {
                next = AvailableProfiles[0];
            }

            if (!string.Equals(CurrentProfileId, next, StringComparison.Ordinal))
            {
                suppressProfileChangedHandling = true;
                CurrentProfileId = next;
                suppressProfileChangedHandling = false;
            }
        }

        private async Task LoadAsync(bool refreshProfiles = true, bool raiseSaved = false)
        {
            CaptureCts?.Cancel();
            CaptureCts = new CancellationTokenSource();

            if (refreshProfiles || AvailableProfiles.Count == 0)
                await LoadProfilesAsync(CurrentProfileId);

            if (string.IsNullOrWhiteSpace(CurrentProfileId))
                return;

            var actions = MappingStore.GetDefaultActions();
            Dictionary<string, PhysicalInput> loaded;

            try
            {
                loaded = new Dictionary<string, PhysicalInput>(StringComparer.OrdinalIgnoreCase);
                foreach (var (action, assigned) in await MappingStore.LoadAsync(CurrentProfileId, CaptureCts.Token))
                {
                    // Ultimo binding para a mesma acao vence (evita excecao de chave duplicada)
                    loaded[action] = assigned;
                }
            }
            catch
            {
                // Se der falha (arquivo bloqueado/corrompido), mantem a UI funcional com defaults
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

            if (raiseSaved)
                Saved?.Invoke();
        }

        private async Task SaveAsync()
        {
            var map = Items.Select(i => (i.ActionName, i.Assigned)).ToArray();

            // salva no perfil atual (pode ser null => "mapping.json" default)
            await MappingStore.SaveAsync(CurrentProfileId, map, CancellationToken.None);

            // atualiza lista e garante que o perfil atual continua selecionado
            await LoadProfilesAsync(CurrentProfileId);

            // avisa o MainViewModel pra recarregar o engine
            Saved?.Invoke();
        }
    }
}


