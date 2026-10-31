using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;

namespace Basil.Host.Rest.Routes;

/// <summary>The server settings, announcements and screenshots.</summary>
internal static class ContentRoutes
{
	/// <summary>The body of an announcement; no recipients means everyone.</summary>
	public sealed record AnnounceRequest(string? Text, int[]? To);

	/// <summary>The answer to an announcement.</summary>
	public sealed record AnnounceResponse(int Sent);

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

	private static async Task<IResult> GetAllAsync(HttpContext context, Server server)
	{
		var (result, settings) = await server.ScopeTo(context.Connection()).Settings.GetAsync(context.RequestAborted);
		return result.IsOk
			? Api.Ok(SettingsCategories.GetAll(settings!))
			: result.Refusal();
	}

	private static async Task<IResult> GetCategoryAsync(HttpContext context, string category, Server server)
	{
		if (!SettingsCategories.Exists(category)) return Api.NotFound("No such settings category.");

		var (result, settings) = await server.ScopeTo(context.Connection()).Settings.GetAsync(context.RequestAborted);
		return result.IsOk ? Api.Ok(SettingsCategories.Get(category, settings!)) : result.Refusal();
	}

	private static async Task<IResult> PatchCategoryAsync(HttpContext context, string category, JsonObject patch,
		Server server)
	{
		if (!SettingsCategories.Exists(category)) return Api.NotFound("No such settings category.");

		var scoped = server.ScopeTo(context.Connection()).Settings;
		var (read, current) = await scoped.GetAsync(context.RequestAborted);
		if (!read.IsOk) return read.Refusal();

		try
		{
			var changed = SettingsCategories.Patch(category, current!, patch);
			var stored = await scoped.UpdateAsync(changed, context.RequestAborted);
			return stored.IsOk ? Api.Ok(SettingsCategories.Get(category, changed), "Updated successfully") : stored.Refusal();
		}
		catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException
			                           or FormatException)
		{
			return Api.Invalid("The settings are not valid.");
		}
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
