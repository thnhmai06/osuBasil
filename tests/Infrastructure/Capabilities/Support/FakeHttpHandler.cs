using System.Net;

namespace Basil.Infrastructure.Capabilities.Tests.Support;

/// <summary>Answers HTTP requests from a delegate and keeps the requests that were sent.</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
	: HttpMessageHandler
{
	/// <summary>Gets the requests that were sent, in order.</summary>
	public List<HttpRequestMessage> Requests { get; } = [];

	/// <summary>Answers with a JSON document.</summary>
	/// <param name="json">The body to answer with.</param>
	/// <returns>A handler that answers every request with <paramref name="json" />.</returns>
	public static FakeHttpHandler Json(string json)
	{
		return new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(json)
		}));
	}

	/// <summary>Answers with a status code and no meaningful body.</summary>
	/// <param name="status">The status code to answer with.</param>
	/// <returns>A handler that answers every request with <paramref name="status" />.</returns>
	public static FakeHttpHandler Status(HttpStatusCode status)
	{
		return new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
	}

	/// <inheritdoc />
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		Requests.Add(request);
		return send(request, cancellationToken);
	}
}