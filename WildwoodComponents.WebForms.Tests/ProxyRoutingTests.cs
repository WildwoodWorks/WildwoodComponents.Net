using System.Threading.Tasks;
using System.Web;
using WildwoodComponents.WebForms.Handlers;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    /// <summary>
    /// Exercises the routing and URL-safety helpers on <see cref="WildwoodProxyHandlerBase"/>.
    /// They are protected, so this subclass re-exposes them; nothing else about the handler
    /// is involved.
    /// </summary>
    public class ProxyRoutingTests
    {
        private sealed class Probe : WildwoodProxyHandlerBase
        {
            protected override Task<bool> TryHandleAsync(HttpContextBase context, string route, string method)
            {
                return Task.FromResult(false);
            }

            public static string? Segment(string route, string prefix, string? suffix)
            {
                return MatchSegment(route, prefix, suffix);
            }

            public static bool Local(string? url)
            {
                return IsLocalUrl(url);
            }

            public static string Return(string? url)
            {
                return ResolveReturnUrl(url);
            }

            public static bool Equals(string route, string literal)
            {
                return RouteEquals(route, literal);
            }
        }

        // ── IsLocalUrl ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("/Default.aspx")]
        [InlineData("/")]
        [InlineData("/a/b/c?d=e#f")]
        [InlineData("~/Default.aspx")]
        public void Rooted_and_app_relative_paths_are_local(string url)
        {
            Assert.True(Probe.Local(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("//evil.example")]                 // protocol-relative
        [InlineData("/\\evil.example")]                // backslash variant
        [InlineData("https://evil.example")]
        [InlineData("http://evil.example/path")]
        [InlineData("javascript:alert(1)")]
        [InlineData("Default.aspx")]                   // relative, not rooted
        public void Off_site_and_unrooted_targets_are_rejected(string? url)
        {
            Assert.False(Probe.Local(url));
        }

        [Theory]
        [InlineData("/\t/evil.example")]
        [InlineData("/\n/evil.example")]
        [InlineData("/\r/evil.example")]
        [InlineData("/\t\n/evil.example")]
        public void Control_characters_cannot_smuggle_a_protocol_relative_target(string url)
        {
            // Browsers strip tab, CR and LF anywhere in a URL before parsing it, so each of
            // these navigates to "//evil.example" — a different site — even though the raw
            // string does not start with "//".
            Assert.False(Probe.Local(url));
        }

        [Fact]
        public void ResolveReturnUrl_falls_back_to_the_site_root()
        {
            Assert.Equal("/Home.aspx", Probe.Return("/Home.aspx"));
            Assert.Equal("/", Probe.Return("https://evil.example"));
            Assert.Equal("/", Probe.Return(null));
            Assert.Equal("/", Probe.Return("/\t/evil.example"));
        }

        // ── MatchSegment ─────────────────────────────────────────────────────────

        [Fact]
        public void An_id_keeps_the_case_it_arrived_with()
        {
            // Credential and device ids are opaque strings, never guaranteed lower-case.
            // Lower-casing one would silently retarget the call at a different credential.
            Assert.Equal("AbC123", Probe.Segment("/credentials/AbC123/set-primary", "/credentials/", "/set-primary"));
            Assert.Equal("DeF456", Probe.Segment("/trusted-devices/DeF456", "/trusted-devices/", null));
        }

        [Fact]
        public void The_fixed_parts_of_a_route_match_case_insensitively()
        {
            Assert.Equal("abc", Probe.Segment("/Credentials/abc/Set-Primary", "/credentials/", "/set-primary"));
        }

        [Fact]
        public void An_id_is_url_decoded()
        {
            Assert.Equal("a b", Probe.Segment("/trusted-devices/a%20b", "/trusted-devices/", null));
        }

        [Theory]
        [InlineData("/credentials/", "/credentials/", null)]                       // no id at all
        [InlineData("/credentials", "/credentials/", null)]                        // prefix only
        [InlineData("/credentials//set-primary", "/credentials/", "/set-primary")] // empty id
        [InlineData("/credentials/a/b", "/credentials/", null)]                    // extra separator
        [InlineData("/other/abc", "/credentials/", null)]                          // different route
        [InlineData("/credentials/abc", "/credentials/", "/set-primary")]          // missing suffix
        [InlineData("/set-primary", "/credentials/", "/set-primary")]              // suffix only
        public void A_route_of_the_wrong_shape_matches_nothing(string route, string prefix, string? suffix)
        {
            Assert.Null(Probe.Segment(route, prefix, suffix));
        }

        [Fact]
        public void RouteEquals_ignores_case()
        {
            Assert.True(Probe.Equals("/login", "/login"));
            Assert.True(Probe.Equals("/Login", "/login"));
            Assert.False(Probe.Equals("/login/extra", "/login"));
        }
    }
}
