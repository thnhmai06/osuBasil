using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Content.Persistence;

/// <summary>A stored row of the <c>menu_banners</c> table.</summary>
internal sealed class MenuBannerRow
{
	/// <summary>The image shown on the banner.</summary>
	public string Image { get; set; } = "";

	/// <summary>The address the banner opens.</summary>
	public string Url { get; set; } = "";

	/// <summary>When the banner starts showing, or <see langword="null" /> when it has no start.</summary>
	public DateTimeOffset? StartsAt { get; set; }

	/// <summary>When the banner stops showing, or <see langword="null" /> when it has no end.</summary>
	public DateTimeOffset? EndsAt { get; set; }

	/// <summary>When the banner was created.</summary>
	public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class MenuBannerRowConfiguration : IEntityTypeConfiguration<MenuBannerRow>
{
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<MenuBannerRow> builder)
	{
		builder.ToTable("menu_banners");
		builder.HasKey(row => row.Image);
		builder.Property(row => row.Image).HasMaxLength(255);
	}
}