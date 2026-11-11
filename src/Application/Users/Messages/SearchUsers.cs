using Basil.Application.Shared;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users for a page of the accounts a query includes.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Query">Which accounts to include.</param>
/// <param name="Page">Which part of the listing to return.</param>
/// <remarks>Deleted accounts are included only when the actor may see them.</remarks>
public sealed record SearchUsers(Connection Actor, UserQuery Query, PageRequest Page) : IRequest<Page<UserRecord>>;