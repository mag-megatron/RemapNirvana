using AvaloniaUI.Hub;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Engine de mapeamento contínuo:
/// - Usa o snapshot de entrada (snap: nome → valor double)
/// - Aplica o dicionário de mapeamentos (PhysicalInput → ação lógica)
/// - Gera um estado final de saída (nome → float) compatível com o ViGEm.
///
/// Regras:
/// - Botões: sempre digitais (0 ou 1).
/// - Triggers:
///   - Se ação == "TriggerLeft"/"TriggerRight" → analógico 0..1.
///   - Qualquer outra ação → digital (0 ou 1, com threshold).
/// - Sticks:
///   - Se ação é "ThumbLX"/"ThumbLY"/"ThumbRX"/"ThumbRY" → analógico -1..1.
///   - Qualquer outra ação → digital (0 ou 1, com threshold e direção).
/// </summary>

namespace AvaloniaUI.ProgramCore
{
    public class MappingEngine
    {
        private readonly IMappingStore _store;

        // físico → ação lógica (ButtonA, LX+, etc)
        private Dictionary<PhysicalInput, string> _map = new();

        public MappingEngine(IMappingStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store)); ;
        }

        // Carrega JSON → dicionário interno;  profileId = null → usa o "mapping.json" padrão.
        public async Task LoadAsync(string? profileId = null, CancellationToken ct = default)
        {
            var loaded = await _store.LoadAsync(profileId, ct);

            var dict = new Dictionary<PhysicalInput, string>();

            foreach (var (action, assigned) in loaded)
            {
                if (assigned == PhysicalInput.None)
                    continue;

                // 👉 Política: o ÚLTIMO mapeamento para o mesmo input físico vence.
                // Se preferir manter o primeiro, é só trocar para if (dict.ContainsKey(assigned)) continue;
                if (dict.TryGetValue(assigned, out var existing))
                {
                    Debug.WriteLine(
                        $"[MappingEngine] Input físico duplicado: {assigned} já estava ligado a '{existing}', sobrescrevendo para '{action}'.");
                }

                dict[assigned] = action;
            }

            _map = dict;
        }

        // ------------------------------------------------------------
        // 1. Interpreta snapshot → ações lógicas ativadas
        // ------------------------------------------------------------

        /// <summary>
        /// Constrói o estado de saída final diretamente a partir do snapshot
        /// de entrada (snap) e do mapeamento físico→ação.
        /// </summary>
        public Dictionary<string, float> BuildOutput(IReadOnlyDictionary<string, double> snap)
        {
            var o = InitOutputState();

            foreach (var kv in _map)
            {
                var phys = kv.Key;
                var action = kv.Value;

                switch (phys)
                {
                    // --------------------------------------------------
                    // BOTÕES: sempre digitais
                    // --------------------------------------------------
                    case PhysicalInput.ButtonSouth:      // A
                        MapFromButton("A", action, snap, o);
                        break;
                    case PhysicalInput.ButtonEast:       // B
                        MapFromButton("B", action, snap, o);
                        break;
                    case PhysicalInput.ButtonWest:       // X
                        MapFromButton("X", action, snap, o);
                        break;
                    case PhysicalInput.ButtonNorth:      // Y
                        MapFromButton("Y", action, snap, o);
                        break;

                    case PhysicalInput.LeftBumper:       // LB
                        MapFromButton("LB", action, snap, o);
                        break;
                    case PhysicalInput.RightBumper:      // RB
                        MapFromButton("RB", action, snap, o);
                        break;

                    case PhysicalInput.LeftStickClick:   // L3
                        MapFromButton("L3", action, snap, o);
                        break;
                    case PhysicalInput.RightStickClick:  // R3
                        MapFromButton("R3", action, snap, o);
                        break;

                    case PhysicalInput.Back:             // View
                        MapFromButton("View", action, snap, o);
                        break;
                    case PhysicalInput.Start:            // Menu
                        MapFromButton("Menu", action, snap, o);
                        break;

                    case PhysicalInput.DPadUp:
                        MapFromButton("DUp", action, snap, o);
                        break;
                    case PhysicalInput.DPadDown:
                        MapFromButton("DDown", action, snap, o);
                        break;
                    case PhysicalInput.DPadLeft:
                        MapFromButton("DLeft", action, snap, o);
                        break;
                    case PhysicalInput.DPadRight:
                        MapFromButton("DRight", action, snap, o);
                        break;

                    // --------------------------------------------------
                    // TRIGGERS: podem ser analógicos ou digitais
                    // --------------------------------------------------
                    case PhysicalInput.LeftTrigger:
                        MapFromTrigger("LT", action, snap, o);
                        break;
                    case PhysicalInput.RightTrigger:
                        MapFromTrigger("RT", action, snap, o);
                        break;

                    // --------------------------------------------------
                    // AXES: podem ser analógicos ou digitais
                    // --------------------------------------------------
                    case PhysicalInput.LeftStickX_Pos:
                        MapFromAxis("LX", +1, action, snap, o);
                        break;
                    case PhysicalInput.LeftStickX_Neg:
                        MapFromAxis("LX", -1, action, snap, o);
                        break;

                    case PhysicalInput.LeftStickY_Pos:
                        MapFromAxis("LY", +1, action, snap, o);
                        break;
                    case PhysicalInput.LeftStickY_Neg:
                        MapFromAxis("LY", -1, action, snap, o);
                        break;

                    case PhysicalInput.RightStickX_Pos:
                        MapFromAxis("RX", +1, action, snap, o);
                        break;
                    case PhysicalInput.RightStickX_Neg:
                        MapFromAxis("RX", -1, action, snap, o);
                        break;

                    case PhysicalInput.RightStickY_Pos:
                        MapFromAxis("RY", +1, action, snap, o);
                        break;
                    case PhysicalInput.RightStickY_Neg:
                        MapFromAxis("RY", -1, action, snap, o);
                        break;

                    default:
                        break;
                }
            }

            return o;
        }


        // ------------------------------------------------------------
        // 2. Constrói estado XInput a partir das ações lógicas
        // ------------------------------------------------------------
        public Dictionary<string, float> InitOutputState()
        {
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                // zeros padrão:
                ["ButtonA"] = 0,
                ["ButtonB"] = 0,
                ["ButtonX"] = 0,
                ["ButtonY"] = 0,

                ["ButtonLeftShoulder"] = 0,
                ["ButtonRightShoulder"] = 0,

                ["ButtonStart"] = 0,
                ["ButtonBack"] = 0,

                ["ThumbLPressed"] = 0,
                ["ThumbRPressed"] = 0,

                ["DPadUp"] = 0,
                ["DPadDown"] = 0,
                ["DPadLeft"] = 0,
                ["DPadRight"] = 0,

                ["TriggerLeft"] = 0,
                ["TriggerRight"] = 0,

                ["ThumbLX"] = 0,
                ["ThumbLY"] = 0,
                ["ThumbRX"] = 0,
                ["ThumbRY"] = 0,
            };

        }
        private static bool IsTriggerOutput(string action) =>
         string.Equals(action, "TriggerLeft", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(action, "TriggerRight", StringComparison.OrdinalIgnoreCase);

        private static bool IsAxisOutput(string action) =>
            string.Equals(action, "ThumbLX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbLY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbRX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "ThumbRY", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------
        // Map a partir de BOTÃO (snapKey → 0/1)
        // ------------------------------------------------------------
        private static void MapFromButton(
            string snapKey,
            string action,
            IReadOnlyDictionary<string, double> snap,
            Dictionary<string, float> o)
        {
            if (!snap.TryGetValue(snapKey, out var v))
                return;

            var pressed = v >= 0.5;
            if (!pressed)
                return;

            if (!o.ContainsKey(action))
                return; // ação desconhecida → ignora

            o[action] = 1f;
        }

        // ------------------------------------------------------------
        // Map a partir de TRIGGER (snapKey → 0..1)
        // ------------------------------------------------------------
        private static void MapFromTrigger(
            string snapKey,
            string action,
            IReadOnlyDictionary<string, double> snap,
            Dictionary<string, float> o)
        {
            var v = snap.TryGetValue(snapKey, out var raw) ? raw : 0.0;

            if (IsTriggerOutput(action))
            {
                if (!o.ContainsKey(action)) return;
                var f = (float)Math.Clamp(v, 0.0, 1.0);
                // pega o maior valor (caso múltiplos mapeamentos apontem pro mesmo trigger)
                if (f > o[action])
                    o[action] = f;
            }
            else
            {
                // Usa como botão (digital)
                if (v >= 0.5 && o.ContainsKey(action))
                    o[action] = 1f;
            }
        }

        // ------------------------------------------------------------
        // Map a partir de EIXO (snapKey → -1..1)
        // direction: +1 (Pos) ou -1 (Neg)
        // ------------------------------------------------------------
        private static void MapFromAxis(
            string snapKey,
            int direction,
            string action,
            IReadOnlyDictionary<string, double> snap,
            Dictionary<string, float> o)
        {
            if (!snap.TryGetValue(snapKey, out var v))
                return;

            // filtra pelo lado correto do eixo
            if (direction > 0 && v <= 0.0) return;
            if (direction < 0 && v >= 0.0) return;

            if (IsAxisOutput(action))
            {
                if (!o.ContainsKey(action)) return;

                // analógico contínuo
                float contrib = (float)v;
                if (direction > 0)
                    contrib = Math.Max(0f, contrib);
                else
                    contrib = Math.Min(0f, contrib);

                var old = o[action];
                // Prioriza o maior módulo (se várias fontes mapearem pro mesmo eixo)
                if (Math.Abs(contrib) > Math.Abs(old))
                    o[action] = contrib;
            }
            else
            {
                // modo digital: threshold
                const double threshold = 0.6;
                bool active = direction > 0
                    ? v >= threshold
                    : v <= -threshold;

                if (active && o.ContainsKey(action))
                    o[action] = 1f;
            }
        }
    }
}
