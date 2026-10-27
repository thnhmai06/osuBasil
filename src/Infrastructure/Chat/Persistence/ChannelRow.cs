using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Chat.Persistence;

/// <summary>A stored row of the <c>channels</c> table.</summary>
internal sealed class ChannelRow
{
	public string Name { get; set; } = "";
	public string Topic { get; set; } = "";
	public int ReadPrivileges { get; set; }
	public int WritePrivileges { get; set; }
	public bool AutoJoin { get; set; }
	public bool Visible { get; set; }
}

internal sealed class ChannelRowConfiguration : IEntityTypeConfiguration<ChannelRow>
{
	public void Configure(EntityTypeBuilder<ChannelRow> builder)
	{
		builder.ToTable("channels");
		builder.HasKey(row => row.Name);
		builder.HasData(
			new ChannelRow { Name = "#osu", Topic = "General discussion.", AutoJoin = true, Visible = true },
			new ChannelRow { Name = "#lobby", Topic = "Multiplayer lobby discussion.", Visible = true });
	}
}