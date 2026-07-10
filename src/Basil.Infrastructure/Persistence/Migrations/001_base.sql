-- Basil schema (SQLite). PascalCase tables/columns, ids autoincrement from 1.
-- Scope: multiplayer/tournament only. Cut vs. upstream bancho.py: Clans, Achievements,
-- UserAchievements, Comments, Favourites, MapRequests, PerformanceReports, Startups (no consumer
-- anywhere in the codebase). Kept: Ratings (consumed by BeatmapLeaderboardService), Mail/
-- Relationships (social), ClientHashes/IngameLogins/Logs (anticheat log-only). New: Matches/Rounds
-- (1 multiplayer room = 1 Match, 1 beatmap played within it = 1 Round), Scores repurposed to link
-- to a Round instead of standing alone.

create table Users
(
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            varchar(32)          not null,
    SafeName        varchar(32)          not null,
    Email           varchar(254)         not null,
    Priv            int     default 1    not null,
    PwBcrypt        char(60)             not null,
    Country         char(2) default 'xx' not null,
    SilenceEnd      int     default 0    not null,
    DonorEnd        int     default 0    not null,
    CreationTime    int     default 0    not null,
    LatestActivity  int     default 0    not null,
    ClanId          int     default 0    not null,
    ClanPriv        int     default 0    not null,
    PreferredMode   int     default 0    not null,
    PlayStyle       int     default 0    not null,
    CustomBadgeName varchar(16) null,
    CustomBadgeIcon varchar(64) null,
    UserpageContent varchar(2048) null,
    constraint Users_Email_uindex unique (Email),
    constraint Users_Name_uindex unique (Name),
    constraint Users_SafeName_uindex unique (SafeName)
);
create index Users_Priv_index on Users (Priv);

-- Fixed gameplay stats: seeded once with default values, never UPDATEd on score submission
-- (server does not track singleplayer ranking/progression).
create table UserStats
(
    Id          int not null,
    Mode        int not null,
    Tscore      bigint  default 0     not null,
    Rscore      bigint  default 0     not null,
    Plays       int     default 0     not null,
    Playtime    int     default 0     not null,
    Acc         float(6, 3) default 0.000 not null,
    MaxCombo    int     default 0     not null,
    TotalHits   int     default 0     not null,
    ReplayViews int     default 0     not null,
    XhCount     int     default 0     not null,
    XCount      int     default 0     not null,
    ShCount     int     default 0     not null,
    SCount      int     default 0     not null,
    ACount      int     default 0     not null,
    primary key (Id, Mode),
    constraint UserStats_Users_Id_fk foreign key (Id) references Users (Id)
);

-- One row per beatmap set. No osu!api staleness tracking (server runs fully offline; sets are
-- only ever added via local ingestion, see BeatmapIngestionService).
create table Mapsets
(
    Id int not null primary key
);

create table Beatmaps
(
    Id          int                   not null primary key,
    SetId       int                   not null,
    Md5         char(32)              not null,
    Artist      varchar(128)          not null,
    Title       varchar(128)          not null,
    Version     varchar(128)          not null,
    Creator     varchar(19)           not null,
    Filename    varchar(256)          not null,
    LastUpdate  datetime              not null,
    TotalLength int                   not null,
    MaxCombo    int                   not null,
    Status      int                   not null,
    Frozen      boolean default false not null,
    Plays       int     default 0     not null,
    Passes      int     default 0     not null,
    Mode        int                   not null,
    Bpm         float(12, 2) default 0.00 not null,
    Cs          float(4, 2)  default 0.00 not null,
    Ar          float(4, 2)  default 0.00 not null,
    Od          float(4, 2)  default 0.00 not null,
    Hp          float(4, 2)  default 0.00 not null,
    Diff        float(6, 3)  default 0.000 not null,
    constraint Beatmaps_Md5_uindex unique (Md5)
);
create index Beatmaps_SetId_index on Beatmaps (SetId);
create index Beatmaps_Status_index on Beatmaps (Status);
create index Beatmaps_Filename_index on Beatmaps (Filename);
create index Beatmaps_Mode_index on Beatmaps (Mode);

create table Channels
(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name      varchar(32)           not null,
    Topic     varchar(256)          not null,
    ReadPriv  int     default 1     not null,
    WritePriv int     default 2     not null,
    AutoJoin  boolean default false not null,
    constraint Channels_Name_uindex unique (Name)
);
create index Channels_AutoJoin_index on Channels (AutoJoin);

create table Mail
(
    Id     INTEGER PRIMARY KEY AUTOINCREMENT,
    FromId int                   not null,
    ToId   int                   not null,
    Msg    varchar(2048)         not null,
    Time   int null,
    `Read` boolean default false not null
);

create table Relationships
(
    User1 int not null,
    User2 int not null,
    Type  varchar(10) not null check (Type in ('friend', 'block')),
    primary key (User1, User2)
);

create table Ratings
(
    UserId int      not null,
    MapMd5 char(32) not null,
    Rating int      not null,
    primary key (UserId, MapMd5)
);

create table ClientHashes
(
    UserId      int           not null,
    OsuPath     char(32)      not null,
    Adapters    char(32)      not null,
    UninstallId char(32)      not null,
    DiskSerial  char(32)      not null,
    LatestTime  datetime      not null,
    Occurrences int default 0 not null,
    primary key (UserId, OsuPath, Adapters, UninstallId, DiskSerial)
);

