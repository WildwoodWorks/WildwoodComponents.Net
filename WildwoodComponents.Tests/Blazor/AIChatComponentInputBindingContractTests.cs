using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Components.AI;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Pins how <c>AIChatComponent</c> binds its message textarea. On Blazor Server, a textarea bound by
/// hand (<c>value="@x"</c> plus an <c>@oninput</c> handler that sets <c>x</c>) scrambles fast typing:
/// every keystroke's render carries a changed <c>value</c>, and the browser assigns that stale value
/// over whatever was typed while the event was in flight. <c>@bind</c> avoids it because the
/// compiler marks the event as updating <c>value</c>, and the renderer then syncs its tree to the
/// value the browser reported before diffing. That marker is what these tests look for.
/// </summary>
public class AIChatComponentInputBindingContractTests
{
    [Fact]
    public void ChatInput_OnInputHandler_UpdatesTheValueAttribute()
    {
        // BuildRenderTree on a bare instance only reads initialised fields and Settings defaults, so
        // no renderer, DI or lifecycle is needed to inspect the compiled markup.
        var component = new AIChatComponent();
        using var builder = new RenderTreeBuilder();
        var buildRenderTree = typeof(AIChatComponent).GetMethod(
            "BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!;
        buildRenderTree.Invoke(component, new object[] { builder });

        var attributes = AttributesOfElement(builder.GetFrames(), "textarea", "ai-chat-input");

        var onInput = Assert.Single(attributes, a => a.AttributeName == "oninput");
        Assert.True(
            onInput.AttributeEventUpdatesAttributeName == "value",
            "The ai-chat-input textarea must use @bind=\"CurrentMessage\" @bind:event=\"oninput\". " +
            "A hand-written value= + @oninput pair overwrites fast typing on Blazor Server.");
        Assert.Contains(attributes, a => a.AttributeName == "value");
    }

    /// <summary>
    /// Proves the mechanism the contract test relies on, so it can never quietly become vacuous: with
    /// the updates-value marker, an input event produces no <c>value</c> write back to the browser;
    /// without it, it does.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InputEvent_WritesValueBackToTheBrowser_OnlyWithoutTheUpdatesMarker(
        bool markValueAsBound, bool expectValueWrite)
    {
        using var renderer = new RecordingRenderer();
        var component = new TextInput { MarkValueAsBound = markValueAsBound };

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var componentId = renderer.Attach(component);
            await renderer.RenderRootAsync(componentId);

            var onInput = Assert.Single(
                AttributesOfElement(renderer.CurrentFrames(componentId), "textarea", cssClass: null),
                a => a.AttributeName == "oninput");

            renderer.AttributeWrites.Clear();

            // Exactly what the browser sends for a keystroke: the handler id, the field's current DOM
            // value, and the change args.
            await renderer.DispatchEventAsync(
                onInput.AttributeEventHandlerId,
                new EventFieldInfo { ComponentId = componentId, FieldValue = "abc" },
                new ChangeEventArgs { Value = "abc" });
        });

        Assert.Equal("abc", component.Text);
        Assert.Equal(expectValueWrite, renderer.AttributeWrites.Contains("value"));
    }

    private static List<RenderTreeFrame> AttributesOfElement(
        ArrayRange<RenderTreeFrame> frames, string elementName, string? cssClass)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != elementName)
            {
                continue;
            }

            // An element's attribute frames immediately follow it.
            var attributes = new List<RenderTreeFrame>();
            for (var j = i + 1; j < frames.Count && frames.Array[j].FrameType == RenderTreeFrameType.Attribute; j++)
            {
                attributes.Add(frames.Array[j]);
            }

            if (cssClass is null || attributes.Any(a =>
                    a.AttributeName == "class" && (a.AttributeValue as string ?? string.Empty).Contains(cssClass)))
            {
                return attributes;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"No <{elementName}{(cssClass is null ? "" : $" class~=\"{cssClass}\"")}> in the render tree.");
    }

    /// <summary>A textarea bound the way the Razor compiler lowers <c>@bind:event="oninput"</c>.</summary>
    private sealed class TextInput : ComponentBase
    {
        public bool MarkValueAsBound { get; init; }

        public string Text { get; private set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "textarea");
            builder.AddAttribute(1, "value", BindConverter.FormatValue(Text));
            builder.AddAttribute(2, "oninput", EventCallback.Factory.CreateBinder(this, value => Text = value ?? string.Empty, Text));
            if (MarkValueAsBound)
            {
                builder.SetUpdatesAttributeName("value");
            }
            builder.CloseElement();
        }
    }

    /// <summary>Records the name of every attribute a render batch would set in the browser.</summary>
    private sealed class RecordingRenderer : Renderer
    {
        public RecordingRenderer() : base(new EmptyServiceProvider(), NullLoggerFactory.Instance)
        {
        }

        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public List<string> AttributeWrites { get; } = new();

        public int Attach(IComponent component) => AssignRootComponentId(component);

        public Task RenderRootAsync(int componentId) => RenderRootComponentAsync(componentId);

        public ArrayRange<RenderTreeFrame> CurrentFrames(int componentId) => GetCurrentRenderTreeFrames(componentId);

        protected override void HandleException(Exception exception) =>
            ExceptionDispatchInfo.Capture(exception).Throw();

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
        {
            for (var d = 0; d < renderBatch.UpdatedComponents.Count; d++)
            {
                var diff = renderBatch.UpdatedComponents.Array[d];
                for (var e = 0; e < diff.Edits.Count; e++)
                {
                    var edit = diff.Edits.Array[diff.Edits.Offset + e];
                    if (edit.Type == RenderTreeEditType.SetAttribute)
                    {
                        AttributeWrites.Add(renderBatch.ReferenceFrames.Array[edit.ReferenceFrameIndex].AttributeName);
                    }
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
