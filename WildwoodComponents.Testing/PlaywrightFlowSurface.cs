using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WildwoodComponents.Testing;

// The one real implementation of the drivers' seam: Playwright.
//
// Everything here is a single locator call plus, where the driver treats "nothing matched" as an
// answer rather than a failure, the catch that turns it into one. That is the whole file on
// purpose: what a browser proves and a unit test cannot is exactly this layer, so keeping it to one
// line per operation leaves nothing in it worth a test and nothing above it that needs a browser.
//
// A surface is either a page or one locator. Both forms are kept rather than reducing the page to a
// root locator, because a locator scopes to DESCENDANTS: scoping the page to some root would
// silently stop matching that root itself, and the drivers would go on looking correct.

internal sealed class PlaywrightFlowSurface : IFlowSurface
{
    private readonly IPage? _page;
    private readonly ILocator? _scope;

    private PlaywrightFlowSurface(IPage? page, ILocator? scope)
    {
        _page = page;
        _scope = scope;
    }

    /// <summary>Drives a whole page.</summary>
    internal static PlaywrightFlowSurface ForPage(IPage page)
    {
        if (page is null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        return new PlaywrightFlowSurface(page, null);
    }

    /// <summary>Drives one element's subtree - the disclaimers panel, say.</summary>
    internal static PlaywrightFlowSurface ForScope(ILocator scope)
    {
        if (scope is null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        return new PlaywrightFlowSurface(null, scope);
    }

    public IFlowSurface Scope(string selector)
    {
        return new PlaywrightFlowSurface(null, Locate(selector));
    }

    public Task FillAsync(string selector, string value)
    {
        return Locate(selector).First.FillAsync(value);
    }

    public Task ClickAsync(string selector)
    {
        return Locate(selector).First.ClickAsync();
    }

    public async Task<int> CountAsync(string selector)
    {
        try
        {
            // Not .First: a count of the whole match is what tells the gate tick how many boxes it
            // is looking at.
            return await Locate(selector).CountAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (IsDriverAnswer(error))
        {
            return 0;
        }
    }

    public async Task<bool> IsVisibleAsync(string selector)
    {
        try
        {
            return await Locate(selector).First.IsVisibleAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (IsDriverAnswer(error))
        {
            return false;
        }
    }

    public async Task<bool> WaitForVisibleAsync(string selector, int timeoutMs)
    {
        return await WaitForStateAsync(Locate(selector).First, WaitForSelectorState.Visible, timeoutMs)
            .ConfigureAwait(false);
    }

    public async Task<bool> WaitForHiddenAsync(string selector, int timeoutMs)
    {
        return await WaitForStateAsync(Locate(selector).First, WaitForSelectorState.Hidden, timeoutMs)
            .ConfigureAwait(false);
    }

    public async Task<string?> TextAsync(string selector)
    {
        try
        {
            return await Locate(selector).First.TextContentAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (IsDriverAnswer(error))
        {
            return null;
        }
    }

    public async Task<bool> IsCheckedAsync(string selector, int index)
    {
        try
        {
            return await Locate(selector).Nth(index).IsCheckedAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (IsDriverAnswer(error))
        {
            // "Checked" is the answer that leaves the box alone. A box that cannot be read is not
            // evidence of anything on its own, and ticking it is the half that could go wrong.
            return true;
        }
    }

    public async Task<bool> CheckAsync(string selector, int index, int timeoutMs)
    {
        try
        {
            await Locate(selector).Nth(index)
                .CheckAsync(new LocatorCheckOptions { Timeout = timeoutMs })
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (IsDriverAnswer(error))
        {
            return false;
        }
    }

    public async Task<bool> WaitForTextAsync(string? text, Regex? pattern, int timeoutMs)
    {
        if (pattern is null && text is null)
        {
            throw new ArgumentNullException(nameof(text), "Name either text or a pattern to wait for.");
        }

        var located = pattern is not null
            ? (_scope is null ? _page!.GetByText(pattern) : _scope.GetByText(pattern))
            : (_scope is null ? _page!.GetByText(text!) : _scope.GetByText(text!));

        return await WaitForStateAsync(located.First, WaitForSelectorState.Visible, timeoutMs)
            .ConfigureAwait(false);
    }

    public Task DelayAsync(int ms)
    {
        return Task.Delay(ms);
    }

    public IDisposable WatchAcceptResponses(AcceptResponseWatcher watcher)
    {
        if (watcher is null)
        {
            throw new ArgumentNullException(nameof(watcher));
        }

        // The page, not the scope: a locator's responses are the page's responses, and the accept
        // loop is usually handed the disclaimers panel rather than the page itself.
        var page = _page ?? _scope!.Page;

        // One delegate instance, held by the subscription, because -= removes an equal delegate and
        // a second lambda would not be one.
        void Handler(object? sender, IResponse response) => watcher.Record(response.Url, response.Status);
        EventHandler<IResponse> handler = Handler;

        page.Response += handler;
        return new ResponseSubscription(page, handler);
    }

    private ILocator Locate(string selector)
    {
        return _scope is null ? _page!.Locator(selector) : _scope.Locator(selector);
    }

    private static async Task<bool> WaitForStateAsync(ILocator locator, WaitForSelectorState state, int timeoutMs)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions { State = state, Timeout = timeoutMs })
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (IsDriverAnswer(error))
        {
            return false;
        }
    }

    /// <summary>
    /// Whether this failure is the driver ANSWERING - nothing matched, or nothing matched in time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both types are caught because Playwright for .NET raises the two separately: its own
    /// <see cref="PlaywrightException"/> for a selector that matched nothing or an element that went
    /// away mid-read, and <see cref="System.TimeoutException"/> when a wait runs out - there is no
    /// <c>Microsoft.Playwright.TimeoutException</c> to cover both. Anything else is left to throw,
    /// so a browser that crashed is not reported as a component that rendered nothing.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than private so <see cref="WildwoodSignupSteps"/> reads the step
    /// through the same rule. It used to catch <see cref="PlaywrightException"/> alone, which let a
    /// locator timeout - the "the view never mounted" case its own remarks describe - escape as a
    /// raw <see cref="System.TimeoutException"/> and pre-empt the caller's budget with the timeout
    /// this package exists to replace. Two files disagreeing about which failures are ANSWERS is
    /// exactly the kind of thing that stays wrong, so there is one rule.
    /// </para>
    /// </remarks>
    internal static bool IsDriverAnswer(Exception error)
    {
        return error is PlaywrightException or System.TimeoutException;
    }

    private sealed class ResponseSubscription : IDisposable
    {
        private readonly IPage _page;
        private readonly EventHandler<IResponse> _handler;
        private bool _off;

        internal ResponseSubscription(IPage page, EventHandler<IResponse> handler)
        {
            _page = page;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_off) return;
            _off = true;
            _page.Response -= _handler;
        }
    }
}
