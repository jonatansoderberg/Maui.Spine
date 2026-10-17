using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// Adds the signed-in user's access token to every request, and on a 401 refreshes it and sends the request once more.
/// A request goes without a token when nobody is signed in.
/// </summary>
internal sealed class SpineAuthHandler(SpineAuthSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await session.GetAccessTokenAsync(cancellationToken);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // A body that can only be read once could not be sent again after a 401.
        if (token is not null && request.Content is not null) await request.Content.LoadIntoBufferAsync(cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null) return response;

        // A token the server no longer takes (a restart that changed keys, a clock that drifted): one fresh try.
        if (await session.RefreshAfterUnauthorizedAsync(token, cancellationToken) is not { } fresh || fresh == token) return response;

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh);
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Adds Spine's access token to an app's own <see cref="HttpClient"/>s.</summary>
public static class SpineAuthHttpClientBuilderExtensions
{
    /// <summary>
    /// Sends the signed-in user's access token with every request of this client, refreshing it as needed and once more
    /// after a 401.
    /// </summary>
    /// <param name="builder">The client from <c>AddHttpClient</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddHttpClient&lt;OrdersApi&gt;(c => c.BaseAddress = new("https://api.example.com"))
    ///                 .AddSpineAuthentication();
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddSpineAuthentication(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddHttpMessageHandler(sp => new SpineAuthHandler(sp.GetRequiredService<SpineAuthSession>()));
    }
}
