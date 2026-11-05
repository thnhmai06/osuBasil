using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ListBlocks" /> on the account it names.</summary>
internal sealed class ListBlocksHandler(UserRegistry registry)
	: IRequestHandler<ListBlocks, (Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)> Handle(ListBlocks request,
		CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Relationships.BlocksAsync(cancellationToken);
	}
}