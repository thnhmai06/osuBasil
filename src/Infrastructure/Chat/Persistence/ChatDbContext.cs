using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Chat.Persistence;

internal sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options),
	IDatabaseContext<ChatDbContext>
{
	/// <summary>The stored channels.</summary>
	public DbSet<ChannelRow> Channels => Set<ChannelRow>();

	static string IDatabaseContext<ChatDbContext>.HistoryTable => "__ef_migrations_chat";

	static ChatDbContext IDatabaseContext<ChatDbContext>.Create(DbContextOptions<ChatDbContext> options)
	{
		return new ChatDbContext(options);
	}

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<ChatDbContext>(modelBuilder);
	}
}