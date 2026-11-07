
namespace BreakMeIfYouCan.Tests.Unit;

internal sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new StubHandler(handler), disposeHandler: false);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(handler(request));
        }
    }
}
