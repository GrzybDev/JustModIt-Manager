using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Attaches the API key in the one place that decides how it travels, so a change the backend asks for touches nothing
/// else, and sends a request the site refuses for the key again without it.
/// </summary>
/// <param name="key">The API key, sent as a bearer token.</param>
/// <param name="status">Shared with the client; a refusal recorded here drops the key for every later request.</param>
internal sealed class ApiKeyHandler(string key, ApiKeyStatus status) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (status.IsRefused)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (await RefusalOfAsync(response, cancellationToken).ConfigureAwait(false) is not { } refusal)
        {
            return response;
        }

        status.Refuse(refusal);
        response.Dispose();
        request.Headers.Authorization = null;
        // The site judges the key before the request itself, so even a counted request has not been counted yet.
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ApiKeyRefusal?> RefusalOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return ApiKeyRefusal.Invalid;
        }

        // Only a refusal of the key names it in its code; any other 403 is about the request.
        return response.StatusCode == HttpStatusCode.Forbidden
            && await CodeOfAsync(response.Content, cancellationToken).ConfigureAwait(false) is { } code
            && code.StartsWith("api_key_", StringComparison.Ordinal)
                ? code == "api_key_paused" ? ApiKeyRefusal.Paused : ApiKeyRefusal.NoAccess
                : null;
    }

    private static async Task<string?> CodeOfAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (!CatalogueErrors.IsJson(content))
        {
            return null;
        }

        try
        {
            using var body = JsonDocument.Parse(await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            return body.RootElement.ValueKind == JsonValueKind.Object && body.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                    ? code.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
