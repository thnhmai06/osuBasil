-- Names are unique among live users instead of forever; permission bits for score submission, moderation
-- notes and login history; the fixed BasilBot account.
-- Conventions: snake_case names; ids from identity columns or sequences; times are timestamptz in UTC.

-- a. Uniqueness moves from a stored column to the normalized name of every user who is not deleted, so a
-- deleted account frees its name at once.
alter table users drop constraint users_safe_name_key;
drop index users_safe_name_trgm;
alter table users drop column safe_name;

create unique index users_name_live on users ((lower(replace(name, ' ', '_')))) where deleted_at is null;
create index users_name_trgm on users using gin ((lower(replace(name, ' ', '_'))) gin_trgm_ops);

-- b. Permission bits: ScoreSubmit is 7, UserNote is 30, UserViewLogins is 29. Whoever could create rooms
-- was a player, a moderator (24) may add notes, and whoever may restrict (25) may view login history.
update users set permissions = permissions | (1::bigint << 7) where (permissions & 1) <> 0;
update users set permissions = permissions | (1::bigint << 30) where (permissions & (1::bigint << 24)) <> 0;
update users set permissions = permissions | (1::bigint << 29) where (permissions & (1::bigint << 25)) <> 0;

-- Old silence restrictions (join, chat and private messages) become the new silence: chatting anywhere and
-- taking part in multiplayer, whose area holds RoomCreate (0) and RoomCreateUnlimited (20).
update restrictions
set permissions = permissions | (1::bigint << 0) | (1::bigint << 20)
where (permissions & 14) = 14;

-- c. BasilBot: the fixed user the server speaks as. It holds no permission and has no credentials row, so
-- it cannot sign in.
do $$
begin
	if exists (select 1 from users where id = 1 and name <> 'BasilBot') then
		raise exception 'user id 1 is taken by an account not named BasilBot';
	end if;
end $$;

insert into users (id, name, country, permissions, deleted_at) overriding system value
select 1, 'BasilBot', 244, 0, null
where not exists (select 1 from users where id = 1);

select setval(pg_get_serial_sequence('users', 'id'), greatest((select max(id) from users), 1));
