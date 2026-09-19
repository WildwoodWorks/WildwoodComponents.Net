using System;
using System.IO;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    /// <summary>
    /// The browser half of Campaign Attribution on a WebForms site: the engine is PACKED from the Razor
    /// package rather than copied into this one, and the proxy handler forwards what
    /// <c>authentication.js</c> posts. Both are properties of files rather than of runtime behaviour,
    /// so they are checked against the source.
    /// </summary>
    public class AttributionPackagingTests
    {
        [Fact]
        public void The_engine_is_packed_from_the_Razor_project_not_copied_into_this_one()
        {
            // WebForms rule 3: one source of truth per asset. A copy under this project would drift
            // from the file Razor serves, and the two stacks would diverge silently.
            var csproj = ReadSource("WildwoodComponents.WebForms/WildwoodComponents.WebForms.csproj");
            Assert.Contains("$(WildwoodSharedAssets)\\js\\attribution.js", csproj, StringComparison.Ordinal);
            Assert.False(
                Directory.Exists(Path.Combine(RepoRoot(), "WildwoodComponents.WebForms", "wwwroot")),
                "WildwoodComponents.WebForms must not carry its own copy of the shared assets.");
        }

        [Fact]
        public void The_shared_registration_script_posts_the_payload_and_clears_it_on_success()
        {
            // This is the script the package packs, so what it sends is this package's wire shape too.
            var script = ReadSource("WildwoodComponents.Razor/wwwroot/js/authentication.js");
            Assert.Contains("window.wildwoodAttribution.getForRegistration()", script, StringComparison.Ordinal);
            Assert.Contains("window.wildwoodAttribution.clear()", script, StringComparison.Ordinal);
        }

        [Fact]
        public void The_auth_proxy_forwards_the_posted_payload()
        {
            var handler = ReadSource("WildwoodComponents.WebForms/Handlers/WildwoodAuthProxyHandler.cs");
            Assert.Contains("public AttributionPayloadModel? Attribution { get; set; }", handler, StringComparison.Ordinal);
            Assert.Contains("Attribution = body.Attribution", handler, StringComparison.Ordinal);
        }

        private static string ReadSource(string relativePath)
        {
            var full = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), "Expected " + relativePath + " to exist.");
            return File.ReadAllText(full);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "WildwoodComponents.Net.slnx")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not find WildwoodComponents.Net.slnx above " + AppContext.BaseDirectory + ".");
        }
    }
}
