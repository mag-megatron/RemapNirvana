using AvaloniaUI.Hub;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Continuous mapping engine that takes the SDL snapshot (name -> double),
/// applies the configured physical-to-logical map, and produces a ViGEm-compatible
/// output state (name -> float).
///
/// Rules:
/// - Buttons are always digital (0 or 1).
/// - Triggers: TriggerLeft/TriggerRight remain analog 0..1; any other action is digital.
/// - Sticks: ThumbLX/ThumbLY/ThumbRX/ThumbRY remain analog -1..1; any other action is digital.
/// </summary>
namespace AvaloniaUI.ProgramCore
{
    /// <summary>
    /// Holds the loaded physical-to-logical bindings and converts input snapshots
    /// into the aggregated output state expected by ViGEm or other output services.
    /// </summary>
    public class MappingEngine
    {
        private readonly IMappingStore _store;

        // physical input -> logical action (ButtonA, LX+, etc.)
        private Dictionary<PhysicalInput, string> _map = new();
        // full physical state; incoming snapshots are deltas
        private readonly Dictionary<string, double> _physicalState = new(StringComparer.Ordinal);

        public MappingEngine(IMappingStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Loads the mapping JSON into the in-memory dictionary; when profileId is null,
        /// the default mapping file is used. The last mapping for the same physical input wins.
        /// </summary>
        public async Task LoadAsync(string? profileId = null, CancellationToken ct = default)
        {
            var loaded = await _store.LoadAsync(profileId, ct);

            var dict = new Dictionary<PhysicalInput, string>();

            foreach (var (action, assigned) in loaded)
            {
                if (assigned == PhysicalInput.None)
                    continue;

                if (dict.TryGetValue(assigned, out var existing))
                {
                    Debug.WriteLine(
                        $"[MappingEngine] Duplicate physical input: {assigned} was '{existing}', overwriting with '{action}'.");
                }

                dict[assigned] = action;
            }

            _map = dict;
        }

        // ------------------------------------------------------------
        // 1. Interpret snapshot -> active logical actions
        // ------------------------------------------------------------

        /// <summary>
        /// Builds the final output state from the incoming snapshot and the configured
        /// physical-to-action mapping.
        /// </summary>
        public Dictionary<string, float> BuildOutput(IReadOnlyDictionary<string, double> snap)
        {
            // Snapshots are deltas; keep a full state to avoid clearing inputs absent from this batch.
            foreach (var (name, value) in snap)
            {
                _physicalState[name] = value;
            }

            var state = (IReadOnlyDictionary<string, double>)_physicalState;
            var o = InitOutputState();

            // Detect axes that have only one side mapped; in that case use the full axis (both signs).
            var axisSides = new Dictionary<string, (bool hasPos, bool hasNeg)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _map)
            {
                if (!IsAxisOutput(kv.Value))
                    continue;

                switch (kv.Key)
                {
                    case PhysicalInput.LeftStickX_Pos:
                    case PhysicalInput.LeftStickY_Pos:
                    case PhysicalInput.RightStickX_Pos:
                    case PhysicalInput.RightStickY_Pos:
                        axisSides[kv.Value] = axisSides.TryGetValue(kv.Value, out var s)
                            ? (true, s.hasNeg)
                            : (true, false);
                        break;
                    case PhysicalInput.LeftStickX_Neg:
                    case PhysicalInput.LeftStickY_Neg:
                    case PhysicalInput.RightStickX_Neg:
                    case PhysicalInput.RightStickY_Neg:
                        axisSides[kv.Value] = axisSides.TryGetValue(kv.Value, out var s2)
                            ? (s2.hasPos, true)
                            : (false, true);
                        break;
                }
            }

            foreach (var kv in _map)
            {
                var phys = kv.Key;
                var action = kv.Value;
                var sides = axisSides.TryGetValue(action, out var s) ? s : (false, false);
                bool fullAxis = IsAxisOutput(action) && !(s.hasPos && s.hasNeg);

                switch (phys)
                {
                    // --------------------------------------------------
                    // BUTTONS: always digital
                    // --------------------------------------------------
                    case PhysicalInput.ButtonSouth:      // A
                        MapFromButton("A", action, state, o);
                        break;
                    case PhysicalInput.ButtonEast:       // B
                        MapFromButton("B", action, state, o);
                        break;
                    case PhysicalInput.ButtonWest:       // X
                        MapFromButton("X", action, state, o);
                        break;
                    case PhysicalInput.ButtonNorth:      // Y
                        MapFromButton("Y", action, state, o);
                        break;

                    case PhysicalInput.LeftBumper:       // LB
                        MapFromButton("LB", action, state, o);
                        break;
                    case PhysicalInput.RightBumper:      // RB
                        MapFromButton("RB", action, state, o);
                        break;

                    case PhysicalInput.LeftStickClick:   // L3
                        MapFromButton("L3", action, state, o);
                        break;
                    case PhysicalInput.RightStickClick:  // R3
                        MapFromButton("R3", action, state, o);
                        break;

                    case PhysicalInput.Back:             // View
                        MapFromButton("View", action, state, o);
                        break;
                    case PhysicalInput.Start:            // Menu
                        MapFromButton("Menu", action, state, o);
                        break;

                    case PhysicalInput.DPadUp:
                        MapFromButton("DUp", action, state, o);
                        break;
                    case PhysicalInput.DPadDown:
                        MapFromButton("DDown", action, state, o);
                        break;
                    case PhysicalInput.DPadLeft:
                        MapFromButton("DLeft", action, state, o);
                        break;
                    case PhysicalInput.DPadRight:
                        MapFromButton("DRight", action, state, o);
                        break;

                    // --------------------------------------------------
                    // TRIGGERS: can be analog or digital
                    // --------------------------------------------------
                    case PhysicalInput.LeftTrigger:
                        MapFromTrigger("LT", action, state, o);
                        break;
                    case PhysicalInput.RightTrigger:
                        MapFromTrigger("RT", action, state, o);
                        break;

                    // --------------------------------------------------
                    // AXES: can be analog or digital
                    // --------------------------------------------------
                    case PhysicalInput.LeftStickX_Pos:
                        MapFromAxis("LX", +1, action, state, o, fullAxis);
                        break;
                    case PhysicalInput.LeftStickX_Neg:
                        MapFromAxis("LX", -1, action, state, o, fullAxis);
                        break;

                    case PhysicalInput.LeftStickY_Pos:
                        MapFromAxis("LY", +1, action, state, o, fullAxis);
                        break;
                    case PhysicalInput.LeftStickY_Neg:
                        MapFromAxis("LY", -1, action, state, o, fullAxis);
                        break;

                    case PhysicalInput.RightStickX_Pos:
                        MapFromAxis("RX", +1, action, state, o, fullAxis);
                        break;
                    case PhysicalInput.RightStickX_Neg:
                        MapFromAxis("RX", -1, action, state, o, fullAxis);
                        break;

                    case PhysicalInput.RightStickY_Pos:
                        MapFromAxis("RY", +1, action, state, o, fullAxis);
                        break;
                    case PhysicalInput.RightStickY_Neg:
                        MapFromAxis("RY", -1, action, state, o, fullAxis);
                        break;

                    default:
                        break;
                }
            }

            return o;
        }


