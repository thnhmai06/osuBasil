using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>A named counter that hands out the next id of a table whose ids the database does not number.</summary>
public sealed class IdCounterRow
{
	/// <summary>Gets or sets the name of the counter.</summary>
	public string Name { get; set; } = "";

	/// <summary>Gets or sets the id the counter hands out next.</summary>
	public long Next { get; set; }
}

/// <summary>Numbers rows with counters stored in an ordinary table, so that every database provider numbers them alike.</summary>
public static class IdCounters
{
	/// <summary>Maps the table of the counters of a module and starts each counter at its first id.</summary>
	/// <param name="modelBuilder">The model being built.</param>
	/// <param name="table">The name of the table, which no other module uses.</param>
	/// <param name="counters">The counters and the first id each one hands out.</param>
	public static void Map(ModelBuilder modelBuilder, string table, params (string Name, long First)[] counters)
	{
		modelBuilder.Entity<IdCounterRow>(entity =>
		{
			entity.ToTable(table);
			entity.HasKey(row => row.Name);
			entity.Property(row => row.Name).HasMaxLength(64);
			entity.HasData(counters.Select(counter => new IdCounterRow { Name = counter.Name, Next = counter.First }));
		});
	}

	/// <summary>Takes the next id of a counter; two callers never take the same id.</summary>
	/// <param name="context">The context of the module that owns the counter.</param>
	/// <param name="name">The name of the counter.</param>
	/// <param name="cancellationToken">A token that cancels the call.</param>
	/// <returns>The id, which the counter will not hand out again.</returns>
	public static async Task<long> NextAsync(DbContext context, string name, CancellationToken cancellationToken)
	{
		var counters = context.Set<IdCounterRow>();
		while (true)
		{
			var next = await counters.Where(row => row.Name == name).Select(row => row.Next)
				.SingleAsync(cancellationToken);
			// The update only counts when nobody took this id in between; otherwise read again.
			var taken = await counters.Where(row => row.Name == name && row.Next == next)
				.ExecuteUpdateAsync(set => set.SetProperty(row => row.Next, next + 1), cancellationToken);
			if (taken == 1) return next;
		}
	}
}