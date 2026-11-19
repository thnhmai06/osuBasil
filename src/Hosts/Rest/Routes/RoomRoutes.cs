using System.Text.Json;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Domain.Multiplayer.Room;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Sse;
using Basil.Host.Shared.Views;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Host.Rest.Routes;

internal static class RoomRoutes
{
	public static void MapRooms(this RouteGroupBuilder v1)
	{
		var rooms = v1.MapGroup("/rooms").WithTags("Rooms").RequireToken();

		rooms.MapGet("/", ListAsync).Admitted(WorkClass.Listing).WithSummary("List rooms.")
			.WithDescription("""
			                 `query` uses the room search syntax: free text matches the room's name; `host:`, `creator:` and `player:` take an account name or id; `status:` takes `open`, `locked` or `playing`; `mode:` takes `0` to `3`; `password:` takes `yes` or `no`. Rooms are ordered by id. Private rooms are listed only for those who may view them.
			                 """);
		rooms.MapPost("/", CreateAsync).WithSummary("Create a room.")
			.WithDescription("""
			                 The caller becomes the creator of the room, and its host when their game client is online and in no room. While room creation is locked, only room managers can create one.
			                 """);
		rooms.MapGet("/{id:int}", GetAsync).WithSummary("Get a room.")
			.WithDescription(
				"Includes its slots, referees and bans. A private room the caller may not view is answered with 404.");
		rooms.MapGet("/{id:int}/live", LiveAsync).Sse().WithSummary("Stream a room.")
			.WithDescription("""
			                 The response is streamed using Server-Sent Events. The first event is the room as `GET /v1/rooms/{id}` returns it; after that a bundle arrives every second in which the room changed, carrying the room as it is then. The stream ends when the room closes and carries no gameplay or scores.
			                 """);

		foreach (var command in RoomCommands.All) Command(rooms, command);
	}

	private static void Command(RouteGroupBuilder rooms, RoomCommand command)
	{
		rooms.MapPost("/{id:int}/" + command.Name, async (HttpContext context, int id, Lobby lobby, ISender sender) =>
		{
			if (await Find(context, lobby, sender, id) is not { } room) return Api.NotFound("No such room.");

			object? body = null;
			if (command.Body is { } type)
			{
				if (!context.Request.HasJsonContentType()) return Api.Invalid("A JSON body is required.");
				try
				{
					body = await context.Request.ReadFromJsonAsync(type, Api.Json, context.RequestAborted);
				}
				catch (JsonException)
				{
					return Api.Invalid("The body is not valid.");
				}

				if (body is null) return Api.Invalid("A body is required.");
			}

			var result = await command.Run(new RoomCall(sender, context.Session(), room), body,
				context.RequestServices, context.RequestAborted);
			return result.Answer();
		}).WithSummary(command.Summary).WithTags("Rooms");
	}

	private static async Task<Room?> Find(HttpContext context, Lobby lobby, ISender sender, int id)
	{
		return lobby.Find(id) is { } room &&
		       await sender.Send(new CanViewMatch(context.Session(), room.Match.Id), context.RequestAborted)
			? room
			: null;
	}

	private static async Task<IResult> ListAsync(HttpContext context, ISender sender, string? query, int? offset,
		int? limit)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var found = await sender.Send(
			new ListRooms(context.Session(), RoomQuery.Parse(query ?? string.Empty), page!),
			context.RequestAborted);
		return Api.Paged(new Page<RoomView>([.. found.Items.Select(RoomView.Of)], found.HasNext), page);
	}

	private static async Task<IResult> GetAsync(HttpContext context, Lobby lobby, ISender sender, int id)
	{
		return await Find(context, lobby, sender, id) is { } room
			? Api.Ok(RoomView.Of(room))
			: Api.NotFound("No such room.");
	}

	private static async Task<IResult> LiveAsync(HttpContext context, Lobby lobby, ISender sender, int id)
	{
		if (await Find(context, lobby, sender, id) is not { } room) return Api.NotFound("No such room.");

		var initial = RoomView.Of(room);
		var last = JsonSerializer.Serialize(initial, Api.Json);
		return await context.StreamAsync($"room:{id}",
			_ => Task.FromResult<(object, string?)>((initial, null)), null,
			_ =>
			{
				if (room.IsClosed) return Task.FromResult(new SsePoll([new SseEvent(new { type = "closed" })], true));

				var view = RoomView.Of(room);
				var json = JsonSerializer.Serialize(view, Api.Json);
				if (json == last) return Task.FromResult(new SsePoll([]));

				last = json;
				return Task.FromResult(new SsePoll([new SseEvent(new { type = "room", room = view })]));
			});
	}

	private static async Task<IResult> CreateAsync(HttpContext context, ISender sender, CreateRoomRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Name)) return Api.Invalid("name is required.");

		var (room, result) = await sender.Send(new OpenRoom(context.Session(),
				RoomCommands.Truncate(request.Name), request.Password ?? string.Empty, RoomSource.Api,
				request.IsPrivate ?? false),
			context.RequestAborted);
		return result == RoomOutcome.Ok ? Api.Created(RoomView.Of(room!)) : result.Refusal();
	}
}