using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Basil.Tests.Shared.Clients.Rest;

/// <summary>A client of the <c>api.</c> host: signs in for a Bearer token, then calls the <c>/v1</c> routes and reads the response envelope.</summary>
/// <remarks>A refusal is an answer; only a transport failure throws. A body that is not an envelope (for example a refusal of the front) leaves <see cref="RestReply.Data"/> undefined and is kept in <see cref="RestReply.Raw"/>.</remarks>
public sealed class RestClient(Endpoint endpoint, HttpClient http)
{
	private const string Host = "api";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	/// <summary>Gets the Bearer token, or <see langword="null"/> while the client is not signed in.</summary>
	public string? Token { get; private set; }

	/// <summary>Gets the id of the signed-in user, or 0 while the client is not signed in.</summary>
	public int UserId { get; private set; }

	/// <summary>Signs in with an account name and password.</summary>
	public async Task<RestReply> SignInAsync(string username, string password, CancellationToken ct)
	{
		var reply = await SendAsync(HttpMethod.Post, "/v1/auth/login", new { username, password }, false, ct);
		if (reply is { Success: true, Data.ValueKind: JsonValueKind.Object } && reply.Data.TryGetProperty("token", out var token) &&
		    reply.Data.TryGetProperty("userId", out var userId))
		{
			Token = token.GetString();
			UserId = userId.GetInt32();
		}

		return reply;
	}

	/// <summary>Signs out and forgets the token.</summary>
	public async Task<RestReply> SignOutAsync(CancellationToken ct)
	{
		var reply = await SendAsync(HttpMethod.Post, "/v1/auth/logout", null, true, ct);
		Token = null;
		UserId = 0;
		return reply;
	}

	/// <summary>Calls a GET route.</summary>
	public Task<RestReply> GetAsync(string path, CancellationToken ct)
	{
		return SendAsync(HttpMethod.Get, path, null, true, ct);
	}

	/// <summary>Calls a POST route.</summary>
	public Task<RestReply> PostAsync(string path, object? body, CancellationToken ct)
	{
		return SendAsync(HttpMethod.Post, path, body, true, ct);
	}

	/// <summary>Calls a PUT route.</summary>
	public Task<RestReply> PutAsync(string path, object? body, CancellationToken ct)
	{
		return SendAsync(HttpMethod.Put, path, body, true, ct);
	}

	/// <summary>Calls a PATCH route.</summary>
	public Task<RestReply> PatchAsync(string path, object? body, CancellationToken ct)
	{
		return SendAsync(HttpMethod.Patch, path, body, true, ct);
	}

	/// <summary>Calls a DELETE route.</summary>
	public Task<RestReply> DeleteAsync(string path, object? body, CancellationToken ct)
	{
		return SendAsync(HttpMethod.Delete, path, body, true, ct);
	}

	/// <summary>Uploads the avatar of a user.</summary>
	/// <param name="userId">The user whose avatar is set.</param>
	/// <param name="picture">A PNG or JPEG file.</param>
	public async Task<RestReply> PutAvatarAsync(int userId, byte[] picture, CancellationToken ct)
	{
		using var request = NewRequest(HttpMethod.Put, $"/v1/users/{userId}/avatar", true);
		using var form = new MultipartFormDataContent { { new ByteArrayContent(picture), "file", "avatar.png" } };
		request.Content = form;
		return await ReadAsync(request, ct);
	}

	/// <summary>Opens a live stream.</summary>
	/// <param name="path">The route of the stream, such as <c>/v1/matches/12</c>, <c>/v1/rooms/12/live</c> or <c>/v1/diagnostics</c>.</param>
	/// <param name="authorize">Whether to send the token; a match report needs none.</param>
	/// <param name="ct">Stops opening; the stream is stopped through <see cref="SseStream.ReadAsync"/>.</param>
	public async Task<SseStream> OpenStreamAsync(string path, bool authorize, CancellationToken ct)
	{
		var request = NewRequest(HttpMethod.Get, path, authorize);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
		var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
		return new SseStream(request, response);
	}

	private async Task<RestReply> SendAsync(HttpMethod method, string path, object? body, bool authorize,
		CancellationToken ct)
	{
		using var request = NewRequest(method, path, authorize);
		if (body is not null) request.Content = JsonContent.Create(body, options: Json);
		return await ReadAsync(request, ct);
	}

	private HttpRequestMessage NewRequest(HttpMethod method, string path, bool authorize)
	{
		var request = endpoint.Request(method, Host, path);
		if (authorize && Token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
		return request;
	}

	private async Task<RestReply> ReadAsync(HttpRequestMessage request, CancellationToken ct)
	{
		using var response = await http.SendAsync(request, ct);
		return RestReply.Read(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
	}
}

/// <summary>An answer of the <c>api.</c> host, read from its response envelope.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Success">The envelope's success flag; <see langword="false"/> when the body is not an envelope.</param>
/// <param name="Message">The envelope's message, or an empty text.</param>
/// <param name="ErrorCode">The envelope's error code, or <see langword="null"/> when there is none.</param>
/// <param name="Data">The envelope's data; undefined when there is none.</param>
/// <param name="Meta">The envelope's paging data; undefined when there is none.</param>
/// <param name="Raw">The body as it came.</param>
public sealed record RestReply(
	HttpStatusCode Status,
	bool Success,
	string Message,
	string? ErrorCode,
	JsonElement Data,
	JsonElement Meta,
	string Raw)
{
	internal static RestReply Read(HttpStatusCode status, string raw)
	{
		try
		{
			using var document = JsonDocument.Parse(raw);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return Unreadable(status, raw);

			return new RestReply(status,
				root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True,
				root.TryGetProperty("message", out var message) ? message.GetString() ?? string.Empty : string.Empty,
				root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object &&
				errors.TryGetProperty("code", out var code)
					? code.GetString()
					: null,
				root.TryGetProperty("data", out var data) ? data.Clone() : default,
				root.TryGetProperty("meta", out var meta) ? meta.Clone() : default,
				raw);
		}
		catch (JsonException)
		{
			return Unreadable(status, raw);
		}
	}

	private static RestReply Unreadable(HttpStatusCode status, string raw)
	{
		return new RestReply(status, false, string.Empty, null, default, default, raw);
	}
}
