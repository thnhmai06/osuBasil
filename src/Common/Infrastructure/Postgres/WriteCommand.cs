using Microsoft.EntityFrameworkCore;

namespace Basil.Common.Infrastructure.Postgres;

/// <summary>One change to store, applied to a context; it works on values copied when the change was made.</summary>
/// <param name="Operation">Applies the change to the context.</param>
public sealed record WriteCommand<TContext>(Func<TContext, CancellationToken, Task> Operation) : IDatabaseCommand
	where TContext : DbContext, IDatabaseContext<TContext>, IWriteTicketContext
{
	/// <inheritdoc />
	public DbContext CreateContext(Database database, bool confirmed)
	{
		return confirmed
			? database.CreateConfirmedContext<TContext>()
			: database.CreateUnconfirmedContext<TContext>();
	}

	/// <inheritdoc />
	public DbSet<WriteTicketRow> Tickets(DbContext context)
	{
		return ((IWriteTicketContext)context).WriteTickets;
	}

	/// <inheritdoc />
	public Task RunAsync(DbContext context, CancellationToken cancellationToken)
	{
		return Operation((TContext)context, cancellationToken);
	}
}