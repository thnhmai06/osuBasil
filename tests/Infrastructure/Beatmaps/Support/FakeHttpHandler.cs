using System.Net;

namespace Basil.Infrastructure.Beatmaps.Tests.Support;

internal sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
	: HttpMessageHandler
{
	public List<HttpRequestMessage> Requests { get; } = [];

	public static FakeHttpHandler Json(string json)
	{
		return new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(json)
		}));
	}

	public static FakeHttpHandler Status(HttpStatusCode status)
	{
		return new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
		CancellationToken ct)
	{
		Requests.Add(request);
		return send(request, ct);
	}
}