-- MenuBanners: main-menu promotional banners (assets.<domain>/menu-content.json). Source is either a
-- locally stored filename (Data/Menu/Banners/) or an external http(s) URL, resolved to a full
-- assets.<domain> URL only when it's a local filename. No privacy flag: every banner is public.
-- Begins/Expires are each optional: a null Begins means no lower bound (already current), a null
-- Expires means no upper bound (never expires), and both null means the banner is always current.
create table MenuBanners
(
	Id        INTEGER PRIMARY KEY AUTOINCREMENT,
	Source    text          not null,
	Url       varchar(2048) not null,
	Begins    datetime      null,
	Expires   datetime      null,
	CreatedAt datetime      not null
);
