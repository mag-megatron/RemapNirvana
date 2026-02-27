# API Reference

This document provides technical reference for the key APIs and services in NirvanaRemap. Since this is a desktop application (not a web API), this focuses on **public classes and methods** you can use when extending or integrating with the system.

---

## Core Interfaces

### IGamepadService

Interface for gamepad input capture services.

```csharp
namespace Core.Interfaces;

public interface IGamepadService
{
    /// <summary>
    /// Fired when new controller input is received.
    /// </summary>
    event EventHandler<ControllerInput> InputReceived;
    
    /// <summary>
    /// Fired when gamepad connection state changes.
    /// </summary>
    event EventHandler<bool> ConnectionChanged;
    
    /// <summary>
    /// Fired on each polling cycle with complete gamepad state.
    /// </summary>
    event EventHandler<GamepadState> StateChanged;
    
    /// <summary>
    /// Indicates if a gamepad is currently connected.
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Starts listening for gamepad inputs.
    /// </summary>
    void StartListening();
    
    /// <summary>
    /// Stops listening and releases resources.
    /// </summary>
    void StopListening();
    
    /// <summary>
    /// Updates calibration settings for axis deadzone/sensitivity.
    /// </summary>
    void UpdateCalibration(CalibrationSettings settings);
}
```

**Example Usage:**
```csharp
var gamepadService = serviceProvider.GetRequiredService<IGamepadService>();
gamepadService.InputReceived += (sender, input) =>
{
    Console.WriteLine($"Button: {input.ButtonName}, Value: {input.Value}");
};
gamepadService.StartListening();
```

---

### IOutputService

Interface for virtual controller output.

```csharp
namespace Core.Interfaces;

public interface IOutputService
{
    /// <summary>
    /// Applies a single mapped output to the virtual controller.
    /// </summary>
    /// <param name="output">The output to apply.</param>
    void Apply(MappedOutput output);
    
    /// <summary>
    /// Applies the complete output state atomically.
    /// Prefer this over multiple Apply() calls for performance.
    /// </summary>
    /// <param name="outputState">
    /// Dictionary of output names to values (e.g., "ButtonA" → 1.0).
    /// </param>
    void ApplyAll(Dictionary<string, float> outputState);
    
    /// <summary>
    /// Connects the virtual controller device.
    /// </summary>
    void Connect();
    
    /// <summary>
    /// Disconnects the virtual controller device.
    /// </summary>
    void Disconnect();
    
    /// <summary>
    /// Ensures the virtual controller is connected (idempotent).
    /// </summary>
    void EnsureConnected();
    
    /// <summary>
    /// Indicates if the virtual controller is connected.
    /// </summary>
    bool IsConnected { get; }
}
```

**Example Usage:**
```csharp
var vigemOutput = serviceProvider.GetRequiredService<IOutputService>();
vigemOutput.Connect();

var state = new Dictionary<string, float>
{
    ["ButtonA"] = 1.0f,
    ["ThumbLX"] = 0.8f,
    ["ThumbLY"] = -0.5f
};
vigemOutput.ApplyAll(state);
```

---

### IHidHideService

Interface for controlling HidHide device hiding.

```csharp
namespace Core.Interfaces;

public interface IHidHideService
{
    /// <summary>
    /// Checks if HidHide is installed on the system.
    /// </summary>
    Task<bool> IsInstalledAsync();
    
    /// <summary>
    /// Adds an application to the HidHide whitelist.
    /// </summary>
    /// <param name="executablePath">Full path to the executable.</param>
    Task AddApplicationAsync(string executablePath);
    
    /// <summary>
    /// Enables global hiding (devices in blacklist become hidden).
    /// </summary>
    Task EnableHidingAsync();
    
    /// <summary>
    /// Adds a device to the blacklist (hidden from games).
    /// </summary>
    /// <param name="deviceInstancePath">
    /// Device instance path (e.g., from SDL_GetGamepadPath).
    /// </param>
    Task AddDeviceAsync(string deviceInstancePath);
    
    /// <summary>
    /// Removes a device from the blacklist (no longer hidden).
    /// </summary>
    /// <param name="deviceInstancePath">Device instance path.</param>
    Task RemoveDeviceAsync(string deviceInstancePath);
}
```

