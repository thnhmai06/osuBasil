using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public interface IDatabaseContext<TContext> where TContext : DbContext
{
	static abstract string HistoryTable { get; }

	static abstract TContext Create(DbContextOptions<TContext> options);
}