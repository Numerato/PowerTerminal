using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.Tests;

/// <summary>
/// Tests for ConfigService – load/save/delete operations against a temp directory.
/// </summary>
public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _svc;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ConfigServiceTests_" + Guid.NewGuid().ToString("N")[..8]);
        _svc     = new ConfigService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Directory creation ────────────────────────────────────────────────

    [Fact]
    public void Constructor_CreatesBaseDirectory()
    {
        Assert.True(Directory.Exists(_tempDir));
    }

    [Fact]
    public void Constructor_CreatesWikiSubdirectory()
    {
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "wikis")));
    }

    [Fact]
    public void Constructor_CreatesCommandsSubdirectory()
    {
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "commands")));
    }

    // ── Connections ───────────────────────────────────────────────────────

    [Fact]
    public void LoadConnections_NoFile_ReturnsEmpty()
    {
        var result = _svc.LoadConnections();
        Assert.Empty(result);
    }

    [Fact]
    public void SaveAndLoadConnections_RoundTrip()
    {
        var connections = new List<SshConnection>
        {
            new() { Name = "dev-server",  Host = "192.168.1.10", Username = "alice", Port = 22   },
            new() { Name = "prod-server", Host = "10.0.0.5",     Username = "bob",   Port = 2222 }
        };
        _svc.SaveConnections(connections);

        var loaded = _svc.LoadConnections();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("dev-server",   loaded[0].Name);
        Assert.Equal("192.168.1.10", loaded[0].Host);
        Assert.Equal("alice",        loaded[0].Username);
        Assert.Equal(22,             loaded[0].Port);
        Assert.Equal("prod-server",  loaded[1].Name);
        Assert.Equal(2222,           loaded[1].Port);
    }

    [Fact]
    public void SaveConnections_EmptyList_SavesToFile()
    {
        _svc.SaveConnections(new List<SshConnection>());
        var loaded = _svc.LoadConnections();
        Assert.Empty(loaded);
    }

    [Fact]
    public void LoadConnections_CorruptFile_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "connections.json");
        File.WriteAllText(path, "not valid json {{{}");
        var result = _svc.LoadConnections();
        Assert.Empty(result);
    }

    // ── Settings ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadSettings_NoFile_ReturnsDefaults()
    {
        var settings = _svc.LoadSettings();
        Assert.NotNull(settings);
        Assert.NotNull(settings.Ai);
        Assert.NotNull(settings.Theme);
    }

    [Fact]
    public void SaveAndLoadSettings_RoundTrip()
    {
        var settings = new AppSettings
        {
            EnableDebugLogging = true,
            SidepaneWidth      = 600,
            Ai = new AiSettings
            {
                Model         = "gpt-4o",
                Temperature   = 0.5,
                MaxTokens     = 4096,
                ApiBaseUrl    = "https://custom.example.com/v1"
            },
            Theme = new ThemeSettings
            {
                Background = "#1E1E1E",
                Foreground = "#D4D4D4",
                FontSize   = 15.0
            }
        };
        _svc.SaveSettings(settings);

        var loaded = _svc.LoadSettings();
        Assert.True(loaded.EnableDebugLogging);
        Assert.Equal(600,                            loaded.SidepaneWidth);
        Assert.Equal("gpt-4o",                       loaded.Ai.Model);
        Assert.Equal(0.5,                            loaded.Ai.Temperature);
        Assert.Equal(4096,                           loaded.Ai.MaxTokens);
        Assert.Equal("https://custom.example.com/v1", loaded.Ai.ApiBaseUrl);
        Assert.Equal("#1E1E1E",                      loaded.Theme.Background);
        Assert.Equal(15.0,                           loaded.Theme.FontSize);
    }

    [Fact]
    public void LoadSettings_CorruptFile_ReturnsDefaults()
    {
        string path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, "}}broken{{");
        var result = _svc.LoadSettings();
        Assert.NotNull(result);
        Assert.NotNull(result.Ai);
    }

    // ── Wiki ──────────────────────────────────────────────────────────────

    [Fact]
    public void LoadAllWikis_EmptyDirectory_ReturnsEmpty()
    {
        var result = _svc.LoadAllWikis();
        Assert.Empty(result);
    }

    [Fact]
    public void SaveWiki_NewEntry_CreatesFile()
    {
        var wiki = new WikiEntry { Title = "Test Entry", Description = "Some desc" };
        _svc.SaveWiki(wiki);

        Assert.False(string.IsNullOrWhiteSpace(wiki.FileName));
        Assert.True(File.Exists(Path.Combine(_svc.WikiDir, wiki.FileName)));
    }

    [Fact]
    public void SaveWiki_NewEntry_SetsFileName()
    {
        var wiki = new WikiEntry { Title = "My Entry" };
        _svc.SaveWiki(wiki);
        Assert.Contains("my_entry", wiki.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveWiki_NewEntry_SetsCreatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var wiki   = new WikiEntry { Title = "entry" };
        _svc.SaveWiki(wiki);
        Assert.True(wiki.CreatedAt >= before);
    }

    [Fact]
    public void SaveWiki_SetsUpdatedAt()
    {
        var wiki = new WikiEntry { Title = "entry" };
        _svc.SaveWiki(wiki);
        var first = wiki.UpdatedAt;

        System.Threading.Thread.Sleep(10);
        _svc.SaveWiki(wiki);
        Assert.True(wiki.UpdatedAt >= first);
    }

    [Fact]
    public void SaveWiki_ExistingFileName_DoesNotChangeIt()
    {
        var wiki = new WikiEntry { Title = "entry" };
        _svc.SaveWiki(wiki);
        string originalFileName = wiki.FileName;

        wiki.Title = "entry updated";
        _svc.SaveWiki(wiki);
        Assert.Equal(originalFileName, wiki.FileName);
    }

    [Fact]
    public void LoadAllWikis_RoundTrip_ReturnsAllFields()
    {
        var wiki = new WikiEntry
        {
            Title       = "My Wiki",
            Description = "A description",
            Tags        = new List<string> { "tag1", "tag2" },
            Sections    = new List<WikiSection>
            {
                new() { Type = WikiSectionType.Command, Content = "ls -la", Language = "bash" },
                new() { Type = WikiSectionType.Text,    Content = "Some notes" }
            }
        };
        _svc.SaveWiki(wiki);

        var all = _svc.LoadAllWikis();
        Assert.Single(all);
        var loaded = all[0];
        Assert.Equal("My Wiki",        loaded.Title);
        Assert.Equal("A description",  loaded.Description);
        Assert.Equal(2,                loaded.Tags.Count);
        Assert.Contains("tag1",        loaded.Tags);
        Assert.Equal(2,                loaded.Sections.Count);
        Assert.Equal("ls -la",         loaded.Sections[0].Content);
        Assert.Equal("bash",           loaded.Sections[0].Language);
        Assert.Equal(WikiSectionType.Command, loaded.Sections[0].Type);
    }

    [Fact]
    public void LoadAllWikis_SetsFileName()
    {
        var wiki = new WikiEntry { Title = "entry" };
        _svc.SaveWiki(wiki);

        var all = _svc.LoadAllWikis();
        Assert.False(string.IsNullOrWhiteSpace(all[0].FileName));
    }

    [Fact]
    public void DeleteWiki_RemovesFile()
    {
        var wiki = new WikiEntry { Title = "to delete" };
        _svc.SaveWiki(wiki);
        string path = Path.Combine(_svc.WikiDir, wiki.FileName);
        Assert.True(File.Exists(path));

        _svc.DeleteWiki(wiki);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteWiki_EmptyFileName_DoesNotThrow()
    {
        var wiki = new WikiEntry { Title = "ghost" };
        _svc.DeleteWiki(wiki);   // no exception expected
    }

    [Fact]
    public void LoadAllWikis_SkipsMalformedFiles()
    {
        // Write a valid wiki and a corrupt file
        var good = new WikiEntry { Title = "good" };
        _svc.SaveWiki(good);
        File.WriteAllText(Path.Combine(_svc.WikiDir, "corrupt.json"), "not json {{");

        var all = _svc.LoadAllWikis();
        Assert.Single(all);
        Assert.Equal("good", all[0].Title);
    }

    [Fact]
    public void SaveWiki_TitlesWithSpecialChars_ProduceSafeFileName()
    {
        var wiki = new WikiEntry { Title = "Hello <World>: /test\\?" };
        _svc.SaveWiki(wiki);
        // File must be creatable and valid
        Assert.True(File.Exists(Path.Combine(_svc.WikiDir, wiki.FileName)));
    }

    [Fact]
    public void SaveWiki_TwoEntriesWithSameTitle_DifferentFileNames()
    {
        var w1 = new WikiEntry { Title = "same title" };
        var w2 = new WikiEntry { Title = "same title" };
        _svc.SaveWiki(w1);
        _svc.SaveWiki(w2);
        Assert.NotEqual(w1.FileName, w2.FileName);
    }

    // ── Custom Variables ──────────────────────────────────────────────────

    [Fact]
    public void LoadCustomVariables_NoFile_ReturnsEmpty()
    {
        Assert.Empty(_svc.LoadCustomVariables());
    }

    [Fact]
    public void SaveAndLoadCustomVariables_RoundTrip()
    {
        var vars = new List<CustomVariable>
        {
            new() { Name = "SERVER_IP",   Value = "192.168.1.100" },
            new() { Name = "DEPLOY_PATH", Value = "/var/www/html"  }
        };
        _svc.SaveCustomVariables(vars);

        var loaded = _svc.LoadCustomVariables();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("SERVER_IP",        loaded[0].Name);
        Assert.Equal("192.168.1.100",    loaded[0].Value);
        Assert.Equal("DEPLOY_PATH",      loaded[1].Name);
        Assert.Equal("/var/www/html",    loaded[1].Value);
    }

    [Fact]
    public void LoadCustomVariables_CorruptFile_ReturnsEmpty()
    {
        string path = Path.Combine(_tempDir, "variables.json");
        File.WriteAllText(path, "{{corrupt}}");
        Assert.Empty(_svc.LoadCustomVariables());
    }

    // ── BaseDir / WikiDir / LogDir properties ─────────────────────────────

    [Fact]
    public void BaseDir_ReturnsConstructorPath()
    {
        Assert.Equal(_tempDir, _svc.BaseDir);
    }

    [Fact]
    public void WikiDir_IsSubdirectoryOfBaseDir()
    {
        Assert.StartsWith(_tempDir, _svc.WikiDir);
        Assert.EndsWith("wikis", _svc.WikiDir);
    }

    [Fact]
    public void CommandDir_IsSubdirectoryOfBaseDir()
    {
        Assert.StartsWith(_tempDir, _svc.CommandDir);
        Assert.EndsWith("commands", _svc.CommandDir);
    }
}