        // ------------------------------------------------------------
        // 2. Build XInput state from logical actions
        // ------------------------------------------------------------
        /// <summary>
        /// Creates a zeroed ViGEm/XInput-compatible output state.
        /// </summary>
        public Dictionary<string, float> InitOutputState()
        {
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                // default zeros:
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
        // Map from BUTTON (snapKey -> 0/1)
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
                return; // unknown action -> ignore

            o[action] = 1f;
        }

        // ------------------------------------------------------------
        // Map from TRIGGER (snapKey -> 0..1)
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
                // take the largest value (if multiple mappings target the same trigger)
                if (f > o[action])
                    o[action] = f;
            }
            else
            {
                // treat as digital button
                if (v >= 0.5 && o.ContainsKey(action))
                    o[action] = 1f;
            }
        }

        // ------------------------------------------------------------
        // Map from AXIS (snapKey -> -1..1)
        // direction: +1 (Pos) or -1 (Neg)
        // ------------------------------------------------------------
        private static void MapFromAxis(
            string snapKey,
            int direction,
            string action,
            IReadOnlyDictionary<string, double> snap,
            Dictionary<string, float> o,
            bool fullAxis)
        {
            if (!snap.TryGetValue(snapKey, out var v))
                return;

            if (IsAxisOutput(action))
            {
                if (!o.ContainsKey(action)) return;

                float contrib;
                if (fullAxis)
                {
                    contrib = (float)v;
                }
                else
                {
                    // filter by correct side of the axis
                    if (direction > 0 && v <= 0.0) return;
                    if (direction < 0 && v >= 0.0) return;

                    contrib = (float)v;
                    if (direction > 0)
                        contrib = Math.Max(0f, contrib);
                    else
                        contrib = Math.Min(0f, contrib);
                }

                var old = o[action];
                // prefer the value with the largest magnitude if multiple sources map to the same axis
                if (Math.Abs(contrib) > Math.Abs(old))
                    o[action] = contrib;
            }
            else
            {
                // digital mode with threshold
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
