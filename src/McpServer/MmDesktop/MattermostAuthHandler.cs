using System.Net.Http.Headers;

namespace MattermostMCP.MmDesktop;

/// <summary>
/// Подставляет bearer-токен MmDesktop в каждый исходящий запрос.
/// Хендлер не хранит состояние — токен живёт в singleton-провайдере.
/// </summary>
public sealed class MattermostAuthHandler(MattermostTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
