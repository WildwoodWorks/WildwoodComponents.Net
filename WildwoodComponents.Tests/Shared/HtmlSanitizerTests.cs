using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

public class HtmlSanitizerTests
{
    // ===== NULL / EMPTY INPUT =====

    [Fact]
    public void Sanitize_NullInput_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, HtmlSanitizer.Sanitize(null));
    }

    [Fact]
    public void Sanitize_EmptyString_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, HtmlSanitizer.Sanitize(""));
    }

    // ===== SAFE CONTENT PRESERVED =====

    [Fact]
    public void Sanitize_SafeHtml_PreservesContent()
    {
        var html = "<p>Hello <strong>world</strong></p>";
        Assert.Equal(html, HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_SafeAttributes_PreservesContent()
    {
        var html = """<a href="https://example.com" class="link">Click</a>""";
        Assert.Equal(html, HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_PlainText_PreservesContent()
    {
        var text = "This is just plain text with no HTML.";
        Assert.Equal(text, HtmlSanitizer.Sanitize(text));
    }

    // ===== DANGEROUS TAGS =====

    [Fact]
    public void Sanitize_ScriptTag_RemovesTagAndContent()
    {
        var html = """<p>Hello</p><script>alert('xss')</script><p>World</p>""";
        Assert.Equal("<p>Hello</p><p>World</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_StyleTag_RemovesTagAndContent()
    {
        var html = "<p>Hello</p><style>body { display: none; }</style><p>World</p>";
        Assert.Equal("<p>Hello</p><p>World</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_IframeTag_RemovesTagAndContent()
    {
        var html = """<p>Text</p><iframe src="evil.com">content</iframe><p>More</p>""";
        Assert.Equal("<p>Text</p><p>More</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_ObjectTag_RemovesTag()
    {
        var html = """<p>Before</p><object data="evil.swf">fallback</object><p>After</p>""";
        Assert.Equal("<p>Before</p><p>After</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_EmbedTag_RemovesSelfClosing()
    {
        var html = """<p>Before</p><embed src="evil.swf"><p>After</p>""";
        Assert.Equal("<p>Before</p><p>After</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_FormTag_RemovesTagAndContent()
    {
        var html = """<p>Before</p><form action="/steal"><input type="text"></form><p>After</p>""";
        Assert.Equal("<p>Before</p><p>After</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_LinkTag_RemovesSelfClosing()
    {
        var html = """<p>Before</p><link rel="stylesheet" href="evil.css"><p>After</p>""";
        Assert.Equal("<p>Before</p><p>After</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_MetaTag_RemovesSelfClosing()
    {
        var html = """<p>Before</p><meta http-equiv="refresh" content="0;url=evil.com"><p>After</p>""";
        Assert.Equal("<p>Before</p><p>After</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_DangerousTags_CaseInsensitive()
    {
        var html = """<p>Hello</p><SCRIPT>alert('xss')</SCRIPT><p>World</p>""";
        Assert.Equal("<p>Hello</p><p>World</p>", HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_MultipleDangerousTags_RemovesAll()
    {
        var html = """<script>bad1</script><p>Good</p><style>.evil{}</style><iframe>bad2</iframe>""";
        Assert.Equal("<p>Good</p>", HtmlSanitizer.Sanitize(html));
    }

    // ===== EVENT HANDLER ATTRIBUTES =====

    [Fact]
    public void Sanitize_OnclickAttribute_Removes()
    {
        var html = """<button onclick="alert('xss')">Click</button>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("onclick", result);
        Assert.Contains("Click", result);
    }

    [Fact]
    public void Sanitize_OnmouseoverAttribute_Removes()
    {
        var html = """<div onmouseover="steal()">Hover</div>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("onmouseover", result);
        Assert.Contains("Hover", result);
    }

    [Fact]
    public void Sanitize_MultipleEventHandlers_RemovesAll()
    {
        var html = """<div onclick="a()" onmouseover="b()" onload="c()">Text</div>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("onclick", result);
        Assert.DoesNotContain("onmouseover", result);
        Assert.DoesNotContain("onload", result);
        Assert.Contains("Text", result);
    }

    [Fact]
    public void Sanitize_EventHandlerSingleQuotes_Removes()
    {
        var html = "<div onclick='alert(1)'>Text</div>";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("onclick", result);
    }

    [Fact]
    public void Sanitize_EventHandlerUnquoted_Removes()
    {
        var html = "<div onclick=alert(1)>Text</div>";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("onclick", result);
        Assert.Contains("Text", result);
    }

    [Fact]
    public void Sanitize_EventHandlerPreservesSafeAttributes()
    {
        var html = """<div class="safe" onclick="evil()" id="myDiv">Text</div>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("onclick", result);
        Assert.Contains("class=\"safe\"", result);
        Assert.Contains("id=\"myDiv\"", result);
    }

    // ===== DANGEROUS ATTRIBUTES =====

    [Fact]
    public void Sanitize_SrcdocAttribute_Removes()
    {
        var html = """<iframe srcdoc="<script>evil()</script>">content</iframe>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("srcdoc", result);
    }

    [Fact]
    public void Sanitize_FormactionAttribute_Removes()
    {
        var html = """<button formaction="https://evil.com/steal">Submit</button>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("formaction", result);
        Assert.Contains("Submit", result);
    }

    [Fact]
    public void Sanitize_DangerousAttributeWithMixedQuotes_Removes()
    {
        var html = """<button formaction="https://evil.com/it's-here">Submit</button>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("formaction", result);
        Assert.Contains("Submit", result);
    }

    // ===== DANGEROUS URL SCHEMES =====

    [Fact]
    public void Sanitize_JavascriptHref_RemovesAttribute()
    {
        var html = """<a href="javascript:alert(1)">Click</a>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("javascript:", result);
        Assert.DoesNotContain("href", result);
        Assert.Contains("Click", result);
    }

    [Fact]
    public void Sanitize_DataHref_RemovesAttribute()
    {
        var html = """<a href="data:text/html,<script>alert(1)</script>">Click</a>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("data:", result);
        Assert.DoesNotContain("href", result);
    }

    [Fact]
    public void Sanitize_VbscriptHref_RemovesAttribute()
    {
        var html = """<a href="vbscript:MsgBox('xss')">Click</a>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("vbscript:", result);
        Assert.DoesNotContain("href", result);
    }

    [Fact]
    public void Sanitize_JavascriptSrc_RemovesAttribute()
    {
        var html = """<img src="javascript:alert(1)">""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("javascript:", result);
        Assert.DoesNotContain("src", result);
    }

    [Fact]
    public void Sanitize_SafeHref_Preserves()
    {
        var html = """<a href="https://example.com">Link</a>""";
        Assert.Equal(html, HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_DangerousScheme_CaseInsensitive()
    {
        var html = """<a href="JAVASCRIPT:alert(1)">Click</a>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("JAVASCRIPT:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", result);
    }

    // ===== COMBINED / COMPLEX CASES =====

    [Fact]
    public void Sanitize_NestedDangerousContent_RemovesAll()
    {
        var html = """<div onclick="evil()"><script>alert(1)</script><a href="javascript:void(0)">Link</a></div>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("onclick", result);
        Assert.DoesNotContain("<script>", result);
        Assert.DoesNotContain("javascript:", result);
        Assert.Contains("Link", result);
    }

    [Fact]
    public void Sanitize_RealWorldDisclaimer_PreservesContent()
    {
        var html = """
            <h2>Terms of Service</h2>
            <p>By using this service, you agree to the following:</p>
            <ul>
                <li>You will not misuse the service</li>
                <li>You accept our <a href="https://example.com/privacy">privacy policy</a></li>
            </ul>
            <p><strong>Last updated:</strong> January 2026</p>
            """;
        // Should be unchanged since it contains no dangerous content
        Assert.Equal(html, HtmlSanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_MultipleTagsOnMultipleElements_RemovesAll()
    {
        var html = """<p onclick="a()" onmouseover="b()">Text1</p><p onclick="c()">Text2</p>""";
        var result = HtmlSanitizer.Sanitize(html);
        Assert.DoesNotContain("onclick", result);
        Assert.DoesNotContain("onmouseover", result);
        Assert.Contains("Text1", result);
        Assert.Contains("Text2", result);
    }
}