**Example Usage:**
```csharp
var hidHide = serviceProvider.GetRequiredService<IHidHideService>();

if (await hidHide.IsInstalledAsync())
{
    await hidHide.AddApplicationAsync(@"C:\Program Files\NirvanaRemap\NirvanaRemap.exe");
    await hidHide.EnableHidingAsync();
    await hidHide.AddDeviceAsync(@"HID\VID_2345&PID_E00B\...");
    Console.WriteLine("Physical device is now hidden from games.");
}
```

---

## Application Layer Services

### GamepadVirtualizationOrchestrator

Orchestrates HidHide to ensure physical devices are hidden while the app can still access them.

```csharp
namespace ApplicationLayer.Services;

public sealed class GamepadVirtualizationOrchestrator
{
    /// <summary>
    /// Configures HidHide to hide physical devices from games while
    /// allowing this application to see them.
    /// </summary>
    /// <param name="devicesToHide">
    /// Collection of device instance paths to hide.
    /// </param>
    /// <returns>
    /// True if virtualization was successfully enabled, false if HidHide
    /// is not installed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the application executable path cannot be determined.
    /// </exception>
    Task<bool> EnsureVirtualIsPrimaryAsync(IEnumerable<string> devicesToHide);
    
    /// <summary>
    /// Removes devices from the HidHide blacklist (unhides them).
    /// </summary>
    /// <param name="devicesToUnhide">
    /// Collection of device instance paths to unhide.
    /// </param>
    /// <returns>
    /// True if devices were unhidden, false if HidHide is not installed.
    /// </returns>
    Task<bool> DisableVirtualizationAsync(IEnumerable<string> devicesToUnhide);
}
```

**Example Usage:**
```csharp
var orchestrator = new GamepadVirtualizationOrchestrator(hidHideService);

var devices = new[] { @"HID\VID_2345&PID_E00B\..." };
bool success = await orchestrator.EnsureVirtualIsPrimaryAsync(devices);

if (success)
    Console.WriteLine("Virtualization enabled. Games will only see the virtual controller.");
```

---

## Presentation Layer Services

### GamepadRemapService

SDL3-based service for capturing physical gamepad/joystick inputs.

```csharp
namespace AvaloniaUI.Services;

public sealed class GamepadRemapService : IDisposable
{
    /// <summary>
    /// Fired when a batch of inputs is captured (name → normalized value).
    /// Fired at intervals (with throttling to avoid event flooding).
    /// </summary>
    event Action<Dictionary<string, double>>? InputBatch;
    
    /// <summary>
    /// Fired when physical device connection state changes.
    /// </summary>
    event Action<bool>? ConnectionChanged;
    
    /// <summary>
    /// Fired when the physical device selection changes.
    /// </summary>
    event Action<PhysicalDeviceInfo?>? PhysicalDeviceChanged;
    
    /// <summary>
    /// Name of the currently selected controller (null if none).
    /// </summary>
    string? CurrentPadName { get; }
    
    /// <summary>
    /// Type of the currently selected controller (e.g., "SDL_GAMEPAD_TYPE_XBOX360").
    /// </summary>
    string? CurrentPadType { get; }
    
    // Configuration Properties
    bool InvertLY { get; set; }  // Invert left stick Y axis
    bool InvertRY { get; set; }  // Invert right stick Y axis
    double SensitivityL { get; set; }  // Left stick sensitivity (0.5..2.0)
    double SensitivityR { get; set; }  // Right stick sensitivity (0.5..2.0)
    bool ButtonsEdgeOnly { get; set; }  // Only emit button state changes
    
    /// <summary>
    /// Starts the SDL polling loop on a background thread.
    /// </summary>
    void StartAsync();
    
    /// <summary>
    /// Stops the polling loop and closes SDL handles.
    /// </summary>
    void Stop();
}
```

**InputBatch Event Payload:**
```csharp
// Example snapshot dictionary
{
    "LX": 0.8,      // Left stick X: -1.0 to 1.0
    "LY": -0.5,     // Left stick Y: -1.0 to 1.0
    "RX": 0.0,
    "RY": 0.0,
    "LT": 0.0,      // Left trigger: 0.0 to 1.0
    "RT": 0.4,
    "A": 1.0,       // Button A: 0.0 or 1.0
    "B": 0.0,
    "DUp": 1.0      // D-Pad Up: 0.0 or 1.0
}
```

