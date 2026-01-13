using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaUI.Hub;

namespace AvaloniaUI.Services
{
    /// <summary>
    /// Implementacao de IMappingStore que armazena mapeamentos em arquivos JSON
    /// na pasta de dados da aplicacao.
    /// </summary>
    public sealed class JsonMappingStore : IMappingStore
    {
        private readonly string _dir;
        private readonly string _defaultPath;

        // Opcoes de serializacao JSON: indentado para leitura humana, camelCase para propriedades.
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Inicializa uma nova instancia do JsonMappingStore.
        /// </summary>
        /// <param name="filePath">Caminho de arquivo opcional para o mapeamento padrao. Se nulo, usa o caminho padrao.</param>
        public JsonMappingStore(string? filePath = null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _dir = Path.Combine(appData, "NirvanaRemap");

            // Garante que o diretorio exista.
            Directory.CreateDirectory(_dir);

            _defaultPath = filePath ?? Path.Combine(_dir, "mapping.json");
        }

        // ----------------------------------------------------------
        // Acoes Padrao
        // ----------------------------------------------------------

        /// <summary>
        /// Obtem a lista de nomes de acoes padrao que o sistema reconhece.
        /// </summary>
        /// <returns>Um array de strings com os nomes das acoes.</returns>
        public string[] GetDefaultActions()
        {
            return GetBuiltInDefaultMapping()
                .Select(x => x.action)
                .Distinct()
                .ToArray();
        }



        private static (string action, PhysicalInput assigned)[] GetBuiltInDefaultMapping()
        {
            return new[]
            {
        // BOTOES
        ("ButtonA", PhysicalInput.ButtonSouth),
        ("ButtonB", PhysicalInput.ButtonEast),
        ("ButtonX", PhysicalInput.ButtonWest),
        ("ButtonY", PhysicalInput.ButtonNorth),

        ("ButtonLeftShoulder", PhysicalInput.LeftBumper),
        ("ButtonRightShoulder", PhysicalInput.RightBumper),

        ("ButtonStart", PhysicalInput.Start),
        ("ButtonBack", PhysicalInput.Back),

        ("ThumbLPressed", PhysicalInput.LeftStickClick),
        ("ThumbRPressed", PhysicalInput.RightStickClick),

        ("DPadUp", PhysicalInput.DPadUp),
        ("DPadDown", PhysicalInput.DPadDown),
        ("DPadLeft", PhysicalInput.DPadLeft),
        ("DPadRight", PhysicalInput.DPadRight),

        // TRIGGERS
        ("TriggerLeft", PhysicalInput.LeftTrigger),
        ("TriggerRight", PhysicalInput.RightTrigger),

        // STICKS analogicos (mapeia ambos os lados para o mesmo eixo)
        ("ThumbLX", PhysicalInput.LeftStickX_Pos),
        ("ThumbLX", PhysicalInput.LeftStickX_Neg),
        ("ThumbLY", PhysicalInput.LeftStickY_Pos),
        ("ThumbLY", PhysicalInput.LeftStickY_Neg),

        ("ThumbRX", PhysicalInput.RightStickX_Pos),
        ("ThumbRX", PhysicalInput.RightStickX_Neg),
        ("ThumbRY", PhysicalInput.RightStickY_Pos),
        ("ThumbRY", PhysicalInput.RightStickY_Neg)
    };
        }
        // ----------------------------------------------------------
        // Listagem de Perfis
        // ----------------------------------------------------------

