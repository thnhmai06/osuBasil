using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Basil.LoadTests.Client;

/// <summary>
///     Typed calls against the <c>api.</c> host, used both for one-time setup (account seeding, admin
///     key, beatmapset fixture ingestion) and by <c>ApiScenario</c>'s per-request timing in Phase 2.
///     Every response is enveloped (<c>{success, code, message, data, ...}</c>); this client always
///     unwraps <c>data</c> before handing a value back, since scenarios time the request, not the
///     envelope parsing.
/// </summary>
public sealed class BasilApiClient(BasilHttpClientFactory clientFactory)
{
	private const int DefaultLoadTestPrivilege = 19; // Unrestricted | Verified | Supporter

	/// <summary>Creates a user via the admin API. Snapshot-first seeding means this is called once per fresh database, not per run.</summary>
	/// <returns>The created user's id.</returns>
	public async Task<int> CreateUserAsync(string name, string password, string country,
		CancellationToken cancellationToken = default)
	{
		using var client = clientFactory.CreateClient();
		var body = JsonSerializer.Serialize(new { name, password, country, privilege = DefaultLoadTestPrivilege });
		using var content = new StringContent(body, Encoding.UTF8, "application/json");
		using var response =
			await client.PostAsync(clientFactory.BuildUri("api", "/users"), content, cancellationToken);
		response.EnsureSuccessStatusCode();
		var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
		return envelope.GetProperty("data").GetProperty("id").GetInt32();
	}

	/// <summary>
	///     Creates a user, or resolves its existing id if the name is already taken (409 Conflict) —
	///     used when a database snapshot could not be captured/restored (e.g. <c>ExistingServerHost</c>)
	///     and accounts from a previous run may already exist.
	/// </summary>
	public async Task<int> EnsureUserAsync(string name, string password, string country,
		CancellationToken cancellationToken = default)
	{
		using var client = clientFactory.CreateClient();
		var body = JsonSerializer.Serialize(new { name, password, country, privilege = DefaultLoadTestPrivilege });
		using var content = new StringContent(body, Encoding.UTF8, "application/json");
		using var response =
			await client.PostAsync(clientFactory.BuildUri("api", "/users"), content, cancellationToken);

		if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
		{
			response.EnsureSuccessStatusCode();
			var created = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
			return created.GetProperty("data").GetProperty("id").GetInt32();
		}

		// SocketsHttpHandler follows the 302 from /users/{name} to /users/{id} automatically.
		using var lookup =
			await client.GetAsync(clientFactory.BuildUri("api", $"/users/{name}"), cancellationToken);
		lookup.EnsureSuccessStatusCode();
		var existing = await lookup.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
		return existing.GetProperty("data").GetProperty("id").GetInt32();
	}

	/// <summary>Sets the admin key, taking the server out of bypass mode, so anonymous API reads no longer redact private data.</summary>
	public async Task SetAdminKeyAsync(string key, CancellationToken cancellationToken = default)
	{
		using var client = clientFactory.CreateClient();
		var body = JsonSerializer.Serialize(new { key });
		using var content = new StringContent(body, Encoding.UTF8, "application/json");
		using var response =
			await client.PutAsync(clientFactory.BuildUri("api", "/adminkey"), content, cancellationToken);
		response.EnsureSuccessStatusCode();
	}

	/// <summary>Attaches the given admin key as a Bearer token to every request the returned client sends.</summary>
	public HttpClient CreateAuthorizedClient(string adminKey)
	{
		var client = clientFactory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminKey);
		return client;
	}

	/// <summary>Uploads an <c>.osz</c> beatmapset fixture and returns its assigned mapset id.</summary>
	public async Task<int> UploadBeatmapsetAsync(byte[] oszBytes, string fileName, string adminKey,
		CancellationToken cancellationToken = default)
	{
		using var client = CreateAuthorizedClient(adminKey);
		using var content = new MultipartFormDataContent();
		using var fileContent = new ByteArrayContent(oszBytes);
		fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		content.Add(fileContent, "file", fileName);

		using var response =
			await client.PostAsync(clientFactory.BuildUri("api", "/beatmapsets"), content, cancellationToken);
		response.EnsureSuccessStatusCode();
		var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
		return envelope.GetProperty("data").GetProperty("id").GetInt32();
	}

	/// <summary>Best-effort lookup of any existing match id, for <c>ApiScenario</c>'s <c>match_report</c> target.</summary>
	/// <returns>The first match id found, or <see langword="null" /> if none exist.</returns>
	public async Task<int?> ResolveSampleMatchIdAsync(CancellationToken cancellationToken = default)
	{
		using var client = clientFactory.CreateClient();
		using var response = await client.GetAsync(
			clientFactory.BuildUri("api", "/matches?status=all&page=1&pageSize=1"), cancellationToken);
		if (!response.IsSuccessStatusCode) return null;

		var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
		var items = envelope.GetProperty("data");
		return items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0
			? items[0].GetProperty("id").GetInt32()
			: null;
	}

	/// <summary>Best-effort lookup of any existing beatmapset id, for <c>ApiScenario</c>'s <c>beatmapset</c> target.</summary>
	/// <returns>The first beatmapset id found, or <see langword="null" /> if none exist.</returns>
	public async Task<int?> ResolveSampleBeatmapsetIdAsync(CancellationToken cancellationToken = default)
	{
		using var client = clientFactory.CreateClient();
		using var response =
			await client.GetAsync(clientFactory.BuildUri("api", "/beatmapsets?page=1&pageSize=1"), cancellationToken);
		if (!response.IsSuccessStatusCode) return null;

		var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
		var items = envelope.GetProperty("data");
		return items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0
			? items[0].GetProperty("id").GetInt32()
			: null;
	}

	/// <summary>Resolves a beatmap id belonging to the given mapset, for assigning to a multiplayer room.</summary>
	public async Task<int?> ResolveFirstBeatmapIdAsync(int mapsetId, CancellationToken cancellationToken = default)
	{
		using var client = clientFactory.CreateClient();
		using var response =
			await client.GetAsync(clientFactory.BuildUri("api", $"/beatmapsets/{mapsetId}"), cancellationToken);
		if (!response.IsSuccessStatusCode) return null;

		var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
		var beatmaps = envelope.GetProperty("data").GetProperty("beatmaps");
		return beatmaps.GetArrayLength() > 0 ? beatmaps[0].GetProperty("id").GetInt32() : null;
	}
}
