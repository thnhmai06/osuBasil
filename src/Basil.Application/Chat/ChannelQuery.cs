using Basil.Domain.Chat;
using Basil.Domain.Client;

using Basil.Application.Common.Queries;
namespace Basil.Application.Chat;

public sealed record ChannelQuery(
	IReadOnlyCollection<string>? Name = null,
	IReadOnlyCollection<string>? DisplayName = null,
	ClientPrivileges? ReadPrivileges = null,
	ClientPrivileges? WritePrivileges = null,
	bool OnlyVisible = true,
	bool OnlyAutoJoin = false) : Query<IChannel>;