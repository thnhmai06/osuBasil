-- Beatmaps.Frozen renamed to IsPrivate: same "hidden unless admin" meaning, just a clearer name now
-- that Mapsets gets its own, differently-meaning IsFrozen below (a write-lock, not a visibility flag).
alter table Beatmaps
    rename column Frozen to IsPrivate;

-- A write-lock toggled via PATCH /mapset/{id}/freeze on the api. host: blocks PUT/DELETE on the
-- mapset (409) regardless of admin role, while the freeze toggle itself stays exempt from its own lock.
alter table Mapsets
    add column IsFrozen boolean default false not null;