        /// <summary>
        /// Lista os perfis de mapeamento disponiveis (ate 5 arquivos JSON, excluindo o default).
        /// </summary>
        /// <param name="ct">Token de cancelamento.</param>
        /// <returns>Um Task contendo um array de IDs de perfil (nomes de arquivo sem extensao).</returns>
        public Task<string[]> ListProfilesAsync(CancellationToken ct)
        {
            // Pega todos os arquivos .json na pasta.
            var files = Directory
                .GetFiles(_dir, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            // Garante que o perfil "mapping" (default) esteja sempre na primeira posicao da lista
            // e seja chamado de "mapping" ou "default".
            var defaultProfileName = "mapping";
            if (files.Contains(defaultProfileName))
            {
                files.Remove(defaultProfileName);
            }
            files.Insert(0, defaultProfileName);

            // Remove duplicatas preservando a ordem (default primeiro)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = files.Where(name => seen.Add(name)).ToArray();

            return Task.FromResult(ordered);
        }

        // ----------------------------------------------------------
        // Helpers de Caminho
        // ----------------------------------------------------------

        /// <summary>
        /// Resolve o ID do perfil para um caminho de arquivo seguro.
        /// </summary>
        /// <param name="profileId">O ID do perfil (nome de arquivo).</param>
        /// <returns>O caminho de arquivo completo para o perfil.</returns>
        private string ResolvePath(string? profileId)
        {
            // Trata IDs vazios ou "default/mapping" como o caminho padrao.
            if (string.IsNullOrWhiteSpace(profileId)
                || profileId.Equals("mapping", StringComparison.OrdinalIgnoreCase)
                || profileId.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                return _defaultPath;
            }

            // Sanitiza o nome de arquivo para remover caracteres invalidos.
            var safe = string.Join("_", profileId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            // Caso o nome sanitizado fique vazio.
            if (string.IsNullOrWhiteSpace(safe))
            {
                safe = "mapping_alt";
            }

            return Path.Combine(_dir, safe + ".json");
        }

        // ----------------------------------------------------------
        // Load / Save com Perfil
        // ----------------------------------------------------------

        /// <summary>
        /// Carrega o mapeamento de um perfil especifico de forma assincrona.
        /// </summary>
        /// <param name="profileId">O ID do perfil a carregar.</param>
        /// <param name="ct">Token de cancelamento.</param>
        /// <returns>Um array de tuplas (acao, input fisico) do mapeamento carregado.</returns>
        public async Task<(string action, PhysicalInput assigned)[]> 
        LoadAsync(
                string? profileId,
                CancellationToken ct
                 )
        {
            var path = ResolvePath(profileId);

            // 1) Se nao existe, gera default e salva
            if (!File.Exists(path))
            {
                var def = GetBuiltInDefaultMapping();
                await SaveAsync(profileId, def, ct);
                return def;
            }

            try
            {
                List<Entry> data;
                await using (var fs = File.OpenRead(path))
                {
                    data = await JsonSerializer
                        .DeserializeAsync<List<Entry>>(fs, _json, ct)
                        .ConfigureAwait(false) ?? new();
                }

                var migrated = MigrateLegacyActions(data);
                var axisCompleted = EnsureAxisPairs(migrated.entries);
                var defaultsCompleted = MergeWithDefaults(axisCompleted.entries);

                if (migrated.changed || axisCompleted.changed || defaultsCompleted.changed)
                {
                    var tuples = defaultsCompleted.entries
                        .Select(e => (e.Action, e.Assigned))
                        .ToArray();

                    // Atualiza o arquivo para evitar reprocessamento futuro
                    await SaveAsync(profileId, tuples, ct);
                    return tuples;
                }

                return data.Select(e => (e.Action, e.Assigned)).ToArray();
            }
            catch (JsonException)
            {
                // 2) JSON zoado: faz backup e recria o default
                try
                {
                    var backup = path + ".bak";
                    if (File.Exists(backup))
                        File.Delete(backup);
                    File.Move(path, backup);
                }
                catch
                {
                    // se falhar o backup, ignora e segue
                }

                var def = GetBuiltInDefaultMapping();
                await SaveAsync(profileId, def, ct);
                return def;
            }
            catch
            {
                // 3) Qualquer outro erro (IO/lock): usa default em mem?ria para manter a UI funcional
                var def = GetBuiltInDefaultMapping();
                try
                {
                    await SaveAsync(profileId, def, ct);
                }
                catch
                {
                    // ignora falha de grava??o
                }
                return def;
            }
        }

        /// <summary>
        /// Salva o mapeamento fornecido para um perfil especifico de forma assincrona.
        /// </summary>
        /// <param name="profileId">O ID do perfil onde salvar.</param>
        /// <param name="map">O array de tuplas (acao, input fisico) a ser salvo.</param>
        /// <param name="ct">Token de cancelamento.</param>
        public async Task SaveAsync(
            string? profileId,
            (string action, PhysicalInput assigned)[] map,
            CancellationToken ct)
        {
            var path = ResolvePath(profileId);

            // Normaliza eixos para sempre salvar ambos os lados (pos/neg),
            // evitando inversoes a cada carregamento.
            var normalized = NormalizeAxisEntries(map);

            // Converte o array de tuplas para uma lista de objetos Entry para serializacao.
            var list = new List<Entry>(normalized.Count);
            foreach (var (a, p) in normalized)
            {
                list.Add(new Entry { Action = a, Assigned = p });
            }

            // Cria/sobrescreve o arquivo. O 'using var' garante o descarte.
            await using var fs = File.Create(path);
            await JsonSerializer.SerializeAsync(fs, list, _json, ct);
        }

        // ----------------------------------------------------------
        // Classe auxiliar de serializacao
        // ----------------------------------------------------------

        /// <summary>
        /// Representa uma unica entrada de mapeamento para fins de serializacao/desserializacao JSON.
        /// </summary>
        private sealed class Entry
        {
            public string Action { get; set; } = "";
            public PhysicalInput Assigned { get; set; } = PhysicalInput.None;
        }

        // Converte nomes antigos de acoes de eixo (LX+/LX-/...) para os novos
        // nomes continuos (ThumbLX/ThumbLY/...), preservando compatibilidade.
        private static (bool changed, List<Entry> entries) MigrateLegacyActions(List<Entry> data)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LX+"] = "ThumbLX",
                ["LX-"] = "ThumbLX",
                ["LY+"] = "ThumbLY",
                ["LY-"] = "ThumbLY",
                ["RX+"] = "ThumbRX",
                ["RX-"] = "ThumbRX",
                ["RY+"] = "ThumbRY",
                ["RY-"] = "ThumbRY",
            };

            var changed = false;
            var result = new List<Entry>(data.Count);

            foreach (var e in data)
            {
                var action = map.TryGetValue(e.Action, out var newer)
                    ? newer
                    : e.Action;

                if (!changed && !string.Equals(action, e.Action, StringComparison.Ordinal))
                    changed = true;

                result.Add(new Entry
                {
                    Action = action,
                    Assigned = e.Assigned
                });
            }

            return (changed, result);
        }

