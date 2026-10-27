using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>A module's context: the name of its migration history table and how the shared database creates it.</summary>
/// <typeparam name="TContext">The context type itself.</typeparam>
internal interface IDatabaseContext<TContext> where TContext : DbContext
{
	/// <summary>Gets the table in which this module records the migrations it applied.</summary>
	static abstract string HistoryTable { get; }

	/// <summary>Creates the context over options that the shared database prepared.</summary>
	/// <param name="options">The options, including the connection and the history table.</param>
	/// <returns>A new, short-lived context.</returns>
	static abstract TContext Create(DbContextOptions<TContext> options);
}

/// <summary>A context that can store the tickets with which a write proves afterwards that it was committed.</summary>
internal interface IWriteTicketContext
{
	/// <summary>Gets the tickets of committed writes.</summary>
	DbSet<WriteTicketRow> WriteTickets { get; }
}

/// <summary>One change to store, together with the context it runs on.</summary>
internal interface IDatabaseCommand
{
	/// <summary>Creates the context the change runs on.</summary>
	/// <param name="database">The shared database.</param>
	/// <param name="confirmed">Whether commits wait for the database to flush them.</param>
	/// <returns>A new context of the module the change belongs to.</returns>
	DbContext CreateContext(Database database, bool confirmed);

	/// <summary>Gets the tickets of the context.</summary>
	/// <param name="context">A context created by <see cref="CreateContext" />.</param>
	/// <returns>The tickets that prove a committed write.</returns>
	DbSet<WriteTicketRow> Tickets(DbContext context);

	/// <summary>Applies the change to the context.</summary>
	/// <param name="context">A context created by <see cref="CreateContext" />.</param>
	/// <param name="cancellationToken">A token that cancels the change.</param>
	Task RunAsync(DbContext context, CancellationToken cancellationToken);
}