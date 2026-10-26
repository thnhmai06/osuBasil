using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Persistence.Content;

/// <summary>A stored row of the <c>menu_banners</c> table.</summary>
internal sealed class MenuBannerRow
{
	public string Image { get; set; } = "";
	public string Url { get; set; } = "";
	public DateTimeOffset? StartsAt { get; set; }
	public DateTimeOffset? EndsAt { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class MenuBannerRowConfiguration : IEntityTypeConfiguration<MenuBannerRow>
{
	public void Configure(EntityTypeBuilder<MenuBannerRow> builder)
	{
		builder.ToTable("menu_banners");
		builder.HasKey(row => row.Image);
	}
}