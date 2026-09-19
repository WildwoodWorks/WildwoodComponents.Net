using System.Text.RegularExpressions;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests;

/// <summary>
/// Two rules that can only be checked against the source, because what they forbid is a SHAPE
/// rather than a value: a money formatter written by hand inside a component, and an entitlement
/// mutation that names the wrong reason (or no reason at all).
/// </summary>
/// <remarks>
/// <para>
/// Money: every amount the Blazor and Razor packages render goes through
/// <see cref="WildwoodComponents.Shared.Utilities.FormatHelpers.FormatMoney"/> — directly, or via
/// <c>CatalogHelpers.FormatPrice</c> / <c>AddOnRowRules.FormatPrice</c>, which resolve the item's
/// own currency first. There were five private copies plus two culture switches, each with its own
/// three-to-six-entry symbol table, so the same CHF plan rendered "$79.00", "CHF 79.00" and
/// "CHF79.00" on three surfaces of one app, and a missing amount was a hard-coded "$0.00" even for
/// a customer billed in francs.
/// </para>
/// <para>
/// Reasons: the six words of the JS <c>entitlementsChanged</c> event are one cross-stack
/// vocabulary. A test that a mutation "raises something" would pass with every reason set to
/// "manual", which is exactly the failure worth catching, so these assert the WORD at each call
/// site.
/// </para>
/// </remarks>
public class MoneyAndEntitlementSourceGuardTests
{
    #region Locating the source tree

    /// <summary>
    /// The directory holding the solution file, found by walking up from the test assembly. The
    /// test project builds into <c>bin/{config}/{tfm}/</c> under it, so the walk is short; a
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

    /// <summary>
    /// Every shipped source file of the two component packages: C#, Razor markup, MVC views and the
    /// Razor package's browser scripts. Build output and the vendored html2canvas are not ours.
    /// </summary>
    private static List<string> ShippedSourceFiles()
    {
        var root = RepoRoot();
        var files = new List<string>();

        foreach (var project in new[] { "WildwoodComponents.Blazor", "WildwoodComponents.Razor" })
        {
            var projectRoot = Path.Combine(root, project);
            Assert.True(Directory.Exists(projectRoot), $"Expected {projectRoot} to exist.");

            foreach (var pattern in new[] { "*.cs", "*.razor", "*.cshtml", "*.js" })
            {
                foreach (var file in Directory.EnumerateFiles(projectRoot, pattern, SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;
                    if (relative.EndsWith("html2canvas.min.js", StringComparison.Ordinal)) continue;
                    files.Add(file);
                }
            }
        }

        Assert.NotEmpty(files);
        return files;
    }

    private static string ReadSource(string relativePath)
    {
        var full = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"Expected {relativePath} to exist.");
        return File.ReadAllText(full);
    }

    #endregion

    #region No private money formatter

