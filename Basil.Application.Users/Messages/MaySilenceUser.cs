using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks whether an actor may silence accounts, so a refused silence is not read at all.</summary>
public sealed record MaySilenceUser(IActor Actor, int TargetId) : IRequest<UserActionResult>;
