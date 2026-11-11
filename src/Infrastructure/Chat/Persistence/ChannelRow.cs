using Basil.Application.Chat.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Chat.Persistence;

/// <summary>A stored row of the <c>channels</c> table.</summary>
internal sealed class ChannelRow
{
	/// <summary>The channel name.</summary>
	public string Name { get; set; } = "";

	/// <summary>The channel topic.</summary>
	public string Topic { get; set; } = "";

	/// <summary>The privileges needed to read the channel.</summary>
	public int ReadPrivileges { get; set; }

	/// <summary>The privileges needed to write to the channel.</summary>
	public int WritePrivileges { get; set; }

	/// <summary>Whether players join the channel on sign-in.</summary>
	public bool AutoJoin { get; set; }

	/// <summary>Whether the channel is listed to players.</summary>
	public bool Visible { get; set; }
}

internal sealed class ChannelRowConfiguration : IEntityTypeConfiguration<ChannelRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<ChannelRow> builder)
	{
		builder.ToTable("channels");
		builder.HasKey(row => row.Name);
		builder.Property(row => row.Name).HasMaxLength(255);
		builder.HasData(
			new ChannelRow { Name = "#osu", Topic = "General discussion.", AutoJoin = true, Visible = true },
			new ChannelRow
				{ Name = GeneralChannel.LobbyName, Topic = "Multiplayer lobby discussion.", Visible = true });
	}
}