**Example Usage:**
```csharp
var capture = new GamepadRemapService();
capture.SensitivityL = 1.2;  // 20% more sensitive
capture.InvertLY = true;     // Invert Y axis

capture.InputBatch += snapshot =>
{
    Console.WriteLine($"Captured {snapshot.Count} inputs:");
    foreach (var (name, value) in snapshot)
        Console.WriteLine($"  {name}: {value:F3}");
};

capture.StartAsync();
// ... later ...
capture.Stop();
```

---

### MappingEngine

Transforms physical inputs to virtual outputs using loaded mapping rules.

```csharp
namespace AvaloniaUI.ProgramCore;

public class MappingEngine
{
    /// <summary>
    /// Loads the mapping configuration from storage.
    /// </summary>
    /// <param name="profileId">
    /// Profile ID to load (null = default profile).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task LoadAsync(string? profileId = null, CancellationToken ct = default);
    
    /// <summary>
    /// Transforms a physical input snapshot into a virtual output state.
    /// </summary>
    /// <param name="snap">
    /// Snapshot dictionary from GamepadRemapService.InputBatch.
    /// </param>
    /// <returns>
    /// XInput-compatible state dictionary (name → float value).
    /// </returns>
    /// <remarks>
    /// <para><strong>Mapping Rules:</strong></para>
    /// <list type="bullet">
    ///   <item>Buttons → always digital (0 or 1)</item>
    ///   <item>Triggers → analog if mapped to triggers, else digital</item>
    ///   <item>Axes → analog if mapped to axes, else digital (0.6 threshold)</item>
    /// </list>
    /// <para><strong>Side Effects:</strong></para>
    /// <para>Maintains cumulative physical state (snapshots are deltas).</para>
    /// </remarks>
    Dictionary<string, float> BuildOutput(IReadOnlyDictionary<string, double> snap);
    
    /// <summary>
    /// Creates a zeroed XInput-compatible output state.
    /// </summary>
    Dictionary<string, float> InitOutputState();
}
```

**BuildOutput Return Value:**
```csharp
// Example output state
{
    "ButtonA": 1.0,
    "ButtonB": 0.0,
    "ButtonX": 0.0,
    "ButtonY": 0.0,
    "ButtonLeftShoulder": 0.0,
    "ButtonRightShoulder": 0.0,
    "ButtonStart": 0.0,
    "ButtonBack": 0.0,
    "ThumbLPressed": 0.0,
    "ThumbRPressed": 0.0,
    "DPadUp": 1.0,
    "DPadDown": 0.0,
    "DPadLeft": 0.0,
    "DPadRight": 0.0,
    "TriggerLeft": 0.0,
    "TriggerRight": 0.4,
    "ThumbLX": 0.8,
    "ThumbLY": -0.5,
    "ThumbRX": 0.0,
    "ThumbRY": 0.0
}
```

**Example Usage:**
```csharp
var engine = new MappingEngine(mappingStore);
await engine.LoadAsync(profileId: "fps_games");

var snapshot = new Dictionary<string, double>
{
    ["A"] = 1.0,
    ["LX"] = 0.8
};

var output = engine.BuildOutput(snapshot);
// output["ButtonA"] == 1.0
// output["ThumbLX"] == 0.8 (if mapped)
```

---

## Infrastructure Implementations

### ViGEmOutput

ViGEm-based implementation of `IOutputService`.

```csharp
namespace Infrastructure.Adapters.Outputs;

public class ViGEmOutput : IOutputService, IDisposable
{
    /// <summary>
    /// Applies the complete output state and sends a single ViGEm report.
    /// </summary>
    /// <param name="outputState">
    /// Complete XInput state (use MappingEngine.BuildOutput).
    /// </param>
    /// <remarks>
    /// <para><strong>Performance:</strong></para>
    /// <para>
    /// This method applies all button/axis states and calls SubmitReport() once.
    /// Prefer this over multiple Apply() calls to avoid partial state updates.
    /// </para>
    /// <para><strong>Side Effects:</strong></para>
    /// <list type="bullet">
    ///   <item>Applies circle-to-square correction on thumb sticks</item>
    ///   <item>Inverts Y axes to match XInput convention</item>
    ///   <item>Submits report to ViGEm driver</item>
    /// </list>
    /// <para><strong>Exceptions:</strong></para>
    /// <para>
    /// Logs errors to console if SubmitReport() fails (does not throw).
    /// </para>
    /// </remarks>
    void ApplyAll(Dictionary<string, float> outputState);
    
    void Connect();
    void Disconnect();
    void EnsureConnected();
    bool IsConnected { get; }
    void Dispose();
}
```

