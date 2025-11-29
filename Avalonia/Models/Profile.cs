using LiteDB;
using System.ComponentModel;

namespace AvaloniaUI.Models
{
    /// <summary>
    /// Representa um perfil nomeado (ex.: "Default", "FPS", "RPG").
    /// 
    /// Hoje ele não faz o mapeamento em si — o mapeamento real
    /// é feito pelos arquivos JSON via IMappingStore/JsonMappingStore.
    /// Este modelo pode ser usado apenas para UI/listagem de perfis,
    /// se você ainda quiser trabalhar com LiteDB.
    /// </summary>
    public class Profile : INotifyPropertyChanged
    {
        [BsonId]
        public string Name { get; set; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
