using Basil.Domain.Login;
using Basil.Server.Features.Auth;
using Basil.Server.Features.Chat;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.OpenApi;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `/matches/{matchId}/chat` stream and send routes.</summary>
internal static class MatchChatEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the match chat routes on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchChat(this RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:numericid}/chat/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry, IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				return match is not null
					? MatchLiveRoutes.HandleChat(context, match, events, cancellationToken)
					: SseEndpoints.NotLive();
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("getMatchChatLive")
			.WithSummary("Stream match chat.")
			.WithDescription("""
			                 Server-Sent Events stream of every line said in the match's own chat, whoever said
			                 it and however they are connected — an osu! client, an IRC client, or BasilBot
			                 answering a command.

			                 Chat is never stored, so the stream carries only what is said from the moment it
			                 opens; there is no history to read back and no plain-JSON sibling of this route.

			                 The admin key travels in the `Authorization` header, which a browser's built-in
			                 `EventSource` cannot set — consume this from a server-side client, or a client that
			                 supports request headers.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Chat")
			.Produces<MatchChatMessage>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MatchChatMessage(new UserBrief(8, "Bob", Country.Gb), "glhf",
					DateTimeOffset.Parse("2026-07-20T14:30:00Z")))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not live"));

		group.MapPost("/matches/{matchId:numericid}/chat", (int matchId, SendMatchChatRequest body,
				IMatchRegistry matchRegistry, IChannelRegistry channelRegistry, ChatDispatchService chatDispatch) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				if (string.IsNullOrWhiteSpace(body.Text))
					return Results.BadRequest(new ErrorResponse("text must not be empty."));

				var channel = channelRegistry.GetByName(match.ChatChannelName);
				if (channel is null) return Results.NotFound(new ErrorResponse("Match chat channel not found."));

				var sent = chatDispatch.SendAsBot(channel, body.Text);
				return sent == 0
					? Results.Json(new ErrorResponse("BasilBot is not online."),
						statusCode: StatusCodes.Status503ServiceUnavailable)
					: Results.Json(new MatchChatSentView(sent));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("sendMatchChat")
			.WithSummary("Say something in a match's chat as BasilBot.")
			.WithDescription("""
			                 Body: `{ text }`. Everyone in the room sees it as an ordinary message from
			                 BasilBot, and it appears on `GET /matches/{matchId}/chat/live` like any other line.

			                 Nothing is ever truncated: the text is split into one message per newline, and any
			                 line still too long for a single chat message is wrapped at a word boundary into as
			                 many messages as it takes. Blank lines are dropped. The response reports how many
			                 messages the text became.

			                 Returns `400 Bad Request` for empty text, `404 Not Found` if the match isn't
			                 currently live, and `503 Service Unavailable` if BasilBot is not online.
			                 """ + AdminKeyNote)
			.WithTags("Match Chat")
			.Produces<MatchChatSentView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
			.WithExample(StatusCodes.Status200OK, new MatchChatSentView(2))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("text must not be empty."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}
}

/// <summary>Request body for `POST /matches/{matchId}/chat`.</summary>
/// <param name="Text">The text BasilBot says in the room.</param>
public sealed record SendMatchChatRequest(string Text);

/// <summary>Confirmation body for `POST /matches/{matchId}/chat`.</summary>
/// <param name="DeliveredCount">The number of chat messages the text became.</param>
public sealed record MatchChatSentView(int DeliveredCount);