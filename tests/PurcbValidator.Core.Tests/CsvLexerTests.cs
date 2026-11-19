using PurcbValidator.Core.Csv;
using Xunit;

namespace PurcbValidator.Core.Tests;

public class CsvLexerTests
{
    private static List<CsvRow> Parse(string text, List<string>? fileErrors = null, List<long>? lineNumbers = null) =>
        new CsvLexer("|", "\"").Parse(text, fileErrors ?? new List<string>(), lineNumbers);

    [Fact]
    public void SimpleRow()
    {
        var rows = Parse("a|b|c\n");
        Assert.Single(rows);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0].Fields);
    }

    [Fact]
    public void QuotedFieldWithDelimiterAndNewline()
    {
        var rows = Parse("\"x|y\"|\"line1\nline2\"|plain\n");
        Assert.Single(rows);
        Assert.Equal(new[] { "x|y", "line1\nline2", "plain" }, rows[0].Fields);
    }

    [Fact]
    public void DoubledQualifierInsideQuotedField()
    {
        var rows = Parse("\"Он сказал \"\"привет\"\"\"|b\n");
        Assert.Single(rows);
        Assert.Equal("Он сказал \"привет\"", rows[0].Fields[0]);
    }

    [Fact]
    public void TrailingEmptyField()
    {
        var rows = Parse("a|\n");
        Assert.Equal(new[] { "a", "" }, rows[0].Fields);
    }

    [Fact]
    public void EmptyValueVsMissingValue()
    {
        var rows = Parse("a|\"\"|c\n");
        Assert.Equal(new[] { "a", "", "c" }, rows[0].Fields);
    }

    [Fact]
    public void QuoteInMiddleOfField_IsReported()
    {
        var rows = Parse("ab\"cd|e\n");
        Assert.True(rows[0].HasErrors);
        Assert.Contains("середине", rows[0].Errors[0]);
        Assert.Equal("ab\"cd", rows[0].Fields[0]);
    }

    [Fact]
    public void UnterminatedQuote_IsFileError()
    {
        var fileErrors = new List<string>();
        Parse("\"незакрытая кавычка|x\n", fileErrors);
        Assert.Contains(fileErrors, e => e.Contains("Незакрытый"));
    }

    [Fact]
    public void MultipleRows_CountAndLines()
    {
        var lines = new List<long>();
        var rows = Parse("h1|h2\nv1|v2\nv3|v4\n", new List<string>(), lines);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, lines);
    }

    [Fact]
    public void CustomDelimiter()
    {
        var rows = new CsvLexer(";", "\"").Parse("a;b;c\n", new List<string>());
        Assert.Equal(3, rows[0].Fields.Count);
    }

    [Fact]
    public void CRLF_IsHandled()
    {
        var rows = Parse("a|b\r\nc|d\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0].Fields);
        Assert.Equal(new[] { "c", "d" }, rows[1].Fields);
    }
}
