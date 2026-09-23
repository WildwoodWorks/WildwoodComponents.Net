using Microsoft.JSInterop;

namespace WildwoodComponents.Tests.TestHelpers;

/// <summary>
/// An <see cref="IJSRuntime"/> that records every call and answers with whatever the test says.
/// </summary>
/// <remarks>
/// For the interop the components make directly on <c>window</c> — <c>wildwoodPayment.*</c> — as
/// opposed to the module imports <c>AttributionServiceTests</c>'s own fake covers. Disposal is the
/// main thing it is here for: a teardown that never reaches the browser is invisible without a
/// record of the calls that were made.
/// </remarks>
public class RecordingJsRuntime : IJSRuntime
{
    public record Call(string Identifier, object?[]? Args);

    /// <summary>Every call, in the order it was made.</summary>
    public List<Call> Calls { get; } = new();

    /// <summary>Thrown by every call when set — a disconnected circuit, say.</summary>
    public Exception? Throw { get; set; }

    /// <summary>What a call answers with, by identifier. Anything unanswered comes back default.</summary>
    public Func<string, object?>? Result { get; set; }

    /// <summary>Every call made to <paramref name="identifier"/>, in order.</summary>
    public List<Call> CallsTo(string identifier)
    {
        var matched = new List<Call>();
        foreach (var call in Calls)
        {
            if (string.Equals(call.Identifier, identifier, StringComparison.Ordinal)) matched.Add(call);
        }

        return matched;
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add(new Call(identifier, args));

        if (Throw is not null) throw Throw;

        var answer = Result?.Invoke(identifier);
        return new ValueTask<TValue>(answer is TValue typed ? typed : default!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);
}
