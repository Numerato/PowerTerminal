using System;
using System.Collections.Generic;
using System.Linq;
using PowerTerminal.Models;

namespace PowerTerminal.Services
{
    /// <summary>In-memory wiki service with search across all loaded entries.</summary>
    public class WikiService
    {
        private readonly ConfigService _config;
        private readonly LoggingService _log;
        private List<WikiEntry> _entries = new();

        public WikiService(ConfigService config, LoggingService log)
        {
            _config = config;
            _log = log;
        }

        public IReadOnlyList<WikiEntry> Entries => _entries;

        public void LoadAll()
        {
            _entries = _config.LoadAllWikis();
        }

        /// <summary>Searches title, description, tags, and section content.</summary>
        public List<WikiEntry> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _entries.ToList();

            string[] terms = query.ToLowerInvariant().Split(
                new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            var results = _entries
                .Select(e => new { Entry = e, Score = Score(e, terms) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Entry)
                .ToList();

            _log.LogWikiSearch(query, results.Count);
            return results;
        }

        private static int Score(WikiEntry entry, string[] terms)
        {
            int score = 0;
            foreach (string term in terms)
            {
                if (entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 10;
                if (entry.Description.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 5;
                if (entry.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 8;
                foreach (var section in entry.Sections)
                    if (section.Content.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 2;
            }
            return score;
        }

        public void Save(WikiEntry wiki)
        {
            _config.SaveWiki(wiki);
            var existing = _entries.FindIndex(e => e.Id == wiki.Id);
            if (existing >= 0)
                _entries[existing] = wiki;
            else
                _entries.Add(wiki);
            _log.LogWikiEdited("SAVE", wiki.Title);
        }

        public void Delete(WikiEntry wiki)
        {
            _config.DeleteWiki(wiki);
            _entries.RemoveAll(e => e.Id == wiki.Id);
            _log.LogWikiEdited("DELETE", wiki.Title);
        }
    }
}
