using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Default <see cref="IFeedbackService"/> implementation. Calls the Wildwood feedback API
    /// endpoints. URLs are built from a root computed by stripping a trailing <c>/api</c> segment
    /// from the configured base URL, then re-adding <c>/api/...</c> per endpoint.
    /// </summary>
    public class FeedbackService : IFeedbackService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FeedbackService> _logger;
        private string _apiRoot = string.Empty;
        private string? _authToken;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public FeedbackService(HttpClient httpClient, ILogger<FeedbackService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void SetApiBaseUrl(string apiBaseUrl)
        {
            _apiRoot = StripApiSuffix(apiBaseUrl);
        }

        public void SetAuthToken(string? token)
        {
            _authToken = string.IsNullOrEmpty(token) ? null : token;
        }

        /// <summary>
        /// Removes a trailing <c>/api</c> or <c>/api/</c> from the supplied base URL so the result
        /// can have per-endpoint <c>/api/...</c> paths appended. Mirrors the vanilla widget's
        /// <c>apiBase.replace(/\/api\/?$/, '')</c>.
        /// </summary>
        private static string StripApiSuffix(string? baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl))
                return string.Empty;

            var trimmed = baseUrl.TrimEnd('/');
            if (trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 4);
            }
            return trimmed.TrimEnd('/');
        }

        private string BuildUrl(string path)
        {
            var suffix = path.TrimStart('/');
            if (!string.IsNullOrEmpty(_apiRoot))
                return $"{_apiRoot}/api/{suffix}";
            return $"/api/{suffix}";
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url, bool includeAuth)
        {
            var request = new HttpRequestMessage(method, url);
            if (includeAuth && !string.IsNullOrEmpty(_authToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
            }
            return request;
        }

        public async Task<FeedbackWidgetConfig?> GetWidgetConfigAsync(string appId)
        {
            if (string.IsNullOrEmpty(appId))
                return null;

            try
            {
                var url = BuildUrl($"AppComponentConfigurations/{Uri.EscapeDataString(appId)}/feedback/widget");
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetWidgetConfig returned {StatusCode} for app {AppId}", response.StatusCode, appId);
                    return null;
                }

                var config = await response.Content.ReadFromJsonAsync<FeedbackWidgetConfig>(JsonOptions);
                return config;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error loading feedback widget config for app {AppId}", appId);
                return null;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request timeout loading feedback widget config for app {AppId}", appId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error loading feedback widget config for app {AppId}", appId);
                return null;
            }
        }

        public async Task<FeedbackSubmissionResult> SubmitFeedbackAsync(FeedbackSubmissionRequest request)
        {
            try
            {
                var url = BuildUrl("SystemFeedback");
                var httpRequest = CreateRequest(HttpMethod.Post, url, includeAuth: true);
                httpRequest.Content = JsonContent.Create(request);

                var response = await _httpClient.SendAsync(httpRequest);

                if (response.IsSuccessStatusCode)
                {
                    return new FeedbackSubmissionResult { Success = true };
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return new FeedbackSubmissionResult
                    {
                        Success = false,
                        RateLimited = true,
                        ErrorMessage = "Too many submissions. Please try again later."
                    };
                }

                var errorMessage = await ExtractErrorMessageAsync(response);
                _logger.LogWarning("SubmitFeedback returned {StatusCode} for app {AppId}", response.StatusCode, request.AppId);
                return new FeedbackSubmissionResult { Success = false, ErrorMessage = errorMessage };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error submitting feedback for app {AppId}", request.AppId);
                return new FeedbackSubmissionResult
                {
                    Success = false,
                    ErrorMessage = "Could not reach the server. Please check your connection and try again."
                };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request timeout submitting feedback for app {AppId}", request.AppId);
                return new FeedbackSubmissionResult
                {
                    Success = false,
                    ErrorMessage = "The request timed out. Please try again."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error submitting feedback for app {AppId}", request.AppId);
                return new FeedbackSubmissionResult
                {
                    Success = false,
                    ErrorMessage = "An unexpected error occurred. Please try again."
                };
            }
        }

        public async Task<FeedbackDuplicateCheckResult> CheckDuplicateAsync(string title, string appId)
        {
            var empty = new FeedbackDuplicateCheckResult { HasPotentialDuplicate = false };

            if (string.IsNullOrWhiteSpace(title))
                return empty;

            try
            {
                var url = BuildUrl($"SystemFeedback/duplicate-check?title={Uri.EscapeDataString(title.Trim())}");
                if (!string.IsNullOrEmpty(appId))
                {
                    url += $"&appId={Uri.EscapeDataString(appId)}";
                }

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("CheckDuplicate returned {StatusCode} for app {AppId}", response.StatusCode, appId);
                    return empty;
                }

                var result = await response.Content.ReadFromJsonAsync<FeedbackDuplicateCheckResult>(JsonOptions);
                return result ?? empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking for duplicate feedback for app {AppId}", appId);
                return empty;
            }
        }

        public async Task<FeedbackVoteResult> VoteAsync(string feedbackId)
        {
            if (string.IsNullOrEmpty(feedbackId))
                return new FeedbackVoteResult { Success = false, ErrorMessage = "Invalid feedback id." };

            try
            {
                var url = BuildUrl($"SystemFeedback/{Uri.EscapeDataString(feedbackId)}/vote");
                var httpRequest = CreateRequest(HttpMethod.Post, url, includeAuth: true);

                var response = await _httpClient.SendAsync(httpRequest);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<FeedbackVoteResult>(JsonOptions);
                    if (result != null)
                    {
                        result.Success = true;
                        return result;
                    }
                    return new FeedbackVoteResult { Success = true };
                }

                _logger.LogWarning("Vote returned {StatusCode} for feedback {FeedbackId}", response.StatusCode, feedbackId);
                return new FeedbackVoteResult { Success = false, ErrorMessage = "Could not record your vote." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error voting on feedback {FeedbackId}", feedbackId);
                return new FeedbackVoteResult { Success = false, ErrorMessage = "Network error while voting." };
            }
        }

        /// <summary>
        /// Extracts a user-facing error message from a failed response body, preferring an
        /// <c>error</c> or <c>title</c> JSON property, then falling back to a status-code message.
        /// </summary>
        private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response)
        {
            try
            {
                var text = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            if (doc.RootElement.TryGetProperty("error", out var errorProp) &&
                                errorProp.ValueKind == JsonValueKind.String)
                            {
                                var msg = errorProp.GetString();
                                if (!string.IsNullOrEmpty(msg))
                                    return msg;
                            }
                            if (doc.RootElement.TryGetProperty("title", out var titleProp) &&
                                titleProp.ValueKind == JsonValueKind.String)
                            {
                                var msg = titleProp.GetString();
                                if (!string.IsNullOrEmpty(msg))
                                    return msg;
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Body was not JSON — fall through to the status-code message.
                    }
                }
            }
            catch
            {
                // Ignore body read failures and use the status-code message.
            }

            return $"Failed to submit feedback (status {(int)response.StatusCode}).";
        }
    }
}
