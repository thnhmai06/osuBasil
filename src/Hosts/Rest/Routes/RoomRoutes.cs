using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Basil.Host.Rest.Sse;

namespace Basil.Host.Rest.Routes;

/// <summary>Multiplayer rooms: reading them and one route for each room command of <c>!mp</c>.</summary>
internal static class RoomRoutes
{
	public static void MapRooms(this RouteGroupBuilder v1)
	{
		var rooms = v1.MapGroup("/rooms").WithTags("Rooms").RequireToken();

		rooms.MapGet("/", ListAsync).WithSummary("List the open rooms the caller may see.")
			.WithDescription("Private rooms are listed only for those who may view them.");
		rooms.MapPost("/", CreateAsync).WithSummary("Open a tournament room.")
			.WithDescription("The caller becomes its creator. While room creation is locked, only room managers can open one.");
		rooms.MapGet("/{id:int}", GetAsync).WithSummary("Retrieve a room with its slots, referees and bans.");
		rooms.MapGet("/{id:int}/live", LiveAsync).Sse().WithSummary("Watch a room change.")
			.WithDescription("""
				The response is streamed using Server-Sent Events. The first event is the room as `GET /v1/rooms/{id}` returns it; after that a bundle arrives every second in which the room changed, carrying the room as it is then. The stream ends when the room closes. It carries no gameplay or scores. Data is up to about one second old.
				""");

		foreach (var command in RoomCommands.All) Command(rooms, command);
	}

	/// <summary>Maps <c>POST /rooms/{id}/{name}</c> to a room operation of the scoped room.</summary>
	private static void Command(RouteGroupBuilder rooms, RoomCommand command)
	{
		rooms.MapPost("/{id:int}/" + command.Name, async (HttpContext context, int id, Lobby lobby) =>
			{
				if (await Find(context, lobby, id) is not { } room) return Api.NotFound("No such room.");

				object? body = null;
				if (command.Body is { } type)
				{
					if (!context.Request.HasJsonContentType()) return Api.Invalid("A JSON body is required.");
					try
					{
						body = await context.Request.ReadFromJsonAsync(type, Api.Json, context.RequestAborted);
					}
					catch (System.Text.Json.JsonException)
					{
						return Api.Invalid("The body is not valid.");
					}

					if (body is null) return Api.Invalid("A body is required.");
				}

				var result = await command.Run(room.ScopeTo(context.Connection()), body, context.RequestServices,
					context.RequestAborted);
				return result.Answer();
			}).WithSummary(command.Summary).WithTags("Rooms");
	}

	private static async Task<Room?> Find(HttpContext context, Lobby lobby, int id)
	{
		return lobby.Find(id) is { } room &&
		       await lobby.ScopeTo(context.Connection()).CanViewAsync(room.Match, context.RequestAborted)
			? room
			: null;
	}

	private static async Task<IResult> ListAsync(HttpContext context, Lobby lobby, int? offset, int? limit)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var scoped = lobby.ScopeTo(context.Connection());
		var visible = new List<Room>();
		foreach (var room in lobby.Rooms.Values.OrderBy(room => room.Id))
			if (await scoped.CanViewAsync(room.Match, context.RequestAborted))
				visible.Add(room);

		return Api.Paged(new Page<RoomView>([.. visible.Skip(page!.Offset).Take(page.Limit).Select(RoomView.Of)],
			visible.Count), page);
	}

	private static async Task<IResult> GetAsync(HttpContext context, Lobby lobby, int id)
	{
		return await Find(context, lobby, id) is { } room ? Api.Ok(RoomView.Of(room)) : Api.NotFound("No such room.");
	}

	private static async Task<IResult> LiveAsync(HttpContext context, Lobby lobby, int id)
	{
		if (await Find(context, lobby, id) is not { } room) return Api.NotFound("No such room.");

		string? last = null;
		return await context.StreamAsync($"room:{id}", _ => Task.FromResult<(object, string?)>((RoomView.Of(room), null)), null,
			_ =>
			{
				if (room.IsClosed) return Task.FromResult(new SsePoll([new SseEvent(new { type = "closed" })], true));

				var view = RoomView.Of(room);
				var json = System.Text.Json.JsonSerializer.Serialize(view, Api.Json);
				if (json == last) return Task.FromResult(new SsePoll([]));

				last = json;
				return Task.FromResult(new SsePoll([new SseEvent(new { type = "room", room = view })]));
			});
	}

	private static async Task<IResult> CreateAsync(HttpContext context, Lobby lobby, CreateRoomRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Name)) return Api.Invalid("name is required.");

		var (room, result) = await lobby.ScopeTo(context.Connection()).OpenRoomAsync(RoomCommands.Truncate(request.Name),
			request.Password ?? string.Empty, true, request.IsPrivate ?? false,
			cancellationToken: context.RequestAborted);
		return result.IsOk ? Api.Created(RoomView.Of(room!)) : result.Refusal();
	}
}
