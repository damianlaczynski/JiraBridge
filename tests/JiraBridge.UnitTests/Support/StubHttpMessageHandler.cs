using System.Net;
using System.Net.Http;

namespace JiraBridge.UnitTests.Support;

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
  private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler;

  public List<HttpRequestMessage> Requests { get; } = [];

  public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
  {
    this.handler = handler;
  }

  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
  {
    Requests.Add(request);
    return Task.FromResult(handler(request, cancellationToken));
  }

  public static HttpResponseMessage Json(string json) =>
    new(HttpStatusCode.OK)
    {
      Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };
}
