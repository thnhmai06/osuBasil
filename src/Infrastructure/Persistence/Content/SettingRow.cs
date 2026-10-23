using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Content;

/// <summary>A stored row of the <c>settings</c> table: one server setting; a setting without a row keeps its default.</summary>
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
	}
}
