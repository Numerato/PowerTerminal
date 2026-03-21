using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.ViewModels
{
    public class AiChatViewModel : ViewModelBase
    {
        private readonly AiService _ai;
        private readonly LoggingService _log;
        private string _inputText = string.Empty;
        private bool _isBusy;
        private CancellationTokenSource? _cts;

        public AiChatViewModel(AiService ai, LoggingService log)
        {
            _ai  = ai;
            _log = log;
            SendCommand  = new RelayCommand(async _ => await SendAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(InputText));
            ClearCommand = new RelayCommand(_ => Clear());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy);
        }

        public ObservableCollection<AiMessage> Messages { get; } = new();
        public ICommand SendCommand  { get; }
        public ICommand ClearCommand { get; }
        public ICommand CancelCommand { get; }

        public string InputText
        {
            get => _inputText;
            set => Set(ref _inputText, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => Set(ref _isBusy, value);
        }

        public event Action? MessagesChanged;

        private async Task SendAsync()
        {
            string text = InputText.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            InputText = string.Empty;

            // Snapshot history BEFORE adding the new user message so AiService
            // doesn't iterate a list that already contains it and then append it
            // a second time (which would duplicate the last user turn in the API call).
            var history = Messages.ToList();

            var userMsg = new AiMessage { Role = "user", Content = text };
            Messages.Add(userMsg);
            MessagesChanged?.Invoke();

            IsBusy = true;
            _cts = new CancellationTokenSource();
            try
            {
                string reply = await _ai.ChatAsync(history, text, _cts.Token);
                Messages.Add(new AiMessage { Role = "assistant", Content = reply });
                MessagesChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
                Messages.Add(new AiMessage { Role = "assistant", Content = "_(cancelled)_" });
                MessagesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Messages.Add(new AiMessage { Role = "assistant", Content = $"**Error:** {ex.Message}" });
                MessagesChanged?.Invoke();
                _log.LogAiError(ex.Message);
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Clear()
        {
            Messages.Clear();
        }

        private void Cancel()
        {
            _cts?.Cancel();
        }
    }
}
