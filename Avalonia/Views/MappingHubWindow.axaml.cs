using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaUI.Views
{
    public partial class MappingHubWindow : Window
    {
        public MappingHubWindow(MappingHubView hubView, ViewModels.MappingHubViewModel vm)
        {
            InitializeComponent();

            // Substitui o Content do Window pela view injetada com VM real
            Content = hubView;
            hubView.DataContext = vm;
        }
    }
}
