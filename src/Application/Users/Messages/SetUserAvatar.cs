using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to set the avatar of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Image">The uploaded image.</param>
public sealed record SetUserAvatar(Connection Actor, UserReference Target, Stream Image) : IRequest<UserOutcome>;