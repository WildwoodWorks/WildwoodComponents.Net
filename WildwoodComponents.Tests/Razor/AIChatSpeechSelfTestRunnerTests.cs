using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// Runs the AI chat speech decisions' Node self-test, and fails this suite if it fails.
/// </summary>
/// <remarks>
/// <para>
/// Recorded voice input lives in the browser — <c>MediaRecorder</c>, <c>getUserMedia</c> and
/// <c>SpeechRecognition</c> have no C# counterpart — but the decisions it turns on are pure, and
/// those are the ones worth pinning: which mechanism a browser gets, which Web Speech failures
/// mean "never going to work here", which container to record, the 25 MB cap, and the media-type
/// table that has to stay equal to <c>SpeechAudioFormats</c>. <c>wwwroot/js/ai-chat.js</c>
/// therefore keeps them as pure functions behind a guarded <c>module.exports</c>, and
/// <c>Razor/js/ai-chat-speech.selftest.mjs</c> replays them under plain Node.
/// </para>
/// <para>
/// Requiring the script at all is part of the coverage: it must load with no DOM, which stays
/// true only while the auto-initialize at the bottom is guarded by <c>typeof document</c>.
/// </para>
/// </remarks>
public class AIChatSpeechSelfTestRunnerTests
{
    private static string SelfTestPath()
    {
        return Path.Combine(
            NodeSelfTest.RepoRoot(), "WildwoodComponents.Tests", "Razor", "js", "ai-chat-speech.selftest.mjs");
    }

    [Fact]
    public void The_self_test_and_the_script_it_covers_are_both_present()
    {
        Assert.True(File.Exists(SelfTestPath()), $"Expected {SelfTestPath()} to exist.");

        var script = Path.Combine(
            NodeSelfTest.RepoRoot(), "WildwoodComponents.Razor", "wwwroot", "js", "ai-chat.js");
        Assert.True(File.Exists(script), $"Expected {script} to exist.");
    }

    /// <summary>
    /// The precedence bug this stage fixed was <c>enableSTT &amp;&amp; A || B</c>, which ran the
    /// speech block with voice input DISABLED whenever <c>SpeechRecognition</c> existed. The gate
    /// is a single pure call now; this pins that the source never grows a second one.
    /// </summary>
    [Fact]
    public void The_speech_block_is_reached_through_one_gate()
    {
        var script = File.ReadAllText(Path.Combine(
            NodeSelfTest.RepoRoot(), "WildwoodComponents.Razor", "wwwroot", "js", "ai-chat.js"));

        Assert.Contains("if (speechEnabled(speechConfig)) {", script);
        // The shape of the old bug: the flag && a capability check, straight in the condition.
        Assert.DoesNotContain("if (enableSTT", script);
    }

    [NodeFact]
    public void The_speech_decisions_pass_their_self_test()
    {
        NodeSelfTest.Run(SelfTestPath(), "AI chat speech");
    }
}
