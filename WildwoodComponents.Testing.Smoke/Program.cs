using System.Diagnostics;
using Microsoft.Playwright;

namespace WildwoodComponents.Testing.Smoke;

// The smoke run: a browser, a loopback fixture server, and the shipped helpers driving it.
//
// A console runner rather than a test project, deliberately. It needs a browser, which a build
// machine does not have, and `dotnet test` must stay green without one - and the alternative,
// an xUnit test that skips itself when no browser is installed, would report coverage that never
// actually runs anywhere. It is run by hand; README.md says how, and says plainly what it proves
// and what it does not.
//
// What keeps it honest is that CI COMPILES it - a step added for exactly this, since CI builds the
// two test projects rather than the solution and neither may reference this one. Being in the
// solution is not by itself protection from rot; being in somebody's build graph is.
//
// Exit codes: 0 every check passed, 1 a check failed, 2 the run could not start (no browser).

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = RunOptions.Parse(args);
        if (options is null) return 2;

        var checks = new List<ContractCheck>();
        foreach (var check in ContractChecks.All)
        {
            if (options.Filter is null
                || check.Name.Contains(options.Filter, StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(check);
            }
        }

        if (checks.Count == 0)
        {
            Console.Error.WriteLine("No check matched --filter \"" + options.Filter + "\".");
            return 2;
        }

        await using var server = await FixtureServer.StartAsync();
        Console.WriteLine("Fixtures on " + server.BaseUrl + " (loopback only, no network, no credentials)");

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        IBrowser browser;
        try
        {
            browser = await BrowserType(playwright, options.Browser)
                .LaunchAsync(new BrowserTypeLaunchOptions { Headless = !options.Headed });
        }
        catch (PlaywrightException error)
        {
            // The one failure that is not a result: the browser is not installed. Say what to run
            // rather than reporting it as a red check, because nothing was actually tested.
            Console.Error.WriteLine("Could not launch " + options.Browser + ": " + error.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Install the browsers once, from this project's build output:");
            Console.Error.WriteLine("  pwsh bin/Debug/net10.0/playwright.ps1 install " + options.Browser);
            return 2;
        }

        var failures = 0;
        var elapsed = Stopwatch.StartNew();

        try
        {
            foreach (var check in checks)
            {
                // A context per check, so a page left mid-flow cannot reach the next one.
                await using var context = await browser.NewContextAsync();
                var page = await context.NewPageAsync();

                var checkTime = Stopwatch.StartNew();

                try
                {
                    await check.RunAsync(new CheckContext
                    {
                        Page = page,
                        BaseUrl = server.BaseUrl,
                        Server = server
                    });

                    Console.WriteLine("  PASS  " + check.Name + "  (" + checkTime.ElapsedMilliseconds + "ms)");
                }
                catch (Exception error)
                {
                    failures++;
                    Console.WriteLine("  FAIL  " + check.Name + "  (" + checkTime.ElapsedMilliseconds + "ms)");
                    Console.WriteLine("        " + error.Message.Replace("\n", "\n        "));
                }
            }
        }
        finally
        {
            await browser.CloseAsync();
        }

        Console.WriteLine();
        Console.WriteLine(
            (checks.Count - failures) + "/" + checks.Count + " checks passed in "
            + elapsed.ElapsedMilliseconds + "ms on " + options.Browser + ".");

        return failures == 0 ? 0 : 1;
    }

    private static IBrowserType BrowserType(IPlaywright playwright, string name)
    {
        return name switch
        {
            "firefox" => playwright.Firefox,
            "webkit" => playwright.Webkit,
            _ => playwright.Chromium
        };
    }

    /// <summary>What the command line can change: the browser, whether it is visible, and which checks run.</summary>
    private sealed class RunOptions
    {
        internal string Browser { get; private init; } = "chromium";

        internal bool Headed { get; private init; }

        internal string? Filter { get; private init; }

        /// <summary>Parsed options, or null when the arguments were not usable and help was printed.</summary>
        internal static RunOptions? Parse(string[] args)
        {
            var browser = "chromium";
            var headed = false;
            string? filter = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--headed":
                        headed = true;
                        break;
                    case "--browser" when i + 1 < args.Length:
                        browser = args[++i].ToLowerInvariant();
                        break;
                    case "--filter" when i + 1 < args.Length:
                        filter = args[++i];
                        break;
                    default:
                        Console.Error.WriteLine("Unrecognised argument: " + args[i]);
                        Usage();
                        return null;
                }
            }

            if (browser is not ("chromium" or "firefox" or "webkit"))
            {
                Console.Error.WriteLine("Unknown browser: " + browser);
                Usage();
                return null;
            }

            return new RunOptions { Browser = browser, Headed = headed, Filter = filter };
        }

        private static void Usage()
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage: dotnet run --project WildwoodComponents.Testing.Smoke [options]");
            Console.Error.WriteLine("  --browser <chromium|firefox|webkit>  which browser to drive (default chromium)");
            Console.Error.WriteLine("  --headed                             show the browser");
            Console.Error.WriteLine("  --filter <text>                      run only checks whose name contains <text>");
        }
    }
}
