using Basil.Application.Shared;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ListBlocks" /> on the account it names.</summary>
internal sealed class ListBlocksHandler(UserRegistry registry)
	: IRequestHandler<ListBlocks, (UserOutcome Result, IReadOnlyList<UserRecord> Users)>
{
	/// <inheritdoc />
	public async ValueTask<(UserOutcome Result, IReadOnlyList<UserRecord> Users)> Handle(ListBlocks request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Relationships.BlocksAsync(ct);
	}
}