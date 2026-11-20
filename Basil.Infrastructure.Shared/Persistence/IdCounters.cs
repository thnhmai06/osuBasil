using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public sealed class IdCounterRow
{
	public string Name { get; set; } = "";

	public long Next { get; set; }
}

public static class IdCounters
{
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

	public static async Task<long> NextAsync(DbContext context, string name)
	{
		var counters = context.Set<IdCounterRow>();
		while (true)
		{
			var next = await counters.Where(row => row.Name == name).Select(row => row.Next)
				.SingleAsync();
			// The update only counts when nobody took this id in between; otherwise read again.
			var taken = await counters.Where(row => row.Name == name && row.Next == next)
				.ExecuteUpdateAsync(set => set.SetProperty(row => row.Next, next + 1));
			if (taken == 1) return next;
		}
	}
}