        // Garante que, se um eixo analogico estiver presente, ambos os lados (pos/neg)
        // tenham entradas, evitando que apenas direita/baixo funcionem.
        private static (bool changed, List<Entry> entries) EnsureAxisPairs(List<Entry> data)
        {
            var pairs = new (string action, PhysicalInput pos, PhysicalInput neg)[]
            {
                ("ThumbLX", PhysicalInput.LeftStickX_Pos, PhysicalInput.LeftStickX_Neg),
                ("ThumbLY", PhysicalInput.LeftStickY_Pos, PhysicalInput.LeftStickY_Neg),
                ("ThumbRX", PhysicalInput.RightStickX_Pos, PhysicalInput.RightStickX_Neg),
                ("ThumbRY", PhysicalInput.RightStickY_Pos, PhysicalInput.RightStickY_Neg),
            };

            var changed = false;
            var list = new List<Entry>(data);

            foreach (var (action, pos, neg) in pairs)
            {
                bool hasPos = list.Any(e => string.Equals(e.Action, action, StringComparison.OrdinalIgnoreCase) && e.Assigned == pos);
                bool hasNeg = list.Any(e => string.Equals(e.Action, action, StringComparison.OrdinalIgnoreCase) && e.Assigned == neg);

                // So corrige se o eixo ja esta mapeado para algum lado (evita recriar se o usuario limpou ambos)
                if (!(hasPos || hasNeg))
                    continue;

                if (!hasPos)
                {
                    list.Add(new Entry { Action = action, Assigned = pos });
                    changed = true;
                }

                if (!hasNeg)
                {
                    list.Add(new Entry { Action = action, Assigned = neg });
                    changed = true;
                }
            }

            return (changed, list);
        }

