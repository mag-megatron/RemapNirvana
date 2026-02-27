# Test Strategy: Efficiency & Performance

## 1. Executive Summary
The primary goal of this test strategy is to empirically evidence the efficiency of **NirvanaRemap**. We define "efficiency" as **minimal added latency** (overhead) and **execution stability** (low jitter) while maintaining low resource consumption.

## 2. Key Performance Indicators (KPIs)

To prove efficiency, we will track the following metrics:

| Metric | Definition | Target |
|:-------|:-----------|:-------|
| **Processing Latency** | Time elapsed from capturing `SDL_Gamepad` state to submitting `ViGEm` report | **< 0.5 ms** (average) |
| **Loop Jitter** | Standard deviation of the time interval between processing loops | **< 1.0 ms** |
| **Input Polling Rate** | Frequency of input checks per second | **120 Hz** (Stable) |
| **CPU Overhead** | Percentage of CPU core usage during active remapping | **< 1%** (on modern CPU) |
| **Memory Footprint** | Private working set memory usage | **< 50 MB** |

## 3. Test Tools & Methodology

### 3.1. Internal Instrumentation (White-Box)
We will inject high-resolution telemetry directly into the `GamepadRemapService` loop.
- **Tools**: `System.Diagnostics.Stopwatch` (High-resolution).
- **Data Points**:
  - `t0`: Start of Poll Loop
  - `t1`: SDL State Acquired
  - `t2`: Mapping Logic Applied
  - `t3`: ViGEm Report Submitted
- **Output**: CSV logs for statistical analysis (P95, P99 latencies).

### 3.2. External Verification (Black-Box)
- **Virtual Loopback**: A separate test harness reading `XInput` state from the virtual controller to verify output stability.
- **Physical Latency (Optional)**: Using a 240fps+ camera to measure "Motion-to-Photon" latency (requires hardware setup).

---

## 4. Execution Roadmap (Roteiro)

This roadmap outlines the steps to gather the evidence.

### Phase 1: Telemetry Implementation
**Objective**: Enable the application to self-report performance stats.

1.  **Modify `RawVirtualizationRunner.cs` / `GamepadRemapService.cs`**:
    - Add a `PerformanceMonitor` class.
    - Measure the delta between Input Capture and Output Report.
    - Log "hot path" duration.
2.  **Add `--benchmark` flag**:
    - Allow running the app in a mode that outputs a `latency_stats.csv` file on exit.

### Phase 2: Baseline Data Collection
**Objective**: Establish a performance baseline in a controlled environment.

1.  **Idle Test**: Run for 5 minutes with no controller input. Measure base loop stability.
2.  **Active Test**: Run for 5 minutes while constantly rotating analog sticks (generating max traffic).
3.  **Analysis**: Calculate Mean, Median, and 99th Percentile latency.

### Phase 3: Stress Testing
**Objective**: Ensure stability under load.

1.  **Connect Multiple Controllers**: If possible, connect 4 virtual devices.
2.  **High-Frequency Input**: Simulate rapid button presses (possibly using a script feeding into a virtual bus if SDL allows, or just mechanical rapid movement).
3.  **Resource Monitoring**: Use `Performance Monitor` (perfmon.exe) to track CPU/RAM over time.

### Phase 4: Comparative Benchmark
**Objective**: Compare against industry standards.

1.  **Competitor**: DS4Windows or Steam Input.
2.  **Method**: Run identical scenarios and compare CPU usage and estimated input lag.
3.  **Report**: "NirvanaRemap requires X% less CPU than Steam Input for identical tasks."

## 5. Implementation Plan (Code Changes)

To support Phase 1, we plan to add the following logic to the `PollLoop`:

```csharp
// Pseudo-code for instrumentation
var sw = Stopwatch.StartNew();
while (running) {
    long startTick = sw.ElapsedTicks;
    
    // 1. Capture
    var state = _sdl.GetState();
    
    // 2. Map
    var output = _mapper.Process(state);
    
    // 3. Emit
    _vigem.Submit(output);
    
    long endTick = sw.ElapsedTicks;
    double latencyMicroseconds = (endTick - startTick) * 1_000_000.0 / Stopwatch.Frequency;
    
    _telemetry.Record(latencyMicroseconds);
    
    // Wait for next frame (8ms)
}
```

## 6. Deliverables
- `latency_stats.csv`: Raw data.
- `EfficiencyReport.md`: Final summary showing:
  - "Average Processing Latency: 0.04ms"
  - "99.9% of inputs processed in under 0.1ms"
