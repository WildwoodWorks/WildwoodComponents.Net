using System.Text;

namespace WildwoodComponents.Tests;

/// <summary>
/// No source file of the registration + subscription surface carries an invisible character
/// literally. Characters that must be IN a value are written as escapes.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a real near-miss. The public-catalog cache keys are joined with U+0001,
/// a character neither an app id nor a currency code can contain, so ("tenant1", "GBP") and
/// ("tenant1GBP", null) cannot land on one key. One package spelled that separator as a RAW U+0001
/// between the quotes. It ran correctly — and it read, in every editor and every diff, as
/// <c>""</c>. A reviewer called it an empty string and a bug; an <c>.editorconfig</c> charset rule,
/// a formatter, a copy/paste through a web form or a merge tool would have made the reviewer right,
/// silently, with nothing visible in the diff. At that point one tenant is served another tenant's
/// prices for the length of the cache window.
/// </para>
/// <para>
/// So: the bytes may contain tab, CR and LF and nothing else below U+0020. The same rule covers the
/// zero-width characters, a BOM anywhere but the first character, and the non-breaking spaces —
/// <c>FormatHelpers.FormatMoney</c> deliberately EMITS U+00A0 and U+202F, and writes both as
/// <c> </c> / <c> </c> so the intent survives a reformat. In markup the escape is
/// <c>&amp;nbsp;</c>.
/// </para>
/// </remarks>
public class InvisibleCharacterSourceGuardTests
{
    /// <summary>
    /// The directory holding the solution file, found by walking up from the test assembly — so a
    /// checkout at any path works, and a run from anywhere does too.
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WildwoodComponents.Net.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find WildwoodComponents.Net.slnx above {AppContext.BaseDirectory}.");
    }

    /// <summary>Directories scanned whole, relative to the repo root.</summary>
    private static readonly string[] GuardedDirectories =
    [
        "WildwoodComponents.Blazor/Components/RegistrationSubscription",
        "WildwoodComponents.Razor/Components/RegistrationSubscription",
        "WildwoodComponents.Razor/Views/Shared/Components/RegistrationSubscriptionPricing"
    ];

    /// <summary>
    /// The files outside those folders that the same surface is made of — the two catalog caches
    /// (where the U+0001 separator lives), the view models, the browser script and stylesheet, and
    /// the Shared helpers whose output contains the spaces this test is about.
    /// </summary>
    private static readonly string[] GuardedFiles =
    [
        "WildwoodComponents.Blazor/Services/PublicCatalogService.cs",
        "WildwoodComponents.Razor/Services/WildwoodPublicCatalogService.cs",
        "WildwoodComponents.Razor/Services/IWildwoodPublicCatalogService.cs",
        "WildwoodComponents.Razor/Models/RegistrationSubscriptionPricingModels.cs",
        "WildwoodComponents.Razor/Views/Shared/_RegSubPackCardBody.cshtml",
        "WildwoodComponents.Razor/wwwroot/js/regsub-pricing.js",
        "WildwoodComponents.Razor/wwwroot/css/regsub.css",
        "WildwoodComponents.Shared/Utilities/RegistrationSubscriptionLabels.cs",
        "WildwoodComponents.Shared/Utilities/FormatHelpers.cs",
        "WildwoodComponents.Shared/Utilities/CatalogHelpers.cs"
    ];

    private static readonly string[] GuardedExtensions = [".cs", ".razor", ".cshtml", ".js", ".css"];

    private static List<string> GuardedSources()
    {
        var root = RepoRoot();
        var files = new List<string>();

        foreach (var relative in GuardedDirectories)
        {
            var dir = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(dir), $"Expected {dir} to exist.");

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (!GuardedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                files.Add(file);
            }
        }

        foreach (var relative in GuardedFiles)
        {
            var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"Expected {relative} to exist.");
            files.Add(file);
        }

        return files;
    }

    /// <summary>
    /// A character that must never appear literally, and the escape to write instead. Tab, CR and
    /// LF are the three control characters source files are made of, so they are not here.
    /// </summary>
    private static string? Forbidden(char c, int index)
    {
        if (c == '\t' || c == '\r' || c == '\n') return null;

        if (c < 0x20 || c == 0x7F) return $"U+{(int)c:X4} (control) — write it as \\u{(int)c:x4}";

        return c switch
        {
            ' ' => "U+00A0 no-break space — write \\u00a0, or &nbsp; in markup",
            ' ' => "U+202F narrow no-break space — write \\u202f, or &#8239; in markup",
            '​' => "U+200B zero-width space — delete it, or write \\u200b",
            '‌' => "U+200C zero-width non-joiner — delete it, or write \\u200c",
            '‍' => "U+200D zero-width joiner — delete it, or write \\u200d",
            '⁠' => "U+2060 word joiner — delete it, or write \\u2060",
            '­' => "U+00AD soft hyphen — delete it, or write \\u00ad",
            '﻿' => index == 0
                ? null // a leading byte-order mark is how the tooling writes these files
                : "U+FEFF byte-order mark in the middle of the file — delete it",
            _ => null
        };
    }

    [Fact]
    public void The_guard_is_looking_at_the_files_it_thinks_it_is()
    {
        var sources = GuardedSources();

        Assert.NotEmpty(sources);
        Assert.Contains(sources, f => f.EndsWith("WildwoodPublicCatalogService.cs", StringComparison.Ordinal));
        Assert.Contains(sources, f => f.EndsWith("PublicCatalogService.cs", StringComparison.Ordinal));
        Assert.Contains(sources, f => f.EndsWith("FormatHelpers.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void No_guarded_source_carries_an_invisible_character_literally()
    {
        var root = RepoRoot();
        var offences = new StringBuilder();

        foreach (var file in GuardedSources())
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            var line = 1;
            var column = 1;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var problem = Forbidden(c, i);
                if (problem is not null)
                {
                    offences.AppendLine($"{relative}:{line}:{column}: {problem}");
                }

                if (c == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }
        }

        Assert.True(
            offences.Length == 0,
            "A source file carries a character no editor shows, so a reformat can delete it with "
                + "nothing visible in the diff. Write the escape instead:\n" + offences);
    }
}
