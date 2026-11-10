using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Content.Persistence;

/// <summary>A stored row of the <c>settings</c> table: one server setting; a setting without a row keeps its default.</summary>
internal sealed class SettingRow
{
	/// <summary>The setting name.</summary>
	public string Name { get; set; } = "";
	/// <summary>The stored value of the setting, if set.</summary>
	public string? Value { get; set; }
}

internal sealed class SettingRowConfiguration : IEntityTypeConfiguration<SettingRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<SettingRow> builder)
	{
		builder.ToTable("settings");
		builder.HasKey(row => row.Name);
		builder.Property(row => row.Name).HasMaxLength(255);
	}
}