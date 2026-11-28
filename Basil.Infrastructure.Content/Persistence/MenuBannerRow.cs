using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Basil.Infrastructure.Content.Persistence;

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
		builder.Property(row => row.Image).HasMaxLength(255);
		builder.HasData(new MenuBannerRow
		{
			Image = "Menu/Banners/default-banner@2x.png",
			Url = "https://github.com/thnhmai06/osuBasil",
			CreatedAt = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero)
		});
	}
}