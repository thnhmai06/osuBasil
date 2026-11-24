using Basil.Application.Users;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Messages;
using Basil.Domain.Content;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Routes;

internal static class ContentRoutes
{
	public static void MapContent(this RouteGroupBuilder v1)
	{
		var settings = v1.MapGroup("/settings").WithTags("Settings").RequireToken();
		settings.MapGet("/", GetAllAsync).Returns<object>().WithSummary("Get the server settings.")
			.WithDescription("Needs the Developer privilege. Groups every setting by category; the same groups are available one at a time through `GET /v1/settings/{category}`.");
		settings.MapGet("/{category}", GetCategoryAsync).Returns<object>().WithSummary("Get a settings category.")
			.WithDescription("Needs the Developer privilege. A category that does not exist is answered with 404.");
		settings.MapPatch("/{category}", PatchCategoryAsync)
			.Takes<object>().Returns<object>().WithSummary("Update a settings category.")
			.WithDescription(
				"The body holds only the fields to change; nested objects are merged. The change applies at once, except that rate limits already counting a caller keep their old limit until they expire.");

		v1.MapPost("/announce", AnnounceAsync).RequireToken().WithTags("Announcements")
			.Takes<AnnounceRequest>().Returns<AnnounceResponse>().WithSummary("Create an announcement.")
			.WithDescription(
				"The text in `text` goes to the users listed in `to`, or to everyone online when `to` is empty or absent. The answer counts the users it reached. A user in `to` that does not exist is answered with 404.");

		var screenshots = v1.MapGroup("/screenshots").WithTags("Screenshots").RequireToken();
		screenshots.MapGet("/", ListScreenshotsAsync).Admitted(WorkClass.Listing)
			.ReturnsList<ScreenshotView>().WithSummary("List screenshots.")
			.WithDescription("Needs the Moderator privilege. Each item names a screenshot, which `DELETE /v1/screenshots/{id}` removes.");
		screenshots.MapDelete("/{id}", DeleteScreenshotAsync).WithSummary("Delete a screenshot.")
			.WithDescription("Needs the Moderator privilege. A screenshot that does not exist is answered with 404.");
	}

	private static async Task<IResult> GetAllAsync(HttpContext context, ISender sender)
	{
		var (result, settings) = await sender.Send(new GetSettings(context.Session()), context.RequestAborted);
		return result == ServerActionResult.Ok
			? Api.Ok(SettingsCategories.GetAll(settings!))
			: result.Refusal();
	}

	private static async Task<IResult> GetCategoryAsync(HttpContext context, string category, ISender sender)
	{
		var (result, settings) = await sender.Send(new GetSettings(context.Session()), context.RequestAborted);
		if (result != ServerActionResult.Ok) return result.Refusal();

		return SettingsCategories.Exists(category)
			? Api.Ok(SettingsCategories.Get(category, settings!))
			: Api.NotFound("No such settings category.");
	}

	private static async Task<IResult> PatchCategoryAsync(HttpContext context, string category, ISender sender)
	{
		var actor = context.Session();
		var (read, current) = await sender.Send(new GetSettings(actor), context.RequestAborted);
		if (read != ServerActionResult.Ok) return read.Refusal();
		if (!SettingsCategories.Exists(category)) return Api.NotFound("No such settings category.");

		var (patch, refused) = await context.ReadBodyAsync<JsonObject>();
		if (refused is not null) return refused;

		try
		{
			var changed = SettingsCategories.Patch(category, current!, patch!);
			var stored = await sender.Send(new UpdateSettings(actor, changed), context.RequestAborted);
			return stored == ServerActionResult.Ok
				? Api.Ok(SettingsCategories.Get(category, changed), "Updated successfully")
				: stored.Refusal();
		}
		catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException
			                           or FormatException)
		{
			return Api.Invalid("The settings are not valid.");
		}
	}

	private static async Task<IResult> AnnounceAsync(HttpContext context, ISender sender)
	{
		var actor = context.Session();
		if (await sender.Send(new MayAnnounce(actor), context.RequestAborted) is
		    var may and not ServerActionResult.Ok)
			return may.Refusal();

		var (request, refused) = await context.ReadBodyAsync<AnnounceRequest>();
		if (refused is not null) return refused;

		if (string.IsNullOrWhiteSpace(request!.Text)) return Api.Invalid("text is required.");

		if (request.To is not { Length: > 0 } ids)
		{
			var (all, sent) = await sender.Send(new NotifyEveryone(actor, request.Text), context.RequestAborted);
			return all == ServerActionResult.Ok ? Api.Ok(new AnnounceResponse(sent)) : all.Refusal();
		}

		var recipients = new List<int>();
		foreach (var id in ids.Distinct())
		{
			if (await sender.Send(new GetUser(id), context.RequestAborted) is not { } user)
				return Api.NotFound($"No such user: {id}.");
			recipients.Add(user.Id);
		}

		var (some, count) = await sender.Send(new NotifyUsers(actor, request.Text, recipients), context.RequestAborted);
		return some == ServerActionResult.Ok ? Api.Ok(new AnnounceResponse(count)) : some.Refusal();
	}

	private static async Task<IResult> ListScreenshotsAsync(HttpContext context, ISender sender)
	{
		var (result, list) = await sender.Send(new ListScreenshots(context.Session()), context.RequestAborted);
		return result == ServerActionResult.Ok
			? Api.Ok(list.Select(reference => new ScreenshotView(Path.GetFileName(reference.Value))).ToList())
			: result.Refusal();
	}

	private static async Task<IResult> DeleteScreenshotAsync(HttpContext context, string id, ISender sender)
	{
		var actor = context.Session();
		var (listed, list) = await sender.Send(new ListScreenshots(actor), context.RequestAborted);
		if (listed != ServerActionResult.Ok) return listed.Refusal();

		var match = list.Where(reference => Path.GetFileName(reference.Value) == id).Cast<ContentReference?>()
			.FirstOrDefault();
		return match is null
			? Api.NotFound("No such screenshot.")
			: (await sender.Send(new DeleteScreenshot(actor, match.Value), context.RequestAborted)).Answer();
	}

	public sealed record AnnounceRequest(string? Text, int[]? To);

	public sealed record AnnounceResponse(int Sent);
}