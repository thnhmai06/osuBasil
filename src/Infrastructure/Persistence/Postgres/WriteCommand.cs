using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>One change to store, applied to a context; it works on values copied when the change was made.</summary>
/// <param name="Operation">Applies the change to the context.</param>
internal sealed record WriteCommand<TContext>(Func<TContext, CancellationToken, Task> Operation) : IDatabaseCommand
	where TContext : DbContext, IDatabaseContext<TContext>, IWriteTicketContext
{
	public DbContext CreateContext(Database database, bool confirmed)
	{
		return confirmed
			? database.CreateConfirmedContext<TContext>()
			: database.CreateUnconfirmedContext<TContext>();
	}

	public DbSet<WriteTicketRow> Tickets(DbContext context)
	{
		return ((IWriteTicketContext)context).WriteTickets;
	}

	public Task RunAsync(DbContext context, CancellationToken cancellationToken)
	{
		return Operation((TContext)context, cancellationToken);
	}
}