    /// <summary>
    /// Each entry is a way of formatting money by hand that the two packages must not contain
    /// again. One deliberate exception, and only one: the declaration of a deprecated public
    /// wrapper — see <see cref="IsDeprecatedWrapperDeclaration"/>. Every money-rendering site calls
    /// a shared helper.
    /// </summary>
    public static TheoryData<string, string> BannedMoneyPatterns() => new()
    {
        // .NET's "C" format reads CultureInfo, so the server and the browser disagree.
        { @"ToString\(""C", @"ToString(""C...) — culture-dependent money; use FormatHelpers.FormatMoney" },
        { @"GetCultureInfo\(", "CultureInfo.GetCultureInfo — a per-currency culture switch for money" },
        { @":C\d?\}", @"{amount:C} interpolation — culture-dependent money" },

        // A missing amount is zero IN THE ITEM'S CURRENCY, never dollars.
        { @"\$0\.00", @"hard-coded ""$0.00"" — format 0 in the item's own currency instead" },

        // Symbol tables. Every one of these was a different subset of currencies.
        { "GetCurrencySymbol", "a currency-symbol lookup — FormatMoney carries the CLDR table" },
        { @"""USD""\s*=>", "a C# currency symbol table" },
        { @"USD\s*:\s*'", "a JS currency symbol table" },
        { @"'\$'\s*\+", @"JS string-concatenating a ""$"" onto an amount" },
        { @"symbols\[", "a JS symbol-table lookup" },
    };

    /// <summary>
    /// The single exception to the ban: the DECLARATION of a deprecated public wrapper. Neither
    /// <c>ViewHelpers.GetCurrencySymbol</c> nor <c>ViewHelpers.FormatAmount</c> may be CALLED any
    /// more, but both remain declared — they are public API a host's own <c>.cshtml</c> can bind to,
    /// and deleting them would break that host at compile time to no end.
    /// </summary>
    /// <remarks>
    /// The shape is deliberately narrow: an <c>[Obsolete]</c>-marked one-line delegation to the
    /// same-named <see cref="WildwoodComponents.Shared.Utilities.FormatHelpers"/> method, in the two
    /// helper files that own these names. Anything else — a call from a component, a view, a script,
    /// or a second call anywhere in these files — still fails. (<c>FormatHelpers.cs</c> lives in
    /// WildwoodComponents.Shared, which this guard does not walk; it is named here so the allowance
    /// still reads correctly if the walked set ever grows.)
    /// </remarks>
    private static readonly string[] DeprecatedWrapperFiles =
    [
        "WildwoodComponents.Razor/Helpers/ViewHelpers.cs",
        "WildwoodComponents.Shared/Utilities/FormatHelpers.cs",
    ];

    private static readonly Regex DeprecatedWrapperDeclaration = new(
        @"^public static string (?<name>GetCurrencySymbol|FormatAmount)\([^)]*\)"
            + @"(\s*=>\s*FormatHelpers\.\k<name>\([^)]*\);)?$",
        RegexOptions.CultureInvariant);

    private static bool IsDeprecatedWrapperDeclaration(string relativePath, string line)
    {
        return DeprecatedWrapperFiles.Contains(relativePath, StringComparer.Ordinal)
            && DeprecatedWrapperDeclaration.IsMatch(line.Trim());
    }

    [Theory]
    [MemberData(nameof(BannedMoneyPatterns))]
    public void No_private_money_formatter_remains_in_Blazor_or_Razor(string pattern, string why)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in ShippedSourceFiles())
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // A comment explaining what was removed is not a formatter.
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal)
                    || trimmed.StartsWith("/*", StringComparison.Ordinal)
                    || trimmed.StartsWith("@*", StringComparison.Ordinal))
                {
                    continue;
                }

                // The deprecated wrapper's own declaration — see IsDeprecatedWrapperDeclaration.
                if (IsDeprecatedWrapperDeclaration(relativePath, lines[i])) continue;

                if (regex.IsMatch(lines[i]))
                {
                    offenders.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, $"{why}. Found:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// The four browser scripts that must format money client-side carry the SAME helper, by name.
    /// There is no shared script every page loads, so the copies are the contract; one drifting
    /// copy is how a currency starts rendering two ways in one app again.
    /// </summary>
    [Theory]
    [InlineData("payment.js")]
    [InlineData("payment-form.js")]
    [InlineData("token-registration.js")]
    [InlineData("subscription-admin.js")]
    public void Razor_scripts_that_format_money_use_the_one_shared_helper(string script)
    {
        var source = ReadSource($"WildwoodComponents.Razor/wwwroot/js/{script}");

        Assert.Contains("function wwFormatMoney(amount, currency)", source);
        // Fixed locale, as in the JS SDK: a server render and a browser render must agree.
        Assert.Contains("new Intl.NumberFormat('en-US', { style: 'currency', currency: code })", source);
        // Intl throws on a non-three-letter code; the fallback says the code, never a dollar sign.
        Assert.Contains("return code + ' ' + value.toFixed(2);", source);
    }

    #endregion

    #region Entitlement reasons

    /// <summary>
    /// Every Blazor mutation path that changes entitlements, and the reason it must name. The
    /// snippets are the call sites themselves, so a path that stops raising — or starts naming the
    /// catch-all "manual" for a plan change — fails here.
    /// </summary>
    public static TheoryData<string, string> BlazorEntitlementCallSites() => new()
    {
        // Tier change / first subscribe, every scope. SubscriptionAdminComponent.
        {
            "WildwoodComponents.Blazor/Components/Subscription/Admin/SubscriptionAdminComponent.razor.cs",
            "await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.TierChange);"
        },
        {
            "WildwoodComponents.Blazor/Components/Subscription/Admin/SubscriptionAdminComponent.razor.cs",
            "await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.Cancel);"
        },
        // Feature overrides are granted and revoked by hand, outside any plan.
        {
            "WildwoodComponents.Blazor/Components/Subscription/Admin/SubscriptionAdminComponent.razor.cs",
            "await RaiseEntitlementsChangedAsync(EntitlementsChangedReasons.Manual);"
        },
        // Packs: the three actions do not share one reason.
        {
            "WildwoodComponents.Blazor/Components/Subscription/Admin/AddOnsPanel.razor.cs",
            "await FinishActionAsync(failure, \"Subscribing to add-on\", EntitlementsChangedReasons.AddOn);"
        },
        {
            "WildwoodComponents.Blazor/Components/Subscription/Admin/AddOnsPanel.razor.cs",
            "await FinishActionAsync(failure, \"Cancelling add-on\", EntitlementsChangedReasons.Cancel);"
        },
        {
            "WildwoodComponents.Blazor/Components/Subscription/Admin/AddOnsPanel.razor.cs",
            "await FinishActionAsync(failure, \"Reactivating add-on\", EntitlementsChangedReasons.Reactivate);"
        },
        // The legacy grid subscribes, cancels and buys packs on its own.
        {
            "WildwoodComponents.Blazor/Components/AppTier/AppTierComponent.razor.cs",
            "NotifyEntitlementsChanged(EntitlementsChangedReasons.TierChange);"
        },
        {
            "WildwoodComponents.Blazor/Components/AppTier/AppTierComponent.razor.cs",
            "NotifyEntitlementsChanged(EntitlementsChangedReasons.Cancel);"
        },
        {
            "WildwoodComponents.Blazor/Components/AppTier/AppTierComponent.razor.cs",
            "NotifyEntitlementsChanged(EntitlementsChangedReasons.AddOn);"
        },
        // A new account is entitled to whatever it just signed up for. "signup" is this flow's only.
        {
            "WildwoodComponents.Blazor/Components/Registration/SignupWithSubscriptionComponent.razor.cs",
            "EntitlementService.Invalidate(AppId, EntitlementsChangedReasons.Signup);"
        },
    };

    [Theory]
    [MemberData(nameof(BlazorEntitlementCallSites))]
    public void Each_Blazor_mutation_path_names_its_own_reason(string relativePath, string callSite)
    {
        Assert.Contains(callSite, ReadSource(relativePath));
    }

    /// <summary>
    /// The Razor analog: a DOM CustomEvent. Both the panel's own changed event and the dedicated
    /// entitlements event carry a reason from the same six-word vocabulary, and the constants are
    /// spelled the way the C# ones are.
    /// </summary>
    [Fact]
    public void Razor_subscription_admin_script_carries_the_six_reasons_and_the_entitlements_event()
    {
        var source = ReadSource("WildwoodComponents.Razor/wwwroot/js/subscription-admin.js");

        foreach (var reason in new[] { "signup", "tierChange", "addOn", "cancel", "reactivate", "manual" })
        {
            Assert.Contains($"'{reason}'", source);
        }

        Assert.Contains("detail: { action: action, appId: appId, reason: reason || null }", source);
        Assert.Contains("new CustomEvent('ww-entitlements-changed'", source);

        // Which reason goes with which mutation.
        Assert.Contains("dispatchChanged('cancelled', WW_REASON.Cancel);", source);
        Assert.Contains("dispatchChanged(ctx.isChange ? 'changed' : 'subscribed', WW_REASON.TierChange);", source);
        Assert.Contains("dispatchChanged('feature_toggled', WW_REASON.Manual);", source);
        Assert.Contains("dispatchChanged('override_removed', WW_REASON.Manual);", source);
        Assert.Contains("dispatchChanged('override_updated', WW_REASON.Manual);", source);
        Assert.Contains("'addon_subscribed', WW_REASON.AddOn,", source);
        Assert.Contains("'addon_cancelled', WW_REASON.Cancel,", source);
        Assert.Contains("'addon_reactivated', WW_REASON.Reactivate,", source);

        // Editing a usage limit moves a number, not what the plan includes: no reason, and so no
        // entitlements event either.
        Assert.Contains("dispatchChanged('limit_updated', null);", source);
        Assert.Contains("dispatchChanged('usage_reset', null);", source);
    }

    [Theory]
    [InlineData("apptier.js")]
    [InlineData("signup-subscription.js")]
    public void Other_Razor_scripts_raise_the_entitlements_event_too(string script)
    {
        var source = ReadSource($"WildwoodComponents.Razor/wwwroot/js/{script}");

        Assert.Contains("new CustomEvent('ww-entitlements-changed'", source);
        Assert.Contains("var WW_REASON = {", source);
    }

    /// <summary>
    /// The reason vocabulary itself: six words, spelled as JS spells them. A C# rename that did not
    /// change the constant's VALUE would go unnoticed everywhere else.
    /// </summary>
    [Fact]
    public void The_reason_vocabulary_is_the_six_JS_words()
    {
        Assert.Equal("signup", EntitlementsChangedReasons.Signup);
        Assert.Equal("tierChange", EntitlementsChangedReasons.TierChange);
        Assert.Equal("addOn", EntitlementsChangedReasons.AddOn);
        Assert.Equal("cancel", EntitlementsChangedReasons.Cancel);
        Assert.Equal("reactivate", EntitlementsChangedReasons.Reactivate);
        Assert.Equal("manual", EntitlementsChangedReasons.Manual);

        Assert.True(EntitlementsChangedReasons.IsKnown("reactivate"));
        Assert.False(EntitlementsChangedReasons.IsKnown("Reactivate")); // the wire is case-sensitive
        Assert.False(EntitlementsChangedReasons.IsKnown(""));
        Assert.False(EntitlementsChangedReasons.IsKnown(null));
    }

    #endregion

    #region Refusals

    /// <summary>
    /// The bug the JS <c>useSubscriptionAdmin</c> hook had: a mutation that answers <c>false</c>
    /// (or <c>success: false</c>) was treated as having happened, so a purchase that never
    /// occurred looked as if it had. Every success branch in the Blazor admin surface must have an
    /// else that reaches <c>HandleErrorAsync</c> — the base class's one route to the alert banner,
    /// the logger and the host's <c>OnError</c>.
    /// </summary>
    [Theory]
    [InlineData("WildwoodComponents.Blazor/Components/Subscription/Admin/SubscriptionAdminComponent.razor.cs")]
    [InlineData("WildwoodComponents.Blazor/Components/Subscription/Admin/OverridesPanel.razor.cs")]
    [InlineData("WildwoodComponents.Blazor/Components/Subscription/Admin/FeaturesPanel.razor.cs")]
    [InlineData("WildwoodComponents.Blazor/Components/Subscription/Admin/UsageLimitsPanel.razor.cs")]
    [InlineData("WildwoodComponents.Blazor/Components/AppTier/AppTierComponent.razor.cs")]
    public void A_refused_mutation_surfaces_an_error_rather_than_reading_as_success(string relativePath)
    {
        var lines = ReadSource(relativePath).Split('\n');
        var successBranch = new Regex(@"^\s*if \((success|result\.Success)\)\s*$", RegexOptions.CultureInvariant);
        var checkedBranches = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!successBranch.IsMatch(lines[i])) continue;
            checkedBranches++;

            // Walk to this branch's else at the same indentation, then look inside it.
            var indent = lines[i].Length - lines[i].TrimStart().Length;
            var elseLine = -1;
            for (var j = i + 1; j < lines.Length && j < i + 60; j++)
            {
                var trimmed = lines[j].Trim();
                if (trimmed == "else" && lines[j].TrimEnd().Length - lines[j].TrimEnd().TrimStart().Length == indent)
                {
                    elseLine = j;
                    break;
                }
            }

            Assert.True(elseLine > 0, $"{relativePath}:{i + 1} has no else for the refusal.");

            var handled = false;
            for (var j = elseLine; j < lines.Length && j < elseLine + 10; j++)
            {
                if (lines[j].Contains("HandleErrorAsync", StringComparison.Ordinal)) handled = true;
            }

            Assert.True(handled, $"{relativePath}:{elseLine + 1} refusal branch never reaches HandleErrorAsync.");
        }

        Assert.True(checkedBranches > 0, $"Expected at least one success branch in {relativePath}.");
    }

    #endregion
}
