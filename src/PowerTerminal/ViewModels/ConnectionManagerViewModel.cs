using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.ViewModels
{
    public class ConnectionManagerViewModel : ViewModelBase
    {
        private readonly ConfigService _config;
        private SshConnection? _selected;
        private SshConnection? _editing;

        public ConnectionManagerViewModel(ConfigService config)
        {
            _config = config;
            Connections = new ObservableCollection<SshConnection>(config.LoadConnections());

            AddCommand    = new RelayCommand(_ => StartAdd());
            EditCommand   = new RelayCommand(_ => StartEdit(), _ => Selected != null);
            DeleteCommand = new RelayCommand(_ => Delete(), _ => Selected != null);
            SaveCommand   = new RelayCommand(_ => SaveEdit(), _ => Editing != null);
            CancelCommand = new RelayCommand(_ => CancelEdit(), _ => Editing != null);
        }

        public ObservableCollection<SshConnection> Connections { get; }
        public ICommand AddCommand    { get; }
        public ICommand EditCommand   { get; }
        public ICommand DeleteCommand { get; }
        public ICommand SaveCommand   { get; }
        public ICommand CancelCommand { get; }

        public SshConnection? Selected
        {
            get => _selected;
            set => Set(ref _selected, value);
        }

        public SshConnection? Editing
        {
            get => _editing;
            set => Set(ref _editing, value);
        }

        public bool IsEditing => Editing != null;

        private bool _isAddMode;

        private void StartAdd()
        {
            _isAddMode = true;
            Editing = new SshConnection { Name = "New Connection", Port = 22 };
        }

        private void StartEdit()
        {
            if (Selected == null) return;
            _isAddMode = false;
            Editing = new SshConnection
            {
                Id             = Selected.Id,
                Name           = Selected.Name,
                Host           = Selected.Host,
                Username       = Selected.Username,
                Port           = Selected.Port,
                PrivateKeyPath = Selected.PrivateKeyPath,
                LogoPath       = Selected.LogoPath
            };
        }

        private void SaveEdit()
        {
            if (Editing == null) return;
            if (_isAddMode)
            {
                Connections.Add(Editing);
            }
            else
            {
                var existing = Connections.FirstOrDefault(c => c.Id == Editing.Id);
                if (existing != null)
                {
                    int idx = Connections.IndexOf(existing);
                    Connections[idx] = Editing;
                }
            }
            _config.SaveConnections(Connections);
            Editing = null;
        }

        private void CancelEdit()
        {
            Editing = null;
        }

        private void Delete()
        {
            if (Selected == null) return;
            Connections.Remove(Selected);
            _config.SaveConnections(Connections);
            Selected = null;
        }
    }
}
