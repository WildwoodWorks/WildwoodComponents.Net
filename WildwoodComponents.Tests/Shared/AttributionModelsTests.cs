using System.Text.Json;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The attribution models must read the camelCase JSON the attribution engine and WildwoodAPI produce, and
/// write it back in the shape the registration endpoints bind.
/// </summary>
public class AttributionModelsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Payload_RoundTripsTheEngineShape()
    {
        const string json = """
            {"version":1,"visitorKey":"visitor-key-0001","firstTouch":null,
             "lastTouch":{"source":"reddit","medium":"paid","campaign":"govcon-test-sep26","term":null,"content":"ad1",
               "clickIdName":"rdt_cid","clickIdValue":"abc_123","referrerHost":null,"landingHost":"cairnfed.ai",
               "landingPath":"/","extraParams":{"sub_id":"42"},"occurredAt":"2026-09-13T12:00:00.000Z"},
             "platform":"web","sdk":"dotnet"}
            """;

        var payload = JsonSerializer.Deserialize<AttributionPayloadModel>(json, Web);

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Version);
        Assert.Equal("visitor-key-0001", payload.VisitorKey);
        Assert.Null(payload.FirstTouch);
        Assert.Equal("reddit", payload.LastTouch?.Source);
        Assert.Equal("govcon-test-sep26", payload.LastTouch?.Campaign);
        Assert.Equal("42", payload.LastTouch?.ExtraParams?["sub_id"]);

        var written = JsonSerializer.Serialize(payload, Web);
        Assert.Contains("\"lastTouch\":{\"source\":\"reddit\"", written);
        Assert.Contains("\"occurredAt\":\"2026-09-13T12:00:00.000Z\"", written);
        Assert.Contains("\"sdk\":\"dotnet\"", written);
    }

    [Fact]
    public void Payload_DefaultsIdentifyTheDotnetSdk()
    {
        var payload = new AttributionPayloadModel();

        Assert.Equal(1, payload.Version);
        Assert.Equal("dotnet", payload.Sdk);
        Assert.Equal("web", payload.Platform);
    }

    [Fact]
    public void Config_ReadsThePublicConfigResponse()
    {
        const string json = """
            {"appId":"app-1","isEnabled":true,"captureFirstTouch":true,"captureLastTouch":false,
             "attributionWindowDays":14,"persistenceConsentCategory":"Analytics","captureClickIds":true,
             "captureReferrer":false,"extraAllowedParamNames":["sub_id"],"beaconEnabled":true}
            """;

        var config = JsonSerializer.Deserialize<AttributionConfigModel>(json, Web);

        Assert.NotNull(config);
        Assert.True(config!.IsEnabled);
        Assert.False(config.CaptureLastTouch);
        Assert.Equal(14, config.AttributionWindowDays);
        Assert.Equal("Analytics", config.PersistenceConsentCategory);
        Assert.False(config.CaptureReferrer);
        Assert.Equal(new[] { "sub_id" }, config.ExtraAllowedParamNames);
        Assert.True(config.BeaconEnabled);
        Assert.False(config.FunnelTrackingEnabled);
        Assert.Empty(config.CustomEventNames);
    }

    [Fact]
    public void Config_ReadsTheFunnelFields()
    {
        const string json = """
            {"appId":"app-1","isEnabled":true,"funnelTrackingEnabled":true,"trackScrollDepth":true,
             "trackEngagement":true,"autoTrackCtaClicks":true,"trackSignupSteps":true,
             "customEventNames":["demo_booked"],"sessionStoragePersistenceBeforeConsent":true}
            """;

        var config = JsonSerializer.Deserialize<AttributionConfigModel>(json, Web)!;

        Assert.True(config.FunnelTrackingEnabled);
        Assert.True(config.TrackScrollDepth);
        Assert.True(config.TrackEngagement);
        Assert.True(config.AutoTrackCtaClicks);
        Assert.True(config.TrackSignupSteps);
        Assert.Equal(new[] { "demo_booked" }, config.CustomEventNames);
        Assert.True(config.SessionStoragePersistenceBeforeConsent);
    }

    [Fact]
    public void Payload_ReadsAndWritesTheFunnelSession()
    {
        const string json = """
            {"version":1,"visitorKey":"visitor-key-0001","firstTouch":null,"lastTouch":null,"platform":"web",
             "sdk":"dotnet","sessionKey":"session-key-0001","deviceClass":"tablet","sessionCount":3}
            """;

        var payload = JsonSerializer.Deserialize<AttributionPayloadModel>(json, Web)!;

        Assert.Equal("session-key-0001", payload.SessionKey);
        Assert.Equal("tablet", payload.DeviceClass);
        Assert.Equal(3, payload.SessionCount);

        // A claim carries them through, so a provider signup joins its funnel too.
        var claim = AttributionClaimRequestModel.From("app-1", payload);
        Assert.Equal("session-key-0001", claim.SessionKey);
        Assert.Equal("tablet", claim.DeviceClass);
        Assert.Equal(3, claim.SessionCount);

        var written = JsonSerializer.Serialize(claim, Web);
        Assert.Contains("\"sessionKey\":\"session-key-0001\"", written);
        Assert.Contains("\"deviceClass\":\"tablet\"", written);
        Assert.Contains("\"sessionCount\":3", written);
    }

    [Fact]
    public void EventsRequest_WritesTheEndpointShape()
    {
        var body = new AttributionEventsRequestModel
        {
            AppId = "app-1",
            VisitorKey = "visitor-key-0001",
            SessionKey = "session-key-0001",
            IsReturning = true,
            DeviceClass = "mobile",
            Touch = null,
            Events = { new AttributionFunnelEventModel { Name = "cta_click", Label = "hero", Path = "/" } }
        };

        var written = JsonSerializer.Serialize(body, Web);

        Assert.Contains("\"appId\":\"app-1\"", written);
        Assert.Contains("\"sessionKey\":\"session-key-0001\"", written);
        Assert.Contains("\"isReturning\":true", written);
        Assert.Contains("\"deviceClass\":\"mobile\"", written);
        Assert.Contains("\"platform\":\"web\"", written);
        Assert.Contains("\"touch\":null", written);
        Assert.Contains("\"events\":[{\"name\":\"cta_click\",\"label\":\"hero\"", written);
    }
}
