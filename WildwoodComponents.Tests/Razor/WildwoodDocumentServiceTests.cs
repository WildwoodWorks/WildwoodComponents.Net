using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

public class WildwoodDocumentServiceTests
{
    private const string DocJson =
        """{"id":"doc-1","fileName":"rfp.pdf","contentType":"application/pdf","sizeBytes":1024,"status":"uploaded","parsedCharacters":0,"createdAt":"2026-07-07T00:00:00Z"}""";

    private static (WildwoodDocumentService Service, FakeHttpMessageHandler Handler, FakeSessionManager Session) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var session = new FakeSessionManager();
        var service = new WildwoodDocumentService(
            handler.CreateClient("https://api.test/api/"),
            session,
            NullLogger<WildwoodDocumentService>.Instance);
        return (service, handler, session);
    }

    [Fact]
    public async Task ListAsync_CallsDocumentsEndpoint_WithAuthHeader_AndNoAppId()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("documents", $"[{DocJson}]");

        var documents = await service.ListAsync();

        var doc = Assert.Single(documents);
        Assert.Equal("doc-1", doc.Id);
        Assert.True(session.ApplyAuthorizationHeaderCalls > 0);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("api/documents", request.Url);
        Assert.DoesNotContain("requestedAppId", request.Url);
    }

    [Fact]
    public async Task ListAsync_ForwardsRequestedAppId()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("documents", "[]");

        await service.ListAsync("app-1");

        Assert.Contains("documents?requestedAppId=app-1", handler.Requests[0].Url);
    }

    [Fact]
    public async Task GetTextAsync_MapsConflict_ToTextLessResult()
    {
        var (service, handler, _) = CreateService();
        handler.When("documents/doc-1/text", HttpStatusCode.Conflict,
            """{"status":"parsing","error":"Text not available yet."}""");

        var result = await service.GetTextAsync("doc-1", "app-1");

        Assert.NotNull(result);
        Assert.Equal("doc-1", result!.Id);
        Assert.Equal("parsing", result.Status);
        Assert.Equal(0, result.Characters);
        Assert.Null(result.Text);
        Assert.Equal("Text not available yet.", result.Error);
        Assert.Contains("documents/doc-1/text?requestedAppId=app-1", handler.Requests[0].Url);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue_OnSuccess_AndForwardsAppId()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("documents/doc-1", """{"deleted":true}""");

        var deleted = await service.DeleteAsync("doc-1", "app-1");

        Assert.True(deleted);
        Assert.True(session.ApplyAuthorizationHeaderCalls > 0);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Contains("api/documents/doc-1?requestedAppId=app-1", request.Url);
    }

    [Fact]
    public async Task UploadAsync_PostsMultipart_WithFileField()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("documents", DocJson);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7"));
        var created = await service.UploadAsync(stream, "rfp.pdf", "application/pdf", "app-1");

        Assert.NotNull(created);
        Assert.Equal("doc-1", created!.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("api/documents?requestedAppId=app-1", request.Url);
        // The file must be a multipart form field named "file" carrying the filename.
        // Normalize quotes so the check holds whether the runtime emits name=file or name="file".
        var disposition = request.Body!.Replace("\"", string.Empty);
        Assert.Contains("name=file", disposition);
        Assert.Contains("filename=rfp.pdf", disposition);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_OnForbidden()
    {
        var (service, handler, _) = CreateService();
        handler.When("documents", HttpStatusCode.Forbidden, """{"error":"no access"}""");

        var documents = await service.ListAsync();

        Assert.Empty(documents);
    }

    [Fact]
    public async Task GetAsync_ReturnsDocument_AndForwardsAppId()
    {
        var (service, handler, session) = CreateService();
        handler.WhenOk("documents/doc-1", DocJson);

        var doc = await service.GetAsync("doc-1", "app-1");

        Assert.NotNull(doc);
        Assert.Equal("doc-1", doc!.Id);
        Assert.True(session.ApplyAuthorizationHeaderCalls > 0);
        Assert.Contains("api/documents/doc-1?requestedAppId=app-1", handler.Requests[0].Url);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsBytes_OnSuccess()
    {
        var (service, handler, _) = CreateService();
        handler.WhenOk("documents/doc-1/download", "%PDF-1.7");

        var bytes = await service.DownloadAsync("doc-1", "app-1");

        Assert.NotNull(bytes);
        Assert.Equal("%PDF-1.7", Encoding.UTF8.GetString(bytes!));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("api/documents/doc-1/download?requestedAppId=app-1", request.Url);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsNull_OnFailure()
    {
        var (service, handler, _) = CreateService();
        handler.When("documents/doc-1/download", HttpStatusCode.InternalServerError, "");

        Assert.Null(await service.DownloadAsync("doc-1", "app-1"));
    }
}
