using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Servers.Messages.Channels;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Routes;

internal static class ChannelRoutes
{
	public static void MapChannels(this RouteGroupBuilder v1)
	{
		var channels = v1.MapGroup("/channels").WithTags("Channels").RequireToken();
		channels.MapGet("/", List).Admitted(WorkClass.Listing)
			.ReturnsList<ChannelView>().WithSummary("List general channels.")
			.WithDescription(
				"Channels the caller cannot read are not listed. Hidden channels are listed for those who manage channels.");
		channels.MapPost("/", CreateAsync).Takes<CreateChannelRequest>().Returns<ChannelView>(201).WithSummary("Create a general channel.")
			.WithDescription(
				"For those who manage channels. A channel whose name is already in use is refused with 409.");
		channels.MapPatch("/{name}", PatchAsync).Takes<PatchChannelRequest>().Returns<ChannelView>().WithSummary("Update a general channel.")
			.WithDescription(
				"A caller who may not manage channels is refused with 403 whether or not the channel exists; 404 means no channel has the name.");
		channels.MapDelete("/{name}", DeleteAsync).WithSummary("Delete a general channel.")
			.WithDescription(
				"A caller who may not manage channels is refused with 403 whether or not the channel exists; 404 means no channel has the name.");
	}

	private static async Task<IResult> List(HttpContext context, ISender sender)
	{
		return Api.Ok((await sender.Send(new ListChannels(context.Session()), context.RequestAborted))
			.Select(ChannelView.Of).ToList());
	}

	private static async Task<IResult> CreateAsync(HttpContext context, ISender sender)
	{
		var (request, refused) = await context.ReadBodyAsync<CreateChannelRequest>();
		if (refused is not null) return refused;

		if (string.IsNullOrWhiteSpace(request!.Name)) return Api.Invalid("name is required.");

		var record = new GeneralChannelRecord
		{
			Name = request.Name,
			Value = GeneralChannelData.Of(request.Topic, request.ReadPrivileges, request.WritePrivileges,
				request.AutoJoin, request.Visible)
		};

		var result = await sender.Send(new CreateGeneralChannel(context.Session(), record), context.RequestAborted);
		return result == ServerActionResult.Ok ? Api.Created(ChannelView.Of(record)) : result.Refusal();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, string name, ISender sender)
	{
		var (request, refused) = await context.ReadBodyAsync<PatchChannelRequest>();
		if (refused is not null) return refused;

		var change = new GeneralChannelChange(request!.Topic,
			request.ReadPrivileges, request.WritePrivileges, request.AutoJoin,
			request.Visible);

		var (result, channel) = await sender.Send(new ConfigureGeneralChannel(context.Session(), name, change),
			context.RequestAborted);
		return result == ChannelJoinResult.Ok
			? Api.Ok(ChannelView.Of(channel!.Record), "Updated successfully")
			: result.Refusal();
	}

	private static async Task<IResult> DeleteAsync(HttpContext context, string name, ISender sender)
	{
		var (result, _) = await sender.Send(new DeleteGeneralChannel(context.Session(), name),
			context.RequestAborted);
		return result.Answer();
	}

	public sealed record CreateChannelRequest(
		string? Name,
		string? Topic,
		UserPrivileges? ReadPrivileges,
		UserPrivileges? WritePrivileges,
		bool? AutoJoin,
		bool? Visible);

	public sealed record PatchChannelRequest(
		string? Topic,
		UserPrivileges? ReadPrivileges,
		UserPrivileges? WritePrivileges,
		bool? AutoJoin,
		bool? Visible);
}