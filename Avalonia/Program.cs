using ApplicationLayer.Services;
using Avalonia;
using Avalonia.Logging;
using AvaloniaUI;
using AvaloniaUI.Hub;
using AvaloniaUI.Services;
using Core.Interfaces;
using Infrastructure.HidHide;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDL;
using static SDL.SDL3;

internal static class Program
{
    public static ServiceProvider Services = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        InitLogging();

        // Logs principais do processo
        var baseDir = AppContext.BaseDirectory;
        Trace.Listeners.Add(new TextWriterTraceListener(
            Path.Combine(baseDir, "nirvana-input_main.log")));
        Trace.AutoFlush = true;

        Debug.WriteLine("==== Nirvana Remap iniciado ====");
        Debug.WriteLine($"BaseDirectory = {baseDir}");

        Services = ConfigureServices();

        // Modo headless (sem UI), só capturando e emitindo ViGEm
        if (args.Any(a => string.Equals(a, "--raw", StringComparison.OrdinalIgnoreCase)))
        {
            RunHeadlessAsync().GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static async Task RunHeadlessAsync()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await using var scope = Services.CreateAsyncScope();
        var runner = scope.ServiceProvider
                          .GetRequiredService<AvaloniaUI.Services.RawVirtualizationRunner>();

        Console.WriteLine("[RAW] Argumento --raw detectado. Rodando sem UI…");
        await runner.RunAsync(cts.Token);
    }

    private static ServiceProvider ConfigureServices()
    {
        var sc = new ServiceCollection();

        // Infra / Core
        sc.AddSingleton<IHidHideService>(_ => new HidHideCliService());
        sc.AddSingleton<GamepadVirtualizationOrchestrator>();
        sc.AddSingleton<Infrastructure.Adapters.Outputs.ViGEmOutput>();

        // Captura física + runner headless
        sc.AddSingleton<AvaloniaUI.Services.GamepadRemapService>();
        sc.AddSingleton<AvaloniaUI.Services.RawVirtualizationRunner>();

        // Hub / storage / captura abstrata (se você ainda usa)
        sc.AddSingleton<IMappingStore, JsonMappingStore>();
        sc.AddSingleton<IInputCaptureService, SdlCaptureService>();

        // Views
        sc.AddTransient<AvaloniaUI.Views.MappingHubView>();
        sc.AddTransient<AvaloniaUI.Views.MappingHubWindow>();
        sc.AddTransient<AvaloniaUI.Views.DiagnosticsGamepadWindow>();

        // ViewModels
        sc.AddSingleton<AvaloniaUI.ViewModels.MainViewModel>();
        sc.AddSingleton<AvaloniaUI.ViewModels.MappingHubViewModel>();
        sc.AddSingleton<AvaloniaUI.ViewModels.DiagnosticsGamepadViewModel>();

        // Log específico de DI
        Trace.Listeners.Add(new TextWriterTraceListener(
            Path.Combine(AppContext.BaseDirectory, "nirvana-input_sc.log")));
        Trace.AutoFlush = true;

        return sc.BuildServiceProvider();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .LogToTrace(LogEventLevel.Information);

    private static void InitLogging()
    {
        // Se você tiver alguma configuração extra de logging, deixa aqui.
        // Por enquanto estamos só com Trace + Debug.
    }
}
