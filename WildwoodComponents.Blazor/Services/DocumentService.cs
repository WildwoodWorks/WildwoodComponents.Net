using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Client for the tenant document endpoints (api/documents): upload, list, metadata,
    /// download, extracted text, and delete. Documents are tenant-scoped server-side via the
    /// caller's company_client_id claim. Same transport idiom as <see cref="AIFlowService"/>:
    /// absolute URLs built from an injected api base, an X-API-Key/Bearer header pair, per-call
    /// ?requestedAppId scoping, and a one-shot 401 signal (re-armed when the token changes).
    /// </summary>
    public interface IDocumentService
    {
        event EventHandler? AuthenticationFailed;

        void SetAuthToken(string token);
        void SetApiBaseUrl(string apiBaseUrl);
        void SetAppId(string? appId);

        /// <summary>All documents for the current tenant/app, or an empty list on failure.</summary>
        Task<List<AppDocumentModel>> ListAsync();

        /// <summary>
        /// Uploads one file as multipart form data (field name "file"). Returns the created
        /// document (status "uploaded"; text extraction runs server-side) or null on failure.
        /// </summary>
        Task<AppDocumentModel?> UploadAsync(Stream fileStream, string fileName, string? contentType = null);

        /// <summary>Document metadata by id, or null when unavailable.</summary>
        Task<AppDocumentModel?> GetAsync(string documentId);

        /// <summary>
        /// Extracted text. While parsing is pending/failed the server responds 409, mapped here to a
        /// result with Text=null plus the status/error so callers can poll. Null on other failures.
        /// </summary>
        Task<AppDocumentTextResult?> GetTextAsync(string documentId);

        /// <summary>Original file bytes, or null when unavailable.</summary>
        Task<byte[]?> DownloadAsync(string documentId);

        /// <summary>Deletes a document; true on success.</summary>
        Task<bool> DeleteAsync(string documentId);
    }

    public class DocumentService : IDocumentService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly ILogger<DocumentService> _logger;

        private string _apiBaseUrl = string.Empty;
        private string? _appId;
        private bool _authFailureFired;

        public event EventHandler? AuthenticationFailed;

        public DocumentService(HttpClient httpClient, ILogger<DocumentService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void SetAuthToken(string token)
        {
            _authFailureFired = false; // re-arm the one-shot 401 signal for the new token
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token ?? string.Empty);
        }

        public void SetApiBaseUrl(string apiBaseUrl) => _apiBaseUrl = apiBaseUrl?.TrimEnd('/') ?? string.Empty;

        public void SetAppId(string? appId) => _appId = appId;

        public async Task<List<AppDocumentModel>> ListAsync()
        {
            try
            {
                using var request = GetRequest($"{_apiBaseUrl}/documents{AppQuery()}", acceptJson: true);
                using var response = await _httpClient.SendAsync(request);
                if (!await EnsureAuthorizedAsync(response) || !response.IsSuccessStatusCode) return new();
                return await response.Content.ReadFromJsonAsync<List<AppDocumentModel>>(Json) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list documents");
                return new();
            }
        }

        public async Task<AppDocumentModel?> UploadAsync(Stream fileStream, string fileName, string? contentType = null)
        {
            try
            {
                // No manual Content-Type: MultipartFormDataContent sets multipart/form-data with the
                // boundary itself. The file goes in as a form field named "file" with its filename.
                using var form = new MultipartFormDataContent();
                var filePart = new StreamContent(fileStream);
                if (!string.IsNullOrEmpty(contentType))
                    filePart.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                form.Add(filePart, "file", string.IsNullOrEmpty(fileName) ? "document" : fileName);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}/documents{AppQuery()}")
                {
                    Content = form
                };
                using var response = await _httpClient.SendAsync(request);

                // NOTE: JS DocumentService.upload throws on failure; the .NET stack returns null + logs
                // for consistency with AIFlow/NotificationInbox (callers here don't expect an exception).
                if (!await EnsureAuthorizedAsync(response))
                {
                    _logger.LogWarning("Document upload denied: {StatusCode}", response.StatusCode);
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Document upload failed: {StatusCode} - {Detail}",
                        response.StatusCode, await ErrorDetailAsync(response));
                    return null;
                }
                return await response.Content.ReadFromJsonAsync<AppDocumentModel>(Json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload document {FileName}", fileName);
                return null;
            }
        }

        public async Task<AppDocumentModel?> GetAsync(string documentId)
        {
            try
            {
                using var request = GetRequest(
                    $"{_apiBaseUrl}/documents/{Uri.EscapeDataString(documentId)}{AppQuery()}", acceptJson: true);
                using var response = await _httpClient.SendAsync(request);
                if (!await EnsureAuthorizedAsync(response) || !response.IsSuccessStatusCode) return null;
                return await response.Content.ReadFromJsonAsync<AppDocumentModel>(Json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load document {DocumentId}", documentId);
                return null;
            }
        }

        public async Task<AppDocumentTextResult?> GetTextAsync(string documentId)
        {
            try
            {
                using var request = GetRequest(
                    $"{_apiBaseUrl}/documents/{Uri.EscapeDataString(documentId)}/text{AppQuery()}", acceptJson: true);
                using var response = await _httpClient.SendAsync(request);
                if (!await EnsureAuthorizedAsync(response)) return null;
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    // 409 = not parsed yet (or parse failed). Map to a text-less, poll-friendly result.
                    TextConflictBody? body = null;
                    try { body = await response.Content.ReadFromJsonAsync<TextConflictBody>(Json); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Malformed 409 text body for {DocumentId}", documentId); }
                    return new AppDocumentTextResult
                    {
                        Id = documentId,
                        Status = body?.Status ?? "parsing",
                        Characters = 0,
                        Text = null,
                        Error = body?.Error
                    };
                }
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadFromJsonAsync<AppDocumentTextResult>(Json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load document text {DocumentId}", documentId);
                return null;
            }
        }

        public async Task<byte[]?> DownloadAsync(string documentId)
        {
            try
            {
                // No Accept header on the download path (binary body).
                using var request = GetRequest(
                    $"{_apiBaseUrl}/documents/{Uri.EscapeDataString(documentId)}/download{AppQuery()}", acceptJson: false);
                using var response = await _httpClient.SendAsync(request);
                if (!await EnsureAuthorizedAsync(response) || !response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download document {DocumentId}", documentId);
                return null;
            }
        }

        public async Task<bool> DeleteAsync(string documentId)
        {
            try
            {
                using var response = await _httpClient.DeleteAsync(
                    $"{_apiBaseUrl}/documents/{Uri.EscapeDataString(documentId)}{AppQuery()}");
                if (!await EnsureAuthorizedAsync(response)) return false;
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete document {DocumentId}", documentId);
                return false;
            }
        }

        // ------------------------------------------------------------------

        private static HttpRequestMessage GetRequest(string url, bool acceptJson)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (acceptJson)
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private async Task<string> ErrorDetailAsync(HttpResponseMessage response)
        {
            var fallback = $"Upload failed ({(int)response.StatusCode})";
            try
            {
                var body = await response.Content.ReadFromJsonAsync<TextConflictBody>(Json);
                return string.IsNullOrEmpty(body?.Error) ? fallback : body!.Error!;
            }
            catch
            {
                return fallback;
            }
        }

        private Task<bool> EnsureAuthorizedAsync(HttpResponseMessage response)
        {
            // 401 = authentication failure — fire AuthenticationFailed once per token (a re-login prompt).
            // 403 = permission/feature denial (e.g. tier lacks DOCUMENTS); the token is valid, so no signal.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (!_authFailureFired)
                {
                    _authFailureFired = true;
                    AuthenticationFailed?.Invoke(this, EventArgs.Empty);
                }
                return Task.FromResult(false);
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return Task.FromResult(false);
            return Task.FromResult(true);
        }

        private string AppQuery() =>
            string.IsNullOrEmpty(_appId) ? string.Empty : $"?requestedAppId={Uri.EscapeDataString(_appId)}";

        /// <summary>Shape of the 409 (not-parsed-yet) and error bodies: <c>{ status?, error? }</c>.</summary>
        private sealed class TextConflictBody
        {
            public string? Status { get; set; }
            public string? Error { get; set; }
        }
    }
}
