using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PowerTerminal.Models;

namespace PowerTerminal.Services
{
    /// <summary>OpenAI-compatible AI chat service.</summary>
    public class AiService
    {
        private readonly LoggingService _log;
        private AppSettings _settings;
        // Replaced atomically via Interlocked.Exchange so an in-flight ChatAsync
        // never sees its HttpClient disposed underneath it.
        private volatile HttpClient? _http;

        public AiService(LoggingService log, AppSettings settings)
        {
            _log = log;
            _settings = settings;
            RebuildClient();
        }

        public void UpdateSettings(AppSettings settings)
        {
            _settings = settings;
            RebuildClient();
        }

        private void RebuildClient()
        {
            var newClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            if (!string.IsNullOrWhiteSpace(_settings.Ai.ApiToken))
                newClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.Ai.ApiToken);

            // Swap atomically; dispose the old instance after the swap so any
            // concurrent request that already captured it can finish cleanly.
            var old = Interlocked.Exchange(ref _http, newClient);
            old?.Dispose();
        }

        /// <summary>Sends a conversation and returns the assistant reply.</summary>
        public async Task<string> ChatAsync(
            IEnumerable<AiMessage> history,
            string userMessage,
            CancellationToken ct = default)
        {
            _log.LogAiMessage("user", userMessage, _settings.Ai.Model);

            var messages = new List<object>();

            if (!string.IsNullOrWhiteSpace(_settings.Ai.SystemPrompt))
                messages.Add(new { role = "system", content = _settings.Ai.SystemPrompt });

            foreach (var m in history)
                messages.Add(new { role = m.Role, content = m.Content });

            messages.Add(new { role = "user", content = userMessage });

            var requestBody = new
            {
                model       = _settings.Ai.Model,
                messages    = messages,
                temperature = _settings.Ai.Temperature,
                max_tokens  = _settings.Ai.MaxTokens
            };

            string json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            string url = _settings.Ai.ApiBaseUrl.TrimEnd('/') + "/chat/completions";

            // Capture locally so a concurrent RebuildClient() cannot dispose the
            // instance we are about to await on.
            var http = _http ?? throw new InvalidOperationException("HTTP client is not initialized.");

            HttpResponseMessage response;
            try
            {
                response = await http.PostAsync(url, content, ct);
            }
            catch (Exception ex)
            {
                _log.LogAiError(ex.Message);
                throw new InvalidOperationException($"AI request failed: {ex.Message}", ex);
            }

            string responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogAiError($"HTTP {(int)response.StatusCode}: {responseBody}");
                throw new InvalidOperationException($"AI API error ({(int)response.StatusCode}): {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            string reply = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            _log.LogAiMessage("assistant", reply, _settings.Ai.Model);
            return reply;
        }
    }
}
