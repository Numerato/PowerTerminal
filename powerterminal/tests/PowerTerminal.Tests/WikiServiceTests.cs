using System;
using System.Collections.Generic;
using System.IO;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.Tests;

/// <summary>
/// Tests for WikiService search scoring and CRUD.
/// Uses real ConfigService and LoggingService with temp directories.
/// </summary>
public class WikiServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _config;
    private readonly LoggingService _log;
    private readonly WikiService _service;

    public WikiServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WikiServiceTests_" + Guid.NewGuid().ToString("N")[..8]);
        _config  = new ConfigService(_tempDir);
        _log     = new LoggingService(_config.LogDir);
        _service = new WikiService(_config, _log);
    }

    public void Dispose()
    {
        _log.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static WikiEntry MakeEntry(
        string title,
        string description = "",
        string[]? tags = null,
        string[]? sectionContents = null)
    {
        var entry = new WikiEntry { Title = title, Description = description };
        if (tags != null) entry.Tags.AddRange(tags);
        if (sectionContents != null)
        {
            foreach (var c in sectionContents)
                entry.Sections.Add(new WikiSection { Content = c });
        }
        return entry;
    }

    private void LoadEntries(params WikiEntry[] entries)
    {
        foreach (var e in entries)
            _config.SaveWiki(e);
        _service.LoadAll();
    }

    // ── Search ────────────────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyQuery_ReturnsAllEntries()
    {
        LoadEntries(MakeEntry("Alpha"), MakeEntry("Beta"));
        var results = _service.Search("");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_WhitespaceQuery_ReturnsAllEntries()
    {
        LoadEntries(MakeEntry("Alpha"), MakeEntry("Beta"));
        var results = _service.Search("   ");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_ByTitle_ReturnsMatchingEntry()
    {
        LoadEntries(MakeEntry("docker run"), MakeEntry("kubectl apply"));
        var results = _service.Search("docker");
        Assert.Single(results);
        Assert.Equal("docker run", results[0].Title);
    }

    [Fact]
    public void Search_ByTag_ReturnsMatchingEntry()
    {
        LoadEntries(
            MakeEntry("entry1", tags: new[] { "networking" }),
            MakeEntry("entry2", tags: new[] { "storage" }));
        var results = _service.Search("networking");
        Assert.Single(results);
        Assert.Equal("entry1", results[0].Title);
    }

    [Fact]
    public void Search_ByDescription_ReturnsMatchingEntry()
    {
        LoadEntries(
            MakeEntry("A", "contains nginx config"),
            MakeEntry("B", "something else"));
        var results = _service.Search("nginx");
        Assert.Single(results);
        Assert.Equal("A", results[0].Title);
    }

    [Fact]
    public void Search_BySectionContent_ReturnsMatchingEntry()
    {
        LoadEntries(
            MakeEntry("A", sectionContents: new[] { "systemctl restart apache2" }),
            MakeEntry("B", sectionContents: new[] { "journalctl -f" }));
        var results = _service.Search("apache2");
        Assert.Single(results);
        Assert.Equal("A", results[0].Title);
    }

    [Fact]
    public void Search_TitleMatchRanksHigherThanDescriptionMatch()
    {
        // Title match: score +10; Description match: score +5
        LoadEntries(
            MakeEntry("other entry", description: "nginx reverse proxy config"),
            MakeEntry("nginx setup",  description: "some info"));
        var results = _service.Search("nginx");
        Assert.Equal(2, results.Count);
        Assert.Equal("nginx setup", results[0].Title);   // title hit first
    }

    [Fact]
    public void Search_TagMatchRanksHigherThanDescriptionMatch()
    {
        // Tag match: +8; Description match: +5
        LoadEntries(
            MakeEntry("A", description: "uses docker"),
            MakeEntry("B", tags: new[] { "docker" }));
        var results = _service.Search("docker");
        Assert.Equal(2, results.Count);
        Assert.Equal("B", results[0].Title);   // tag hit (8) > description hit (5)
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        LoadEntries(MakeEntry("NGINX Configuration"));
        var results = _service.Search("nginx");
        Assert.Single(results);
    }

    [Fact]
    public void Search_MultipleTerms_BothMustScore()
    {
        LoadEntries(
            MakeEntry("docker compose", description: "multi-container"),
            MakeEntry("docker run",     description: "single image"),
            MakeEntry("kubectl apply",  description: "k8s"));

        // Both "docker" and "compose" must match to get highest score
        var results = _service.Search("docker compose");
        Assert.True(results.Count > 0);
        Assert.Equal("docker compose", results[0].Title);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmptyList()
    {
        LoadEntries(MakeEntry("nginx"), MakeEntry("docker"));
        var results = _service.Search("kubernetes");
        Assert.Empty(results);
    }

    [Fact]
    public void Search_TermSeparators_CommaAndSemicolon()
    {
        LoadEntries(MakeEntry("foo bar"), MakeEntry("baz qux"));
        var results = _service.Search("foo,baz");
        Assert.Equal(2, results.Count);
    }

    // ── Save & Delete ─────────────────────────────────────────────────────

    [Fact]
    public void Save_NewEntry_AddsToEntries()
    {
        _service.LoadAll();
        var entry = MakeEntry("new entry");
        _service.Save(entry);
        Assert.Single(_service.Entries);
        Assert.Equal("new entry", _service.Entries[0].Title);
    }

    [Fact]
    public void Save_ExistingEntry_UpdatesInPlace()
    {
        var entry = MakeEntry("original");
        _service.Save(entry);

        entry.Title = "updated";
        _service.Save(entry);

        Assert.Single(_service.Entries);
        Assert.Equal("updated", _service.Entries[0].Title);
    }

    [Fact]
    public void Save_PersistsToFile_ReloadReturnsEntry()
    {
        var entry = MakeEntry("persisted entry", "a description", new[] { "tag1" });
        _service.Save(entry);

        // Create a fresh service pointing at same config dir
        var service2 = new WikiService(_config, _log);
        service2.LoadAll();
        Assert.Single(service2.Entries);
        Assert.Equal("persisted entry", service2.Entries[0].Title);
        Assert.Equal("a description",   service2.Entries[0].Description);
        Assert.Contains("tag1",         service2.Entries[0].Tags);
    }

    [Fact]
    public void Delete_RemovesEntryFromMemoryAndDisk()
    {
        var entry = MakeEntry("to be deleted");
        _service.Save(entry);
        Assert.Single(_service.Entries);

        _service.Delete(entry);
        Assert.Empty(_service.Entries);

        // Confirm it's gone from disk too
        var service2 = new WikiService(_config, _log);
        service2.LoadAll();
        Assert.Empty(service2.Entries);
    }

    [Fact]
    public void Delete_NonExistentFile_DoesNotThrow()
    {
        var entry = MakeEntry("ghost entry");
        entry.FileName = "nonexistent_file.json";
        // Should not throw
        _service.Delete(entry);
    }

    // ── Entries property ──────────────────────────────────────────────────

    [Fact]
    public void Entries_BeforeLoadAll_IsEmpty()
    {
        Assert.Empty(_service.Entries);
    }

    [Fact]
    public void LoadAll_LoadsAllSavedEntries()
    {
        // Save 3 entries directly via config, then load
        _config.SaveWiki(MakeEntry("E1"));
        _config.SaveWiki(MakeEntry("E2"));
        _config.SaveWiki(MakeEntry("E3"));

        _service.LoadAll();
        Assert.Equal(3, _service.Entries.Count);
    }
}
