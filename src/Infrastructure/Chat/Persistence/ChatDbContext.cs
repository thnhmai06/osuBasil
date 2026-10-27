using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Chat.Persistence;

internal sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options),
	IDatabaseContext<ChatDbContext>, IWriteTicketContext
{
	public DbSet<ChannelRow> Channels => Set<ChannelRow>();
	public DbSet<WriteTicketRow> WriteTickets => Set<WriteTicketRow>();

	static string IDatabaseContext<ChatDbContext>.HistoryTable => "__ef_migrations_chat";

	static ChatDbContext IDatabaseContext<ChatDbContext>.Create(DbContextOptions<ChatDbContext> options) =>
		new(options);

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<ChatDbContext>(modelBuilder);
		ContextModel.MapWriteTicketReference(modelBuilder);
	}
}