-- SafeName is derived from Name by a fixed rule, so the database owns it rather than every insert
-- and update path remembering to recompute it. replace(lower(Name), ' ', '_') reproduces
-- User.MakeSafeName exactly for every username ValidateUsername admits, which is ASCII-only;
-- SQLite's lower() is ASCII-only too, so the two agree wherever they can both be reached.
--
-- SilenceEnd becomes nullable in the same rebuild rather than in a second one: "never silenced"
-- is naturally NULL, not a sentinel epoch, and the table can only be rebuilt once cheaply.
--
-- A rebuild is required because SafeName already exists as a real column carrying a UNIQUE index,
-- and SQLite refuses DROP COLUMN on an indexed column.
PRAGMA foreign_keys = off;

create table Users_new
(
	Id         INTEGER PRIMARY KEY AUTOINCREMENT,
	Name       varchar(32)                                                        not null,
	SafeName   varchar(32) generated always as (replace(lower(Name), ' ', '_')) stored,
	Privilege  int      default 1                                                 not null,
	PwBcrypt   char(60)                                                           not null,
	Country    char(2)  default 'xx'                                              not null,
	SilenceEnd datetime                                                           null,
	DeletedAt  datetime                                                           null,
	constraint Users_Name_uindex unique (Name),
	constraint Users_SafeName_uindex unique (SafeName)
);

insert into Users_new (Id, Name, Privilege, PwBcrypt, Country, SilenceEnd, DeletedAt)
select Id, Name, Privilege, PwBcrypt, Country,
       case when SilenceEnd is null or SilenceEnd <= '1970-01-01 00:00:00' then null else SilenceEnd end,
       DeletedAt
from Users;

drop table Users;
alter table Users_new rename to Users;
create index Users_Privilege_index on Users (Privilege);

PRAGMA foreign_keys = on;
