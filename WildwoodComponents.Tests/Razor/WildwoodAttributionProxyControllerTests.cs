using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Controllers;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The claim proxy's HTTP contract toward attribution.js: 401 for a signed-out session, 400 for a payload with no
/// touch, 502 for a transient failure (the engine keeps its touches), 200 with the server's answer otherwise.
/// </summary>
public class WildwoodAttributionProxyControllerTests
{
    private sealed class FakeAttributionService : IWildwoodAttributionService
    {
        public AttributionClaimResultModel? Result { get; set; }
        public bool Unauthorized { get; set; }
        public int Calls { get; private set; }

        public bool LastResponseUnauthorized => Unauthorized;

        public Task<AttributionClaimResultModel?> ClaimAsync(AttributionPayloadModel? payload, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private static WildwoodAttributionProxyController CreateController(FakeAttributionService service, string? accessToken = "jwt-1")
    {
        return new WildwoodAttributionProxyController(service, new FakeSessionManager(accessToken))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static AttributionPayloadModel Payload() => new()
    {
        VisitorKey = "visitor-key-0001",
        FirstTouch = new AttributionTouchModel { Source = "reddit" }
    };

    [Fact]
    public async Task A_signed_out_session_is_rejected_without_calling_the_api()
    {
        var service = new FakeAttributionService { Result = new AttributionClaimResultModel { Recorded = true } };

        var result = await CreateController(service, accessToken: null).Claim(Payload());

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task A_payload_without_a_touch_is_a_bad_request()
    {
        var service = new FakeAttributionService();

        var result = await CreateController(service).Claim(new AttributionPayloadModel { VisitorKey = "visitor-key-0001" });

        Assert.IsType<BadRequestResult>(result);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task The_server_answer_is_returned()
    {
        var answer = new AttributionClaimResultModel { Recorded = false, Reason = "AlreadyRecorded" };
        var service = new FakeAttributionService { Result = answer };

        var result = await CreateController(service).Claim(Payload());

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(answer, ok.Value);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task A_transient_failure_is_a_502_so_the_engine_keeps_its_touches()
    {
        var service = new FakeAttributionService { Result = null };

        var result = await CreateController(service).Claim(Payload());

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task A_dead_session_token_is_a_401_flagged_as_session_expiry()
    {
        var service = new FakeAttributionService { Result = null, Unauthorized = true };
        var controller = CreateController(service);

        var result = await controller.Claim(Payload());

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal("true", controller.Response.Headers["X-Session-Expired"].ToString());
    }
}
