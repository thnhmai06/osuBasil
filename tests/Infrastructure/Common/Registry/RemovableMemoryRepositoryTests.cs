using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Registry;
using Basil.Testing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Tests.Registry;

/// <summary>Contract tests of a memory repository whose items can be deleted.</summary>
[Collection("Persistence")]
public sealed class RemovableMemoryRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	[Fact]
	public async Task A_deletion_that_failed_does_not_bring_the_item_back_into_memory()
	{
		var repository = new Probe(Get<DatabaseReader>(), Get<DatabaseWriter>());

		await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RemoveAsync("7"));
		var found = await repository.FindItemAsync(7);

		Assert.Null(found);
	}

	private sealed class Probe(DatabaseReader reader, DatabaseWriter writer)
		: RemovableMemoryRepository<int, string>(reader, writer)
	{
		public new Task RemoveAsync(string item)
		{
			return base.RemoveAsync(item);
		}

		public async Task<string?> FindItemAsync(int key)
		{
			return await FindAsync(key, CancellationToken.None);
		}

		protected override int KeyOf(string item)
		{
			return int.Parse(item);
		}

		protected override Root RootOf(string item)
		{
			return Root.Server;
		}

		// The row is still stored, so a repository that forgot the deletion would load the item again.
		protected override Task<string?> LoadAsync(int key)
		{
			return Task.FromResult<string?>(key.ToString());
		}

		protected override IDatabaseCommand SaveCommand(string item)
		{
			throw new NotSupportedException();
		}

		protected override IDatabaseCommand EraseCommand(int key)
		{
			return new FailingCommand();
		}
	}

	private sealed class FailingCommand : IDatabaseCommand
	{
		public DbContext CreateContext(Database database, bool confirmed)
		{
			throw new InvalidOperationException("The deletion cannot be stored.");
		}

		public DbSet<WriteTicketRow> Tickets(DbContext context)
		{
			throw new NotSupportedException();
		}

		public Task RunAsync(DbContext context, CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}
	}
}