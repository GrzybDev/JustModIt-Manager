using System.Net.Http.Headers;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>The one place that decides how the API key travels, so a change the backend asks for touches nothing else.</summary>
internal sealed class ApiKeyHandler(string key) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return base.SendAsync(request, cancellationToken);
    }
}
