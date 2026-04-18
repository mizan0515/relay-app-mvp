using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class CanonicalHashTests
{
    [Fact]
    public void Identical_input_produces_identical_hash()
    {
        var a = CanonicalHash.OfString("packet-body\nline-2\n");
        var b = CanonicalHash.OfString("packet-body\nline-2\n");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Different_input_produces_different_hash()
    {
        var a = CanonicalHash.OfString("packet-body-1");
        var b = CanonicalHash.OfString("packet-body-2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Line_ending_variants_collapse_to_same_hash()
    {
        var lf = CanonicalHash.OfString("a\nb\nc");
        var crlf = CanonicalHash.OfString("a\r\nb\r\nc");
        var cr = CanonicalHash.OfString("a\rb\rc");
        Assert.Equal(lf, crlf);
        Assert.Equal(lf, cr);
    }

    [Fact]
    public void Trailing_whitespace_per_line_is_stripped()
    {
        var clean = CanonicalHash.OfString("alpha\nbeta\n");
        var dirty = CanonicalHash.OfString("alpha   \nbeta\t\t\n");
        Assert.Equal(clean, dirty);
    }

    [Fact]
    public void Trailing_newlines_are_trimmed()
    {
        var one = CanonicalHash.OfString("payload");
        var many = CanonicalHash.OfString("payload\n\n\n");
        Assert.Equal(one, many);
    }

    [Fact]
    public void Leading_whitespace_and_interior_whitespace_are_preserved()
    {
        var a = CanonicalHash.OfString("  indented");
        var b = CanonicalHash.OfString("indented");
        Assert.NotEqual(a, b);

        var c = CanonicalHash.OfString("a b");
        var d = CanonicalHash.OfString("a  b");
        Assert.NotEqual(c, d);
    }

    [Fact]
    public void Empty_and_null_hash_to_sha256_of_empty_bytes()
    {
        const string shaOfEmpty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        Assert.Equal(shaOfEmpty, CanonicalHash.OfString(""));
        Assert.Equal(shaOfEmpty, CanonicalHash.OfString(null));
    }

    [Fact]
    public void OfBytes_matches_OfString_for_utf8_input()
    {
        const string payload = "dad-v2 canonical";
        var viaString = CanonicalHash.OfString(payload);
        var viaBytes = CanonicalHash.OfBytes(System.Text.Encoding.UTF8.GetBytes(payload));
        Assert.Equal(viaString, viaBytes);
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        const string input = "line-1   \r\nline-2\t\r\n\r\n";
        var once = CanonicalHash.Normalize(input);
        var twice = CanonicalHash.Normalize(once);
        Assert.Equal(once, twice);
        Assert.Equal("line-1\nline-2", once);
    }
}
