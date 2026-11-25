using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace Basil.Tests.Shared.Clients.Rest;

/// <summary>One event of a Server-Sent Events stream.</summary>
/// <param name="Event">The event name; <c>message</c> when the server named none.</param>
/// <param name="Data">The data lines joined by line feeds.</param>
/// <param name="Id">The event id, or <see langword="null"/> when it has none.</param>
public sealed record SseEvent(string Event, string Data, string? Id);

/// <summary>An open <c>text/event-stream</c> response, read event by event until it ends or the caller stops reading.</summary>
public sealed class SseStream(HttpRequestMessage request, HttpResponseMessage response) : IDisposable
{
	/// <summary>Gets the status of the response; a refused stream has no events.</summary>
	public HttpStatusCode Status => response.StatusCode;

	/// <inheritdoc />
	public void Dispose()
	{
		response.Dispose();
		request.Dispose();
	}

	/// <summary>Reads the events as they arrive.</summary>
	/// <param name="ct">Stops reading; this ends the sequence without an exception.</param>
	/// <returns>The events in order; the sequence ends when the server closes the stream, or at once for a refused one.</returns>
	public async IAsyncEnumerable<SseEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct)
	{
		if (!response.IsSuccessStatusCode) yield break;

		await using var body = await response.Content.ReadAsStreamAsync(ct);
		using var reader = new StreamReader(body, Encoding.UTF8);
		var name = string.Empty;
		var id = (string?)null;
		var data = new StringBuilder();
		while (true)
		{
			string? line;
			try
			{
				line = await reader.ReadLineAsync(ct);
			}
			catch (OperationCanceledException)
			{
				yield break;
			}

			if (line is null) yield break;

			if (line.Length == 0)
			{
				if (data.Length > 0) yield return new SseEvent(name.Length == 0 ? "message" : name, data.ToString(), id);

				name = string.Empty;
				id = null;
				data.Clear();
				continue;
			}

			if (line[0] == ':') continue;

			var colon = line.IndexOf(':');
			var field = colon < 0 ? line : line[..colon];
			var value = colon < 0 ? string.Empty : line[(colon + 1)..].TrimStart(' ');
			switch (field)
			{
				case "event":
					name = value;
					break;
				case "data":
					if (data.Length > 0) data.Append('\n');
					data.Append(value);
					break;
				case "id":
					id = value;
					break;
			}
		}
	}
}
