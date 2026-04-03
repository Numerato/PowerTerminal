using PowerTerminal.Services;

namespace PowerTerminal.Tests;

public class SyntaxValidationServiceTests
{
    // ── JSON ─────────────────────────────────────────────────────────────

    [Fact]
    public void ValidJson_ReturnsSuccess()
    {
        var result = SyntaxValidationService.Validate("{\"key\":\"value\"}", "file.json");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidJson_ReturnsFail()
    {
        var result = SyntaxValidationService.Validate("{bad json}", "file.json");
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public void InvalidJson_IncludesLineInfo()
    {
        var result = SyntaxValidationService.Validate("{\n  \"a\": }", "file.json");
        Assert.False(result.IsValid);
        Assert.True(result.Line >= 1);
    }

    [Fact]
    public void JsonWithTrailingComma_ReturnsFail()
    {
        var result = SyntaxValidationService.Validate("{\"a\":1,}", "file.json");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void JsonArray_ReturnsSuccess()
    {
        var result = SyntaxValidationService.Validate("[1, 2, 3]", "data.json");
        Assert.True(result.IsValid);
    }

    // ── XML ──────────────────────────────────────────────────────────────

    [Fact]
    public void ValidXml_ReturnsSuccess()
    {
        var result = SyntaxValidationService.Validate("<root><child/></root>", "file.xml");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidXml_ReturnsFail()
    {
        var result = SyntaxValidationService.Validate("<root><unclosed>", "file.xml");
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public void InvalidXml_IncludesLineInfo()
    {
        var result = SyntaxValidationService.Validate("<root>\n<bad attr=unclosed>\n</root>", "file.xml");
        Assert.False(result.IsValid);
        Assert.True(result.Line >= 1);
    }

    [Theory]
    [InlineData("file.xaml")]
    [InlineData("file.svg")]
    [InlineData("file.csproj")]
    public void ValidXml_TreatedAsXmlByExtension_ReturnsSuccess(string fileName)
    {
        var result = SyntaxValidationService.Validate("<Project/>", fileName);
        Assert.True(result.IsValid);
    }

    // ── YAML ─────────────────────────────────────────────────────────────

    [Fact]
    public void ValidYaml_ReturnsSuccess()
    {
        var result = SyntaxValidationService.Validate("key: value\nlist:\n  - item1\n  - item2", "file.yaml");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidYml_Extension_ReturnsSuccess()
    {
        var result = SyntaxValidationService.Validate("name: test", "config.yml");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidYaml_ReturnsFail()
    {
        // Invalid: mixing block and flow sequences
        var result = SyntaxValidationService.Validate("key: :\n  bad: [unclosed", "file.yaml");
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    // ── TOML ─────────────────────────────────────────────────────────────

    [Fact]
    public void ValidToml_ReturnsSuccess()
    {
        var result = SyntaxValidationService.Validate("[section]\nkey = \"value\"\nnumber = 42", "file.toml");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidToml_ReturnsFail()
    {
        var result = SyntaxValidationService.Validate("key = value without quotes", "file.toml");
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void EmptyContent_AlwaysReturnsSuccess()
    {
        Assert.True(SyntaxValidationService.Validate("", "file.json").IsValid);
        Assert.True(SyntaxValidationService.Validate("", "file.xml").IsValid);
        Assert.True(SyntaxValidationService.Validate("", "file.yaml").IsValid);
    }

    [Fact]
    public void UnknownExtension_ReturnsSuccess()
    {
        var result = SyntaxValidationService.Validate("anything goes here", "file.txt");
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("file.sh")]
    [InlineData("file.py")]
    [InlineData("file.cs")]
    [InlineData("Makefile")]
    public void UnsupportedExtension_ReturnsSuccess(string fileName)
    {
        var result = SyntaxValidationService.Validate("any content", fileName);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ExtensionMatching_IsCaseInsensitive()
    {
        var result = SyntaxValidationService.Validate("{bad}", "FILE.JSON");
        Assert.False(result.IsValid);
    }
}
