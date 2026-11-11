using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

/// <summary>A module's context: the name of its migration history table and how the shared database creates it.</summary>
/// <typeparam name="TContext">The context type itself.</typeparam>
public interface IDatabaseContext<TContext> where TContext : DbContext
{
	/// <summary>Gets the table in which this module records the migrations it applied.</summary>
	static abstract string HistoryTable { get; }

	/// <summary>Creates the context over options that the shared database prepared.</summary>
	/// <param name="options">The options, including the connection and the history table.</param>
	/// <returns>A new, short-lived context.</returns>
	static abstract TContext Create(DbContextOptions<TContext> options);
}