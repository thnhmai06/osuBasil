using Basil.Domain.Chat;
using Basil.Domain.Client;

namespace Basil.Application.Queries;

public sealed record ChannelQuery(
	IReadOnlyCollection<string>? Name = null,
	IReadOnlyCollection<string>? DisplayName = null,
	ClientPrivileges? ReadPrivileges = null,
	ClientPrivileges? WritePrivileges = null,
	bool OnlyVisible = true,
	bool OnlyAutoJoin = false) : IQuery<IChannel>;