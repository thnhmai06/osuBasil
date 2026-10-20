using System.Threading.Channels;
using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Chat;

/// <summary>Opens general channels and manages automatic channel membership.</summary>
internal sealed class ChannelService(ChannelContext context, ChannelEventStream events) : IChannelService
{
	/// <inheritdoc />
	public ChannelReader<ChannelEvent> Events => events.Reader;

	/// <inheritdoc />
	public GeneralChannelSession? Open(GeneralChannel channel)
	{
		var session = new GeneralChannelSession(channel, context);
		if (!context.Registry.Add(session)) return null;
		context.Events.Emit(new ChannelOpened(session));
		return session;
	}

	/// <inheritdoc />
	public async Task<ChannelManageResult> CreateAsync(GeneralChannel channel, Connection by,
		CancellationToken cancellationToken = default)
	{
		if (by.Session.Check(Permissions.ServerConfigure, context.Clock.GetUtcNow()) is not Access.Allowed)
			return ChannelManageResult.NotAuthorized;
		if (Open(channel) is null) return ChannelManageResult.NameTaken;
		await context.Channels.CreateOrUpdateAsync(channel, cancellationToken);
		return ChannelManageResult.Ok;
	}

	/// <inheritdoc />
	public void JoinAutoChannels(Connection by)
	{
		foreach (var channel in context.Registry.All.Values)
			if (channel.Channel.AutoJoin)
				channel.ScopeTo(by).Join();
	}

	/// <inheritdoc />
	public void PartAll(Connection by)
	{
		foreach (var channel in context.Registry.All.Values)
			channel.ScopeTo(by).Part();
	}
}
