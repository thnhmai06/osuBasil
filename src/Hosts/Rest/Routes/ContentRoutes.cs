using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Runtime;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;

namespace Basil.Host.Rest.Routes;

/// <summary>The server settings, announcements and screenshots.</summary>
internal static class ContentRoutes
{
	/// <summary>The settings categories and how each one is read and replaced.</summary>
	private static readonly Dictionary<string, (Func<ServerSettings, object> Get, Func<ServerSettings, JsonNode, ServerSettings> Apply)>
		Categories = new(StringComparer.OrdinalIgnoreCase)
		{
			["general"] = Category(s => s.General, (s, v) => s with { General = v }),
			["locks"] = Category(s => LocksView.Of(s.Locks), (s, v) => s with { Locks = v.ToLocks() }),
			["sessions"] = Category(s => s.Sessions, (s, v) => s with { Sessions = v }),
			["rateLimits"] = Category(s => s.RateLimits, (s, v) => s with { RateLimits = v }),
			["uploads"] = Category(s => s.Uploads, (s, v) => s with { Uploads = v }),
			["beatmaps"] = Category(s => s.Beatmaps, (s, v) => s with { Beatmaps = v }),
			["multiplayer"] = Category(s => s.Multiplayer, (s, v) => s with { Multiplayer = v }),
			["bot"] = Category(s => s.Bot, (s, v) => s with { Bot = v }),
			["cors"] = Category(s => s.Cors, (s, v) => s with { Cors = v })
		};

	/// <summary>The locks as one flag each.</summary>
	public sealed record LocksView(bool Accounts, bool Rooms, bool Beatmapsets, bool Profiles)
	{
		public static LocksView Of(Locks locks)
		{
			return new LocksView(locks.HasFlag(Locks.Accounts), locks.HasFlag(Locks.Rooms),
				locks.HasFlag(Locks.Beatmapsets), locks.HasFlag(Locks.Profiles));
		}

		public Locks ToLocks()
		{
			return (Accounts ? Locks.Accounts : 0) | (Rooms ? Locks.Rooms : 0) | (Beatmapsets ? Locks.Beatmapsets : 0) |
			       (Profiles ? Locks.Profiles : 0);
		}
	}

	/// <summary>The body of an announcement; no recipients means everyone.</summary>
	public sealed record AnnounceRequest(string? Text, int[]? To);

	/// <summary>The answer to an announcement.</summary>
	public sealed record AnnounceResponse(int Sent);

	/// <summary>A stored screenshot.</summary>
	public sealed record ScreenshotView(string Id);

	public static void MapContent(this RouteGroupBuilder v1)
	{
		var settings = v1.MapGroup("/settings").WithTags("Settings").RequireToken();
		settings.MapGet("/", GetAllAsync).WithSummary("Retrieve every category of the server settings.");
		settings.MapGet("/{category}", GetCategoryAsync).WithSummary("Retrieve one category of the server settings.");
		settings.MapPatch("/{category}", PatchCategoryAsync).WithSummary("Change the given fields of one settings category.")
			.WithDescription("The body holds only the fields to change; nested objects are merged. The change applies at once. Rate limit partitions that already exist keep their old limit until they expire.");

		v1.MapPost("/announce", AnnounceAsync).RequireToken().WithTags("Announcements")
			.WithSummary("Send an announcement to some users or to everyone online.")
			.WithDescription("An empty or absent `to` announces to everyone online.");

		var screenshots = v1.MapGroup("/screenshots").WithTags("Screenshots").RequireToken();
		screenshots.MapGet("/", ListScreenshotsAsync).WithSummary("List the stored screenshots.");
		screenshots.MapDelete("/{id}", DeleteScreenshotAsync).WithSummary("Delete a screenshot.");
	}

	private static (Func<ServerSettings, object>, Func<ServerSettings, JsonNode, ServerSettings>) Category<T>(
		Func<ServerSettings, T> get, Func<ServerSettings, T, ServerSettings> set)
	{
		return (s => get(s)!, (s, node) => set(s, node.Deserialize<T>(Api.Json)!));
	}

