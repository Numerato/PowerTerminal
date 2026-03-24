using System;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using YamlDotNet.Core;

namespace PowerTerminal.Services
{
    public class ValidationResult
    {
        public bool   IsValid      { get; set; } = true;
        public string ErrorMessage { get; set; } = string.Empty;
        public int    Line         { get; set; }
        public int    Column       { get; set; }
    }

    public static class SyntaxValidationService
    {
        /// <summary>
        /// Validates <paramref name="content"/> based on the file type derived from
        /// <paramref name="filePath"/>. Returns a successful result for unsupported types.
        /// </summary>
        public static ValidationResult Validate(string content, string filePath)
        {
            if (string.IsNullOrEmpty(content)) return Ok();

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            return ext switch
            {
                ".json"                                => ValidateJson(content),
                ".xml" or ".xaml" or ".svg"
                    or ".csproj" or ".xshd" or ".xhtml" => ValidateXml(content),
                ".yaml" or ".yml"                      => ValidateYaml(content),
                ".toml"                                => ValidateToml(content),
                _                                      => Ok()
            };
        }

        // ── JSON ─────────────────────────────────────────────────────────────

        private static ValidationResult ValidateJson(string content)
        {
            try
            {
                JsonDocument.Parse(content, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling     = JsonCommentHandling.Disallow
                });
                return Ok();
            }
            catch (JsonException ex)
            {
                int line = (int)ex.LineNumber.GetValueOrDefault() + 1;
                int col  = (int)ex.BytePositionInLine.GetValueOrDefault() + 1;
                return Fail(ex.Message, line, col);
            }
        }

        // ── XML ──────────────────────────────────────────────────────────────

        private static ValidationResult ValidateXml(string content)
        {
            try
            {
                XDocument.Parse(content, LoadOptions.SetLineInfo);
                return Ok();
            }
            catch (System.Xml.XmlException ex)
            {
                return Fail(ex.Message, ex.LineNumber, ex.LinePosition);
            }
        }

        // ── YAML ─────────────────────────────────────────────────────────────

        private static ValidationResult ValidateYaml(string content)
        {
            try
            {
                var stream = new YamlDotNet.RepresentationModel.YamlStream();
                stream.Load(new StringReader(content));
                return Ok();
            }
            catch (YamlException ex)
            {
                int line = (int)ex.Start.Line;
                int col  = (int)ex.Start.Column;
                string msg = ex.Message;
                int paren = msg.LastIndexOf("): ", StringComparison.Ordinal);
                if (paren >= 0) msg = msg[(paren + 3)..];
                return Fail(msg, line, col);
            }
        }

        // ── TOML ─────────────────────────────────────────────────────────────

        private static ValidationResult ValidateToml(string content)
        {
            var doc = Tomlyn.Parsing.SyntaxParser.Parse(content, "file.toml", false);
            if (!doc.HasErrors) return Ok();

            var first = doc.Diagnostics[0];
            // SourceSpan only has Offset+Length; derive line/col from content
            OffsetToLineCol(content, first.Span.Offset, out int line, out int col);
            return Fail(first.Message, line, col);
        }

        private static void OffsetToLineCol(string content, int offset, out int line, out int col)
        {
            line = 1; col = 1;
            for (int i = 0; i < offset && i < content.Length; i++)
            {
                if (content[i] == '\n') { line++; col = 1; }
                else col++;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static ValidationResult Ok() => new() { IsValid = true };

        private static ValidationResult Fail(string message, int line, int col) => new()
        {
            IsValid      = false,
            ErrorMessage = message,
            Line         = line,
            Column       = col
        };
    }
}