create table IngameLogins
(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId    int         not null,
    Ip        varchar(45) not null,
    OsuVer    date        not null,
    OsuStream varchar(11) not null,
    Datetime  datetime    not null
);

-- Time is always supplied by the app on insert (see SqliteLogRepository) — never UPDATEd
-- afterwards, so no ON UPDATE trigger is needed (MySQL's `on update CURRENT_TIMESTAMP` here was
-- dead weight; nothing ever updates a Logs row).
create table Logs
(
    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
    `From`   int         not null,
    `To`     int         not null,
    `Action` varchar(32) not null,
    Msg      varchar(2048) null,
    Time     datetime    not null
);

-- One multiplayer room = one Match. EndedAt is null while the room is still open in-memory.
-- Mode/WinCondition/TeamType moved to Rounds (can change between rounds).
-- HostId removed — tracked via MatchEvents instead.
create table Matches
(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name      varchar(50) not null,
    CreatedAt datetime    not null,
    EndedAt   datetime null
);

-- One beatmap played within a Match = one Round. WinningTeam is intentionally NOT stored here —
-- it's computed on read (TRT generation) from whatever Scores rows exist for the round, since
-- score submission and MatchComplete arrive on separate connections with no ordering guarantee.
-- Mode/WinCondition/TeamType/beatmap fields denormalized per round for self-contained TRT.
create table Rounds
(
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    MatchId        int      not null,
    RoundIndex     int      not null,
    BeatmapId      int      not null,
    MapMd5         char(32) not null,
    Mode           int      not null,
    WinCondition   int      not null,
    TeamType       int      not null,
    BeatmapArtist  varchar(128) not null default '',
    BeatmapTitle   varchar(128) not null default '',
    BeatmapVersion varchar(128) not null default '',
    BeatmapCreator varchar(64)  not null default '',
    Aborted        boolean not null default 0,
    Mods           int      not null,
    StartedAt      datetime not null,
    EndedAt        datetime null,
    constraint Rounds_Matches_Id_fk foreign key (MatchId) references Matches (Id)
);
create index Rounds_MatchId_index on Rounds (MatchId);

-- MapMd5/Mode kept denormalised (not just via Round) so the solo-leaderboard-shaped read paths
-- (osu-osz2-getscores.php) can keep querying by map without joining through Rounds. RoundId/Team
-- are null for a score submitted outside any match (not linked to a Round).
create table Scores
(
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    RoundId         int null,
    Team            int null,
    MapMd5          char(32) not null,
    Score           bigint   not null,
    Acc             float(6, 3) not null,
    MaxCombo        int      not null,
    Mods            int      not null,
    N300            int      not null,
    N100            int      not null,
    N50             int      not null,
    NMiss           int      not null,
    NGeki           int      not null,
    NKatu           int      not null,
    Grade           varchar(2) default 'N' not null,
    Status          int      not null,
    Mode            int      not null,
    PlayTime        datetime not null,
    TimeElapsed     int      not null,
    ClientFlags     int      not null,
    UserId          int      not null,
    Perfect         boolean  not null,
    OnlineChecksum  char(32) not null,
    SubmittedAt     datetime not null,
    constraint Scores_Rounds_Id_fk foreign key (RoundId) references Rounds (Id)
);
create index Scores_MapMd5_index on Scores (MapMd5);
create index Scores_Score_index on Scores (Score);
create index Scores_Status_index on Scores (Status);
create index Scores_Mode_index on Scores (Mode);
create index Scores_UserId_index on Scores (UserId);
create index Scores_OnlineChecksum_index on Scores (OnlineChecksum);
create index Scores_FetchLeaderboard_index on Scores (MapMd5, Status, Mode);
create index Scores_RoundId_index on Scores (RoundId);

-- Chronological log of match lifecycle events. ActorUserId is null for system actions
-- (e.g. server shutdown recovery, external API). Usernames denormalised for self-contained TRT.
create table MatchEvents
(
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    MatchId        int       not null,
    EventType      int       not null,
    ActorUserId    int       null,
    ActorUserName  varchar(32) null,
    TargetUserId   int       null,
    TargetUserName varchar(32) null,
    Timestamp      datetime  not null,
    Detail         varchar(512) null,
    constraint MatchEvents_Matches_Id_fk foreign key (MatchId) references Matches (Id)
);
create index MatchEvents_MatchId_index on MatchEvents (MatchId);

insert into Users (Id, Name, SafeName, Priv, Country, SilenceEnd, Email, PwBcrypt, CreationTime, LatestActivity)
values (1, 'BasilBot', 'basilbot', 1, 'ca', 0, 'bot@localhost',
        '_______________________my_cool_bcrypt_______________________', unixepoch(), unixepoch());

insert into UserStats (Id, Mode)
values (1, 0);
insert into UserStats (Id, Mode)
values (1, 1);
insert into UserStats (Id, Mode)
values (1, 2);
insert into UserStats (Id, Mode)
values (1, 3);
insert into UserStats (Id, Mode)
values (1, 4);
insert into UserStats (Id, Mode)
values (1, 5);
insert into UserStats (Id, Mode)
values (1, 6);
insert into UserStats (Id, Mode)
values (1, 8);

insert into Channels (Name, Topic, ReadPriv, WritePriv, AutoJoin)
values ('#osu', 'General discussion.', 1, 2, true),
       ('#announce', 'Exemplary performance and public announcements.', 1, 24576, true),
       ('#lobby', 'Multiplayer lobby discussion room.', 1, 2, false);