**Special Behaviors:**
- **Circle-to-Square Mapping**: Stick inputs are corrected using `AxisUtils.CircleToSquare()` for full diagonal range
- **Y-Axis Inversion**: SDL uses +Y = down, XInput uses +Y = up, so `ThumbLY` and `ThumbRY` are inverted

---

## Data Models

### PhysicalDeviceInfo

Information about a detected physical controller.

```csharp
public sealed record PhysicalDeviceInfo(
    string Name,                // e.g., "Xbox Wireless Controller"
    string? Path,               // Device instance path
    ushort VendorId,            // USB Vendor ID (e.g., 0x045E for Microsoft)
    ushort ProductId,           // USB Product ID
    bool IsGamepad,             // true = Gamepad API, false = Joystick API
    bool IsLikelyVirtual        // true if detected as virtual (e.g., ViGEm)
);
```

### CalibrationSettings

Axis calibration parameters.

```csharp
namespace Core.Entities;

public class CalibrationSettings
{
    public double Deadzone { get; set; }      // Stick deadzone (0.0..1.0)
    public double Sensitivity { get; set; }   // Axis sensitivity multiplier
    // Additional properties...
}
```

---

## Error Codes and Exceptions

| Exception | Scenario | Resolution |
|:----------|:---------|:-----------|
| `InvalidOperationException` | ViGEm not installed | Install ViGEm Bus Driver |
| `InvalidOperationException` | SDL initialization failed | Ensure SDL3 DLLs are in bin directory |
| `ArgumentNullException` | Null service in constructor | Check DI configuration |
| `OperationCanceledException` | Background task canceled | Normal shutdown, can ignore |

---

## Constants and Configuration

### GamepadRemapService Defaults

```csharp
private const int PollIntervalMs = 8;       // 120Hz polling
private const int MinBatchIntervalMs = 2;   // Event throttling
private const double StickDeadzone = 0.10;  // 10% deadzone
private const double TriggerDeadzone = 0.05; // 5% deadzone
private const double ResponseGamma = 1.35;  // Response curve exponent
```

### MappingEngine Thresholds

```csharp
const double threshold = 0.6;  // Axis-to-button activation threshold
```

---

## Logging

Logs are written to:
- **Main Log**: `%APPDATA%/NirvanaRemap/nirvana-input_main.log`
- **DI Log**: `%APPDATA%/NirvanaRemap/nirvana-input_sc.log`

Enable verbose logging:
```csharp
Trace.Listeners.Add(new ConsoleTraceListener());
Trace.AutoFlush = true;
```

---

## Performance Characteristics

| Operation | Typical Latency | Notes |
|:----------|:---------------|:------|
| SDL Polling | ~8ms | Runs at 120Hz |
| Mapping Transform | < 0.1ms | Dictionary lookup + math |
| ViGEm Report | ~1ms | Kernel driver call |
| **Total Latency** | **~9-10ms** | End-to-end input lag |

---

## Integration Example: Custom Mapping Plugin

```csharp
using AvaloniaUI.Services;
using AvaloniaUI.ProgramCore;
using Infrastructure.Adapters.Outputs;

public class CustomRemapper
{
    public void Run()
    {
        var capture = new GamepadRemapService();
        var engine = new MappingEngine(new JsonMappingStore());
        var output = new ViGEmOutput();
        
        // Load custom mapping
        await engine.LoadAsync("my_custom_profile");
        
        // Wire up events
        capture.InputBatch += snapshot =>
        {
            var virtualState = engine.BuildOutput(snapshot);
            output.ApplyAll(virtualState);
        };
        
        // Start capture
        output.Connect();
        capture.StartAsync();
        
        Console.WriteLine("Custom remapper running. Press Ctrl+C to exit.");
        Console.ReadLine();
        
        capture.Stop();
        output.Disconnect();
    }
}
```

---

## See Also

- [ARCHITECTURE.md](ARCHITECTURE.md) - System design and flow diagrams
- [CONTRIBUTING.md](CONTRIBUTING.md) - Development guidelines
- [ViGEm Documentation](https://vigem.org/projects/ViGEm/How-to-use/#net-managed-c-c-f-)
- [SDL3 API Reference](https://wiki.libsdl.org/SDL3/CategoryAPI)
