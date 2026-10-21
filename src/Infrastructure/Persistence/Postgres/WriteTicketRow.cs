using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>
///     A stored row of the <c>write_tickets</c> table: the mark a write leaves in its own transaction, so that when a
///     connection drops while the transaction commits, the row tells whether it did.
/// </summary>
internal sealed class WriteTicketRow
{
	public Guid Id { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class WriteTicketRowConfiguration : IEntityTypeConfiguration<WriteTicketRow>
{
	public void Configure(EntityTypeBuilder<WriteTicketRow> builder)
	{
		builder.ToTable("write_tickets");
		builder.HasKey(row => row.Id);
		builder.Property(row => row.Id).ValueGeneratedNever();
		builder.HasIndex(row => row.CreatedAt);
	}
}
