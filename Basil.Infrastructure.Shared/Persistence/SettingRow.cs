using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Shared.Persistence;

internal sealed class SettingRow
{
	public string Name { get; set; } = "";

	public string? Value { get; set; }
}

internal sealed class SettingRowConfiguration : IEntityTypeConfiguration<SettingRow>
{
	public void Configure(EntityTypeBuilder<SettingRow> builder)
	{
		builder.ToTable("settings");
		builder.HasKey(row => row.Name);
		builder.Property(row => row.Name).HasMaxLength(255);
	}
}