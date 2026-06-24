using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// Guards the cross-stack storage-key parity invariant: every current key carries the
/// <c>ww_</c> prefix and matches the literal names used by @wildwood/core and WildwoodCore (Swift).
/// </summary>
public class WildwoodStorageKeysTests
{
    [Theory]
    [InlineData("ww_accessToken")]
    [InlineData("ww_refreshToken")]
    [InlineData("ww_user")]
    [InlineData("ww_theme")]
    [InlineData("ww_draft_")]
    public void CurrentKeys_AllCarryTheWwPrefix(string key)
    {
        Assert.StartsWith("ww_", key);
    }

    [Fact]
    public void CurrentKeys_MatchTheCanonicalCrossStackNames()
    {
        Assert.Equal("ww_accessToken", WildwoodStorageKeys.AccessToken);
        Assert.Equal("ww_refreshToken", WildwoodStorageKeys.RefreshToken);
        Assert.Equal("ww_user", WildwoodStorageKeys.User);
        Assert.Equal("ww_theme", WildwoodStorageKeys.Theme);
        Assert.Equal("ww_draft_", WildwoodStorageKeys.DraftPrefix);
    }

    [Fact]
    public void Draft_ComposesThePrefixedThreadKey()
    {
        Assert.Equal("ww_draft_thread-123", WildwoodStorageKeys.Draft("thread-123"));
    }

    [Fact]
    public void LegacyKeys_AreUnprefixed_ForMigrationOnReadOnly()
    {
        // These intentionally lack the ww_ prefix; they exist only so an upgrade can migrate
        // old values on read. If any gained a ww_ prefix, migration-on-read would silently break.
        Assert.Equal("accessToken", WildwoodStorageKeys.Legacy.AccessToken);
        Assert.Equal("refreshToken", WildwoodStorageKeys.Legacy.RefreshToken);
        Assert.Equal("user", WildwoodStorageKeys.Legacy.User);
        Assert.Equal("wildwood-theme", WildwoodStorageKeys.Legacy.Theme);
        Assert.Equal("draft_", WildwoodStorageKeys.Legacy.DraftPrefix);

        Assert.DoesNotContain("ww_", WildwoodStorageKeys.Legacy.AccessToken);
        Assert.Equal("draft_thread-9", WildwoodStorageKeys.Legacy.Draft("thread-9"));
    }
}
