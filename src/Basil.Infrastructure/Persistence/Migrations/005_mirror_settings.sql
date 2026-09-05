-- Mirror:DownloadEndpoint/Mirror:SearchEndpoint back MirrorService (the beatmap mirror endpoints,
-- moved from appsettings.json to this table so they're mutable at runtime). Mirror:Seeded is a
-- one-time marker: MirrorService.SeedFromConfigIfUnsetAsync sets it after its first run so a later
-- operator clear of both endpoints doesn't get silently re-seeded from appsettings.json on the next
-- restart. All three seeded null: SqliteSettingsRepository.SetAsync is UPDATE-only, so every key it
-- ever writes must already have a row here.
insert into Settings (Key, Value)
values ('Mirror:DownloadEndpoint', null),
       ('Mirror:SearchEndpoint', null),
       ('Mirror:Seeded', null);