        // Garante que todas as acoes padrao tenham algum binding (default completo).
        // Se um perfil existente estiver faltando acoes, preenche com o mapeamento built-in.
        private static (bool changed, List<Entry> entries) MergeWithDefaults(List<Entry> data)
        {
            var changed = false;
            var dict = new Dictionary<string, PhysicalInput>(StringComparer.OrdinalIgnoreCase);

            // preserva ultimo binding encontrado para cada acao, ignorando entradas None (considera como ausente)
            foreach (var e in data)
            {
                if (e.Assigned == PhysicalInput.None)
                {
                    changed = true; // vamos preencher com default
                    continue;
                }

                dict[e.Action] = e.Assigned;
            }

            foreach (var (action, phys) in GetBuiltInDefaultMapping())
            {
                if (!dict.ContainsKey(action))
                {
                    dict[action] = phys;
                    changed = true;
                }
            }

            var result = dict.Select(kv => new Entry { Action = kv.Key, Assigned = kv.Value }).ToList();
            return (changed, result);
        }

        // ----------------------------------------------------------
        // Normalizacao de eixos antes de salvar
        // ----------------------------------------------------------

        private static bool IsAxisAction(string action) =>
            string.Equals(action, "ThumbLX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbLY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbRX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbRY", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetAxisPair(PhysicalInput input, out PhysicalInput counterpart)
        {
            switch (input)
            {
                case PhysicalInput.LeftStickX_Pos:
                    counterpart = PhysicalInput.LeftStickX_Neg; return true;
                case PhysicalInput.LeftStickX_Neg:
                    counterpart = PhysicalInput.LeftStickX_Pos; return true;
                case PhysicalInput.LeftStickY_Pos:
                    counterpart = PhysicalInput.LeftStickY_Neg; return true;
                case PhysicalInput.LeftStickY_Neg:
                    counterpart = PhysicalInput.LeftStickY_Pos; return true;
                case PhysicalInput.RightStickX_Pos:
                    counterpart = PhysicalInput.RightStickX_Neg; return true;
                case PhysicalInput.RightStickX_Neg:
                    counterpart = PhysicalInput.RightStickX_Pos; return true;
                case PhysicalInput.RightStickY_Pos:
                    counterpart = PhysicalInput.RightStickY_Neg; return true;
                case PhysicalInput.RightStickY_Neg:
                    counterpart = PhysicalInput.RightStickY_Pos; return true;
                default:
                    counterpart = default;
                    return false;
            }
        }

        private static List<(string action, PhysicalInput assigned)> NormalizeAxisEntries(
            (string action, PhysicalInput assigned)[] map)
        {
            var result = new List<(string, PhysicalInput)>();

            void AddIfMissing(string action, PhysicalInput input)
            {
                if (!result.Any(e =>
                        string.Equals(e.Item1, action, StringComparison.OrdinalIgnoreCase) &&
                        e.Item2 == input))
                {
                    result.Add((action, input));
                }
            }

            foreach (var (action, assigned) in map)
            {
                if (IsAxisAction(action) && TryGetAxisPair(assigned, out var other))
                {
                    // Deixa o lado selecionado por ultimo para que ele prevaleca na UI
                    AddIfMissing(action, other);
                    AddIfMissing(action, assigned);
                }
                else
                {
                    AddIfMissing(action, assigned);
                }
            }

            return result;
        }
    }
}

