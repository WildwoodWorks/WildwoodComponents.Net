namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Whether the visitor is signing up freely or redeeming an invite.
/// </summary>
public enum SignupTokenMode
{
    /// <summary>Follow the app's configuration.</summary>
    Auto = 0,

    /// <summary>
    /// Invite redemption: the visitor arrived with a token, so the token step is the only way in
    /// regardless of what the configuration says.
    /// </summary>
    Required = 1
}

/// <summary>Where a resolved registration mode came from.</summary>
public enum SignupRegistrationModeSource
{
    /// <summary>Decided by the app's live settings.</summary>
    Config = 0,

    /// <summary>
    /// The settings could not be read, so open sign-up plus the optional token entry.
    /// </summary>
    Fallback = 1,

    /// <summary>The caller asked for invite redemption, which overrides the settings.</summary>
    TokenMode = 2
}

/// <summary>
/// The two flags the decision reads, mapped out of whichever authentication configuration the
/// caller holds — Blazor's <c>AuthenticationConfiguration</c> or Razor's <c>AuthConfigResponse</c>.
/// JS gets this structurally from
/// <c>Pick&lt;AuthenticationConfiguration, 'allowOpenRegistration' | 'allowTokenRegistration'&gt;</c>.
/// </summary>
public class SignupRegistrationSettings
{
    /// <summary>Anyone may register without a token.</summary>
    public bool AllowOpenRegistration { get; set; }

    /// <summary>A registration token may be redeemed to create an account.</summary>
    public bool AllowTokenRegistration { get; set; }
}

/// <summary>
/// How a signup screen may offer registration, read from the app's live Wildwood authentication
/// settings so the screen offers exactly what the server will accept. Ported from
/// packages/wildwood-react-shared/src/authentication/registrationMode.ts.
/// </summary>
/// <remarks>
/// <para>
///   open + token registration -&gt; open sign-up, plus the optional "Have a registration token?" card<br/>
///   open registration only    -&gt; open sign-up, no token card<br/>
///   token registration only   -&gt; the token step is required<br/>
///   neither                   -&gt; sign-up is closed
/// </para>
/// <para>
/// When the settings cannot be read (network error, older API), it falls back to open sign-up with
/// the optional token card. The server still enforces its own settings, so the fallback can only
/// offer a path that is refused with a clear message, never grant one the server would not.
/// </para>
/// <para>
/// Not to be confused with the Blazor <c>RegistrationAccess</c> gate, which decides whether the
/// login component shows a sign-up affordance. This one decides how the signup screen itself works.
/// </para>
/// </remarks>
public static class SignupRegistrationMode
{
    /// <summary>
    /// The registration paths a signup screen may offer, and where the answer came from.
    /// </summary>
    public readonly struct Result : IEquatable<Result>
    {
        public Result(
            bool closed,
            bool requireToken,
            bool allowOpenRegistration,
            bool showOptionalTokenEntry,
            SignupRegistrationModeSource source)
        {
            Closed = closed;
            RequireToken = requireToken;
            AllowOpenRegistration = allowOpenRegistration;
            ShowOptionalTokenEntry = showOptionalTokenEntry;
            Source = source;
        }

        /// <summary>No registration path is open: render the "sign-up is closed" panel.</summary>
        public bool Closed { get; }

        /// <summary>A registration token must be supplied before an account can be created.</summary>
        public bool RequireToken { get; }

        /// <summary>Anyone may sign up without a token.</summary>
        public bool AllowOpenRegistration { get; }

        /// <summary>
        /// Offer the optional "Have a registration token?" entry alongside open sign-up.
        /// </summary>
        public bool ShowOptionalTokenEntry { get; }

        /// <summary>Which rule produced this answer.</summary>
        public SignupRegistrationModeSource Source { get; }

        public bool Equals(Result other)
        {
            return Closed == other.Closed
                && RequireToken == other.RequireToken
                && AllowOpenRegistration == other.AllowOpenRegistration
                && ShowOptionalTokenEntry == other.ShowOptionalTokenEntry
                && Source == other.Source;
        }

        public override bool Equals(object? obj)
        {
            return obj is Result other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = Closed ? 1 : 0;
            hash = (hash * 31) + (RequireToken ? 1 : 0);
            hash = (hash * 31) + (AllowOpenRegistration ? 1 : 0);
            hash = (hash * 31) + (ShowOptionalTokenEntry ? 1 : 0);
            hash = (hash * 31) + (int)Source;
            return hash;
        }

        public override string ToString()
        {
            return "Closed=" + Closed
                + ", RequireToken=" + RequireToken
                + ", AllowOpenRegistration=" + AllowOpenRegistration
                + ", ShowOptionalTokenEntry=" + ShowOptionalTokenEntry
                + ", Source=" + Source;
        }

        public static bool operator ==(Result left, Result right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Result left, Result right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Resolves how a signup screen should offer registration.
    /// </summary>
    /// <param name="config">The app's authentication settings, or null while they are unknown.</param>
    /// <param name="tokenMode">
    /// <see cref="SignupTokenMode.Required"/> forces the token path — the visitor is redeeming an
    /// invite, and the server validates the token itself, so a closed configuration does not block it.
    /// </param>
    public static Result Resolve(
        SignupRegistrationSettings? config,
        SignupTokenMode tokenMode = SignupTokenMode.Auto)
    {
        // Invite redemption wins over everything: the link carries a token the server will
        // validate, and an app that has turned open registration off still honours its own
        // invitations.
        if (tokenMode == SignupTokenMode.Required)
        {
            return new Result(
                closed: false,
                requireToken: true,
                allowOpenRegistration: false,
                showOptionalTokenEntry: false,
                source: SignupRegistrationModeSource.TokenMode);
        }

        var source = config is not null
            ? SignupRegistrationModeSource.Config
            : SignupRegistrationModeSource.Fallback;
        var open = config is null || config.AllowOpenRegistration;
        var token = config is null || config.AllowTokenRegistration;

        if (open)
        {
            return new Result(
                closed: false,
                requireToken: false,
                allowOpenRegistration: true,
                showOptionalTokenEntry: token,
                source: source);
        }

        if (token)
        {
            return new Result(
                closed: false,
                requireToken: true,
                allowOpenRegistration: false,
                showOptionalTokenEntry: false,
                source: source);
        }

        return new Result(
            closed: true,
            requireToken: true,
            allowOpenRegistration: false,
            showOptionalTokenEntry: false,
            source: source);
    }
}