	private static async Task<IResult> GetAllAsync(HttpContext context, Server server)
	{
		var (result, settings) = await server.ScopeTo(context.Connection()).Settings.GetAsync(context.RequestAborted);
		return result.IsOk
			? Api.Ok(Categories.ToDictionary(c => c.Key, c => c.Value.Get(settings!)))
			: result.Refusal();
	}

	private static async Task<IResult> GetCategoryAsync(HttpContext context, string category, Server server)
	{
		if (!Categories.TryGetValue(category, out var entry)) return Api.NotFound("No such settings category.");

		var (result, settings) = await server.ScopeTo(context.Connection()).Settings.GetAsync(context.RequestAborted);
		return result.IsOk ? Api.Ok(entry.Get(settings!)) : result.Refusal();
	}

	private static async Task<IResult> PatchCategoryAsync(HttpContext context, string category, JsonObject patch,
		Server server)
	{
		if (!Categories.TryGetValue(category, out var entry)) return Api.NotFound("No such settings category.");

		var scoped = server.ScopeTo(context.Connection()).Settings;
		var (read, current) = await scoped.GetAsync(context.RequestAborted);
		if (!read.IsOk) return read.Refusal();

		try
		{
			var merged = JsonSerializer.SerializeToNode(entry.Get(current!), Api.Json)!;
			Merge(merged, patch);
			var changed = entry.Apply(current!, merged);
			var stored = await scoped.UpdateAsync(changed, context.RequestAborted);
			return stored.IsOk ? Api.Ok(entry.Get(changed), "Updated successfully") : stored.Refusal();
		}
		catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException
			                           or FormatException)
		{
			return Api.Invalid("The settings are not valid.");
		}
	}

	/// <summary>Merges a patch into a node the way JSON merge patch does: objects merge, anything else replaces.</summary>
	private static void Merge(JsonNode target, JsonObject patch)
	{
		if (target is not JsonObject targetObject) return;

		foreach (var (key, value) in patch)
			if (value is JsonObject nested && targetObject[key] is JsonObject existing)
				Merge(existing, nested);
			else
				targetObject[key] = value?.DeepClone();
	}

	private static async Task<IResult> AnnounceAsync(HttpContext context, AnnounceRequest request, Server server,
		IUserRepository users)
	{
		if (string.IsNullOrWhiteSpace(request.Text)) return Api.Invalid("text is required.");

		var scoped = server.ScopeTo(context.Connection());
		if (request.To is not { Length: > 0 } ids)
		{
			var (all, sent) = scoped.NotifyAll(request.Text);
			return all.IsOk ? Api.Ok(new AnnounceResponse(sent)) : all.Refusal();
		}

		var recipients = new List<UserRecord>();
		foreach (var id in ids.Distinct())
		{
			if (await users.GetAsync(id, context.RequestAborted) is not { } user)
				return Api.NotFound($"No such user: {id}.");
			recipients.Add(user);
		}

		var (some, count) = scoped.Notify(request.Text, recipients);
		return some.IsOk ? Api.Ok(new AnnounceResponse(count)) : some.Refusal();
	}

	private static async Task<IResult> ListScreenshotsAsync(HttpContext context, Server server)
	{
		var (result, list) = await server.ScopeTo(context.Connection()).Screenshots.ListAsync(context.RequestAborted);
		return result.IsOk
			? Api.Ok(list.Select(reference => new ScreenshotView(Path.GetFileName(reference.Value))).ToList())
			: result.Refusal();
	}

	private static async Task<IResult> DeleteScreenshotAsync(HttpContext context, string id, Server server)
	{
		var screenshots = server.ScopeTo(context.Connection()).Screenshots;
		var (listed, list) = await screenshots.ListAsync(context.RequestAborted);
		if (!listed.IsOk) return listed.Refusal();

		var match = list.Where(reference => Path.GetFileName(reference.Value) == id).Cast<Domain.Content.ContentReference?>()
			.FirstOrDefault();
		return match is null
			? Api.NotFound("No such screenshot.")
			: (await screenshots.DeleteAsync(match.Value, context.RequestAborted)).Answer();
	}
}
