using Basil.Common.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Common.Infrastructure;

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