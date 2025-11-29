using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaUI
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var services = Program.Services;

                var mainVm = services.GetRequiredService<AvaloniaUI.ViewModels.MainViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainVm
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
