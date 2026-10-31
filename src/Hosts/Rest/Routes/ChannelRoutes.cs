using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;

namespace Basil.Host.Rest.Routes;

/// <summary>Management of the general channels; chat itself is not part of this API.</summary>
internal static class ChannelRoutes
{
	public static void MapChannels(this RouteGroupBuilder v1)
	{
		var channels = v1.MapGroup("/channels").WithTags("Channels").RequireToken();
		channels.MapGet("/", List).WithSummary("List the general channels the caller may see.")
			.WithDescription(
				"Channels the caller cannot read are not listed. Hidden channels are listed for those who manage channels.");
		channels.MapPost("/", CreateAsync).WithSummary("Create a general channel.");
		channels.MapPatch("/{name}", PatchAsync).WithSummary("Change a general channel.")
			.WithDescription(
				"A caller who may not manage channels is refused with 403 whether or not the channel exists; 404 means no channel has the name.");
		channels.MapDelete("/{name}", DeleteAsync).WithSummary("Delete a general channel.")
			.WithDescription(
				"A caller who may not manage channels is refused with 403 whether or not the channel exists; 404 means no channel has the name.");
	}

	private static IResult List(HttpContext context, GeneralChannelRegistry registry)
	{
		return Api.Ok(registry.VisibleTo(context.Connection()).OrderBy(c => c.Name)
			.Select(c => ChannelView.Of(c.Record)).ToList());
	}

	private static async Task<IResult> CreateAsync(HttpContext context, CreateChannelRequest request, Server server)
	{
		if (string.IsNullOrWhiteSpace(request.Name)) return Api.Invalid("name is required.");

		GeneralChannelRecord record;
		try
		{
			record = new GeneralChannelRecord
			{
				Name = request.Name,
				Value = new GeneralChannelData
				{
					Topic = request.Topic ?? string.Empty,
					ReadPrivileges = (UserPrivileges)(request.ReadPrivileges ?? 0),
					WritePrivileges = (UserPrivileges)(request.WritePrivileges ?? 0),
					AutoJoin = request.AutoJoin ?? false,
					Visible = request.Visible ?? true
				}
			};
		}
		catch (ArgumentException)
		{
			return Api.Invalid("The channel is not valid.");
		}

		var result = await server.ScopeTo(context.Connection()).Channels.CreateAsync(record, context.RequestAborted);
		return result.IsOk ? Api.Created(ChannelView.Of(record)) : result.Refusal();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, string name, PatchChannelRequest request,
		Server server)
	{
		var (found, channel) = server.ScopeTo(context.Connection()).Channels.Find(name);
		if (channel is null) return found.Refusal();

		try
		{
			var result = await channel.ScopeTo(context.Connection()).General!.ConfigureAsync(new GeneralChannelChange(
				request.Topic,
				(UserPrivileges?)request.ReadPrivileges, (UserPrivileges?)request.WritePrivileges, request.AutoJoin,
				request.Visible), context.RequestAborted);
			return result.IsOk ? Api.Ok(ChannelView.Of(channel.Record), "Updated successfully") : result.Refusal();
		}
		catch (ArgumentException)
		{
			return Api.Invalid("The change is not valid.");
		}
	}

	private static async Task<IResult> DeleteAsync(HttpContext context, string name, Server server)
	{
		var (found, channel) = server.ScopeTo(context.Connection()).Channels.Find(name);
		return channel is null
			? found.Refusal()
			: (await channel.ScopeTo(context.Connection()).General!.DeleteAsync(context.RequestAborted)).Answer();
	}

	/// <summary>A new general channel.</summary>
	public sealed record CreateChannelRequest(
		string? Name,
		string? Topic,
		byte? ReadPrivileges,
		byte? WritePrivileges,
		bool? AutoJoin,
		bool? Visible);

	/// <summary>The fields of a channel that can change; absent fields stay as they are.</summary>
	public sealed record PatchChannelRequest(
		string? Topic,
		byte? ReadPrivileges,
		byte? WritePrivileges,
		bool? AutoJoin,
		bool? Visible);
}