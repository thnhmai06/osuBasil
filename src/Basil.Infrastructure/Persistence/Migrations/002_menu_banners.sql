-- MenuBanners: main-menu promotional banners (assets.<domain>/menu-content.json). Image is either a
-- locally stored filename (Data/Menu/Banners/) or an external http(s) URL, resolved to a full
-- assets.<domain> URL only when it's a local filename. No privacy flag: every banner is public.
create table MenuBanners
(
	Id        INTEGER PRIMARY KEY AUTOINCREMENT,
	Image     text          not null,
	Url       varchar(2048) not null,
	Begins    datetime      not null,
	Expires   datetime      not null,
	CreatedAt datetime      not null
);
