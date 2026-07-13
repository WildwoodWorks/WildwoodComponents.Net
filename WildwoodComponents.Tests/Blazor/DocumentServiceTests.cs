using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class DocumentServiceTests
{
    private const string DocJson =
        """{"id":"doc-1","fileName":"rfp.pdf","contentType":"application/pdf","sizeBytes":1024,"status":"uploaded","parsedCharacters":0,"createdAt":"2026-07-07T00:00:00Z"}""";

    private static (DocumentService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new DocumentService(
            handler.CreateClient("https://api.test/"),
            NullLogger<DocumentService>.Instance);
        service.SetApiBaseUrl("https://api.test/api");
        service.SetAppId("app-1");
        service.SetAuthToken("jwt-1");
        return (service, handler);
    }

    [Fact]
    public async Task ListAsync_CallsDocumentsEndpoint_WithRequestedAppId_AndDeserializes()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("documents", $"[{DocJson}]");

        var documents = await service.ListAsync();

        var doc = Assert.Single(documents);
        Assert.Equal("doc-1", doc.Id);
        Assert.Equal("rfp.pdf", doc.FileName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("https://api.test/api/documents?requestedAppId=app-1", request.Url);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_OnNonSuccess()
    {
        var (service, handler) = CreateService();
        handler.When("documents", HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        var documents = await service.ListAsync();

        Assert.Empty(documents);
    }

    [Fact]
    public async Task GetTextAsync_MapsConflict_ToTextLessResult()
    {
        var (service, handler) = CreateService();
        handler.When("documents/doc-1/text", HttpStatusCode.Conflict,
            """{"status":"parsing","error":"Text not available yet."}""");

        var result = await service.GetTextAsync("doc-1");

        Assert.NotNull(result);
        Assert.Equal("doc-1", result!.Id);
        Assert.Equal("parsing", result.Status);
        Assert.Equal(0, result.Characters);
        Assert.Null(result.Text);
        Assert.Equal("Text not available yet.", result.Error);
    }

    [Fact]
    public async Task GetTextAsync_ReturnsParsedText_WhenAvailable()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("documents/doc-1/text", """{"id":"doc-1","status":"parsed","characters":5,"text":"hello"}""");

        var result = await service.GetTextAsync("doc-1");

        Assert.NotNull(result);
        Assert.Equal("parsed", result!.Status);
        Assert.Equal("hello", result.Text);
        Assert.Equal(5, result.Characters);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue_OnSuccess_AndUsesDelete()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("documents/doc-1", """{"deleted":true}""");

        var deleted = await service.DeleteAsync("doc-1");

        Assert.True(deleted);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Contains("https://api.test/api/documents/doc-1?requestedAppId=app-1", request.Url);
    }

    [Fact]
    public async Task UploadAsync_PostsMultipart_WithFileField()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("documents", DocJson);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7"));
        var created = await service.UploadAsync(stream, "rfp.pdf", "application/pdf");

        Assert.NotNull(created);
        Assert.Equal("doc-1", created!.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("https://api.test/api/documents?requestedAppId=app-1", request.Url);
        // The file must be a multipart form field named "file" carrying the filename.
        // Normalize quotes so the check holds whether the runtime emits name=file or name="file".
        var disposition = request.Body!.Replace("\"", string.Empty);
        Assert.Contains("name=file", disposition);
        Assert.Contains("filename=rfp.pdf", disposition);
    }

    [Fact]
    public async Task UploadAsync_ReturnsNull_OnFailure_WithoutThrowing()
    {
        var (service, handler) = CreateService();
        handler.When("documents", HttpStatusCode.BadRequest, """{"error":"Unsupported document type."}""");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        var created = await service.UploadAsync(stream, "x.exe");

        Assert.Null(created);
    }

    [Fact]
    public async Task Requests_CarryBearerAuthorizationHeader()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("documents", $"[{DocJson}]");

        await service.ListAsync();

        // SetAuthToken must attach the session JWT to every outgoing request.
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer jwt-1", request.Authorization);
    }

    [Fact]
    public async Task AuthenticationFailed_FiresOnce_On401()
    {
        var (service, handler) = CreateService();
        handler.When("documents", HttpStatusCode.Unauthorized, "");
        var fired = 0;
        service.AuthenticationFailed += (_, _) => fired++;

        var first = await service.ListAsync();
        var second = await service.ListAsync();

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Equal(1, fired); // one-shot per token
    }

    [Fact]
    public async Task AuthenticationFailed_NeverFires_On403()
    {
        var (service, handler) = CreateService();
        handler.When("documents", HttpStatusCode.Forbidden, "");
        var fired = 0;
        service.AuthenticationFailed += (_, _) => fired++;

        var documents = await service.ListAsync();

        Assert.Empty(documents);
        Assert.Equal(0, fired); // 403 = tier lacks DOCUMENTS, not a session expiry
    }
}
