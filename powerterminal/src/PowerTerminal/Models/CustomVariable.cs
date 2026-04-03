using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PowerTerminal.Models
{
    /// <summary>A user-defined named variable stored in config/variables.json.</summary>
    public class CustomVariable : INotifyPropertyChanged
    {
        private string _name  = string.Empty;
        private string _value = string.Empty;
        private bool _isDuplicate;

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public string Value
        {
            get => _value;
            set { if (_value != value) { _value = value; OnPropertyChanged(); } }
        }

        /// <summary>True if another variable with the same name exists (used for UI indication).</summary>
        public bool IsDuplicate
        {
            get => _isDuplicate;
            set { if (_isDuplicate != value) { _isDuplicate = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

