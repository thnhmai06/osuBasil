# Audit: Basil.Protocol.Bancho and Basil.Protocol.Irc (2026-10-21)

Read-only. Files read in full; Rider `lint_files` run (only real hit: `SkipRaw` never used; the rest was allocation hints, ignored).
Items marked (verified) were reproduced with a throwaway console project in the scratchpad that compiles the source files.
Paths are relative to `src/Protocol/`.

## [bug]

* Bancho/Wire/Packets/ClientPacketType.cs:4 + PacketReader.cs:21 - the enum is `: byte` but the wire id is u16; `(ClientPacketType)ReadUInt16()` truncates silently, so id 256 reads as `ChangeAction` (verified). Make the enum `ushort`.
* Bancho/Wire/Binary/BinaryReaderExtensions.cs:80-98 `ReadOsuString` - the ULEB128 length is untrusted and unbounded. `0xFF FF FF FF 07` (int.MaxValue) tries to allocate ~2 GB and throws `OutOfMemoryException` (verified). A 5th byte >= 0x08 gives a negative length, so `ArgumentOutOfRangeException` instead of `EndOfStreamException` (verified). `shift` is unbounded (>= 32 wraps). Needs a max byte count (5) and a configurable max length, plus a guard that checks the length against the remaining stream.
* Bancho/Wire/Binary/BinaryReaderExtensions.cs:57-66 `ReadI32ListI32L` - `new int[uint]` from an untrusted 32-bit count (up to 4G elements). Unused by anything; delete or bound it.
* Bancho/Wire/Binary/BinaryReaderExtensions.cs:26-36 `SkipRaw` - dead (Rider), and a negative length makes `stackalloc` throw `OverflowException`. `ReadHeader` (PacketReader.cs:23) also hands an unchecked negative `Length` to callers. Delete `SkipRaw` or validate. The header test file claims to test `SkipRaw`, which is not reachable.
* Bancho/Wire/Packets/PacketReader.cs:40 vs PacketWriter.Payload.cs:60 - `ReadMatch` reads the room id as `Int16`, the writer writes `ushort`. Ids above 32767 do not round-trip. Same for team/status read as `sbyte` (PacketReader.cs:51-54) while the writer writes `byte`: status 128 (Quit) reads as -128. Read unsigned, as the writer does.
* Bancho/Wire/Packets/PacketWriter.cs:16-32 `Wrap` - needs a seekable stream (it patches the length by seeking back). A non-seekable response stream throws `NotSupportedException` (verified). Neither the class doc nor the constructor says so. Document it or buffer internally.
* Bancho/Wire/Packets/PacketWriter.Payload.cs:58-101 `WriteMatchPayload` - writes whatever `Slots.Count` it gets; anything other than 16 produces a corrupt packet with no error. `HasPlayer` with `PlayerId == null` throws `InvalidOperationException` (line 88); a `PlayerId` on a non-occupied status is silently dropped. Decide: throw a documented exception on a count other than 16.
* Bancho/Wire/Packets/PacketWriter.Payload.cs:29-34, PacketWriter.cs:111,122,454-456 - unchecked narrowing casts to the wire width (`(ushort)playerCount`, `(byte)action`, `(ushort)pp`, `(byte)(utcOffset + 24)`, `(byte)countryCode`, `(byte)(banchoPrivileges | mode << 5)`, ScoreFrame counts to `ushort`). A large value wraps silently. Only `WriteI32ListI16L` uses `checked`. Use `checked` consistently, or type the parameters at wire width.
* Bancho/Models/Auth/ClientHash.cs:30 - `Split(':', 5)` swallows extra components: `a:b:c:d:e:f:` parses with `DiskSignatureHash = "e:f"` (verified), contradicting the doc "exactly five components" (line 25). Split without a limit and require 5.
* Bancho/Models/Auth/ClientBuild.cs:78 - `$` matches before a trailing `\n`, so `"b20240815\n"` parses (verified) and then does not round-trip. Use `\z`. The alternative `\d{4}\.\d{3}` can only ever throw (`ParseExact` "yyyyMMdd" rejects it, caught in `TryParse`); it is dead. `\d` matches non-ASCII digits (use `[0-9]` or `RegexOptions.ECMAScript`). Stream suffix is case-sensitive.
* Bancho/Models/Scores/ScoreSubmission.cs:82 - `int.Parse` throws `OverflowException` on out-of-range numbers (verified) but the doc promises only `FormatException` (line 53). Hosts that catch `FormatException` will crash. No `TryParse` exists, unlike the sibling models.
* Bancho/Models/Scores/ScoreSubmission.cs:77 - `DateTime.ParseExact` yields `Kind = Unspecified`; the project rule is UTC only (`DateTimeOffset`, `DateTimeStyles.AssumeUniversal`). The `Timestamp` property is a `DateTime`.
* Bancho/Models/Scores/ScoreSubmissionCipher.cs:25-41 - undocumented exceptions: an osu version that makes the key length not 16/24/32 bytes (e.g. `b20240815`, 33 bytes) and an IV that is not 32 bytes throw `ArgumentException`; truncated ciphertext throws `DataLengthException`. Only `InvalidCipherTextException` is documented. Also no `Encrypt`, so the cipher cannot be round-trip tested without duplicating the key logic.
* Irc/IrcMessage.cs:29-46 `ToString` - no CR/LF/NUL guard. `Privmsg(prefix, "#c", "hi\r\nQUIT :x")` emits two lines (verified): IRC command injection from user chat text. A non-last parameter that contains a space or starts with `:` is also emitted as-is and re-parses as a different message. The serializer must reject (documented exception) or strip these.
* Irc/IrcMessage.cs:16 - record equality over `IReadOnlyList<string>` is by reference: two messages parsed from the same line are `!=` (verified). Tests and handlers cannot compare messages.
* Irc/IrcMessage.cs:55-120 `Parse` - accepts an empty prefix (`": PING"`), does not strip tags (`@a=b CMD`), keeps the command case as sent, and has no line-length or parameter-count limit. Decide and document each; callers must not have to guess.

## [rule]

* ConfigureAwait: none in either project (checked).
* Names carry the mechanism: `ReadI32ListI16L`, `ReadI32ListI32L`, `WriteI32ListI16L` (BinaryReader/WriterExtensions); name by meaning, e.g. `ReadInt32List`. `IsHasPlayerStatus` (RoomSlotPacket.cs:29) is also ungrammatical. `ClientBuild.Stream` shadows `System.IO.Stream`; `Keys` (Replay/ReplayFrame.cs:17) shadows `System.Windows.Forms.Keys`.
* Abbreviations: `PacketWriter.Privileges(int priv)` (line 377, banned `Priv`), `msg`, `ms`, `t`, `ip`, `Rtx`, `IrcMessage.Params` (use `Parameters`), `Numeric(... args)`, `RplIsupport`, `ErrChanOPrivsNeeded`, `ErrPasswdMismatch`, `ErrInviteOnlyChan`, `RplNamReply`, `ErrNicknameInUse` family (RFC mnemonics are not meaning). `ScoreFrame.CurrentHp`/`Num300` are acceptable osu terms.
* One concept, one name: wire "match" vs Basil "room" (`ReadMatch`, `UpdateMatch`, `NewMatch` vs `RoomPacket`, `RoomStatePacket`). The same byte is `Powerplay` in RoomStatePacket.cs:36 and "room type, always 0" in PacketWriter.Payload.cs:62. `ReplayFrameBundle.ExtraByte` is an `int` (ReadInt32). `ScoreFrame.Id` vs wire slot id.
* Two models for one wire struct: `RoomPacket` (write shape, `IReadOnlyList<RoomSlotPacket>`) and `RoomStatePacket` (read shape, four parallel lists). Consumers must zip them; one model should serve both directions.
* Value types with a text form should implement `IParsable<T>`/`IFormattable` (code-style.md): `ClientBuild`, `ClientHash`, `ClientSerial` have hand-rolled `Parse`/`TryParse` only; `IrcMessage` and `IrcUserPrefix` do it right. `TryParse` in the three Auth types is written as try/catch around `Parse` (exceptions as control flow); invert it, as `IrcMessage` does.
* Models/Scores/ScoreSubmissionCipher.cs is behaviour in a `Models` folder; it belongs under `Wire`.
* ScoreFrame.cs:7 - a mutable `struct` with one `set` (`Id`, line 13) among `init` members; other models are `record`/`readonly record struct`. Make it a `readonly record struct` (exposed mutable state).
* PacketReader.cs:56-61 - `ReadInt32()` inside a LINQ `select` has side effects in a query; use a `for` loop. Magic `16` repeated 4 times (PacketReader.cs:50-73); one named constant, and the same constant for the doc "16 slots".
* PacketWriter.cs:481 - user-visible English text ("Come join my game: ...") is a literal in Protocol. Rule 9: the chat text is public behaviour; pass it in or own it as a named constant in the owning chat surface. Protocol should not compose business wording.
* PacketWriter.cs:74 `ChangeUsername` and `MainMenuIcon` compose the `>>>>` / `|` delimited strings inside the writer (acceptable wire knowledge), but the same pattern is missing from the reader side (no `ReadMainMenuIcon`); asymmetry is fine, just keep it documented.
* Irc/IrcMessage.cs:221-224 and IrcUserPrefix.cs:104-107 - redundant `ToString()` override that only forwards (Rider: "same default value"). A method that only forwards should go (code-style: no wrapper methods). Note `IrcUserPrefix.ToString()` does not recurse; the 2-arg overload wins (verified).
* Irc/IrcUserPrefix.cs:74-86 - `switch` statement with `case < 0 when` fallthrough to code after the switch; rewrite as a plain if chain.
* Irc/IrcUserPrefix.cs:19-27 `ToString` throws `InvalidOperationException` for a state the type's own constructor allows (`User` without `Host`); a debugger or logger calling `ToString` can throw. Validate in the constructor instead.
* Doc/mechanism wording: ReplayFrame.cs:12-14 and 39-41 name `osu.Game...` classes (implementation detail); IrcNumericReply.cs:3 claims "actually used by Basil's IRC bridge" (drifts, and ties Protocol to a consumer).

## [doc]

* PacketWriter.cs: ~45 methods return `void` but their `<returns>The complete packet.</returns>` claims otherwise (lines 52, 61, 71, ...). Remove `<returns>`, say "Writes ... to the stream".
* PacketWriter.cs:9 and PacketReader.cs:13 - no doc of the stream contract (seekable, ownership, not thread-safe, no disposal).
* RoomSlotPacket.cs:24-28 - the remark is wrong: NotReady (4) and Ready (8) are inside the `0b0111_1100` mask; only Open (1), Locked (2) and Quit (128) fall outside.
* RoomStatePacket.cs:13 - typo "roomes"; `Powerplay` doc contradicts the writer's "room type".
* ServerPacketType.cs:72-73 `AllPlayersLoaded = 45` duplicates the meaning of `MatchAllPlayersLoaded = 53`; there is no writer for either 45, 10, 62, 69. `Rtx` (line 174) expansion "Rich Text eXchange" looks invented. The `TaikoByte` members (`Don`, `Kat`, `Right`, `Big`, `LargeDouble`; Replay/ReplayFrame.cs:44-62) are unverified against any reference (bancho.py only has an unused `taiko_byte`); confirm or remove.
* ScoreSubmission.cs:47-51 - "client flags (without bit 4)": the code masks the value 4 (bit 2). `ScoreFrame.Id` doc says "id of the player"; the wire byte is the slot id (unverified; check against the client).
* ScoreSubmissionCipher.cs - document the key shape (24-byte constant + version -> must be 16/24/32 bytes) and the IV length (32).
* IrcMessage.cs:10-15 - `<param>` tags list Prefix before Command (the ctor order is Command, Prefix, Params). `ToString` format/provider "Ignored" is fine, but `Parse` does not document case handling, tags, empty prefix.
* IrcNumericReply.cs:102,129 - "may not send the target channel" (missing "to"); `ErrInviteOnlyChan` doc is wrong (473 = cannot join, channel is invite-only). Also `ErrUnknownCommand`, `RplLuserMe` docs are fine.
* BinaryReaderExtensions.cs:42,55 - say what a count of 0 returns and the max; `ReadOsuString` doc says "existence byte not 0x0B is the null marker" but any value (e.g. 0x01) is treated as null with no desync check.
* WriteOsuString (BinaryWriterExtensions.cs:33) documents `null` input but the parameter is `string`, not `string?`.

## [style]

* Hand-written wire layout in `PacketWriter.Payload.cs` and `PacketReader.cs` is not mirrored: reader and writer live in different files with different field names; keep one field table per packet in a single doc.
* `PacketWriter.cs`: 70 public methods, one lambda each (`Wrap(type, () => ...)`); consistent, acceptable.
* Files mix BOM/no BOM and trailing newlines; leave to the IDE reformat.

## Public API with no documented test-worthy contract

* `PacketReader` (all methods): no statement of truncated input, max sizes, who validates `Length`.
* `PacketWriter.Wrap`/stream requirements; `UserStats.accuracy` rounding (`float`); `UserPresence.utcOffset + 24`, mode in the high bits of privileges.
* `ScoreSubmissionCipher.Decrypt` (exceptions, key/IV shape), `ScoreSubmission.Parse` (overflow, extra fields accepted: `Length < 18` is the only check), `ClientSerial.Parse` (extra `|` swallowed into `DiskSignature`).
* `IrcMessage.Parse` leniency (see above); `IrcUserPrefix.TryParse` (accepts spaces in nick, `a@b@c`, `a!b!c@d`).

## Behaviours worth testing (existing tests: header x2, `HasPlayer` mask, `ReadMessage`, score frame v1/v2, replay frame and bundle, `ReadMatch` round-trip, 66 writer goldens, 8 IRC parse/format tests)

* `ReadOsuString`: marker 0x00 -> ""; `0x0B 0x00` -> ""; one-, two-, five-byte ULEB (127, 128, 16383, 16384, 2^28); length longer than the data -> `EndOfStreamException`; negative/oversized length; multibyte UTF-8; invalid UTF-8.
* `WriteOsuString`/`WriteUleb128`: null, "", boundaries 127/128; round-trip against the reader.
* `ReadHeader`: id > 255 (`ChangeAction` aliasing), truncated header (<7 bytes), negative length.
* `ReadI32ListI16L`: 0, 1, 65535 elements, truncated body; `WriteI32ListI16L` with 65536 -> `OverflowException`.
* `ReadMatch`/`WriteMatchPayload`: round-trip with id > 32767, status 128, FreeMods on/off, password empty/masked (`0B 00`)/real, fewer or more than 16 slots, occupied slot without id.
* `ReadScoreFrame`: exact 29 bytes then 16 more for V2; truncated at each boundary; `ReadReplayFrameBundle`: 0 and 65535 frames, unknown action byte, truncated score frame.
* Writer goldens for the packets that have none: `MatchPlayerFailed`, `SilenceEnd`, `MatchInvite` text, `UserPresence` (utcOffset -12/+14, mode bits), `UserStats` (pp > 65535).
* `ClientBuild`: `b20240815`, `20240815`, `.2`, each stream suffix, uppercase stream, `2024.123`, trailing newline, impossible date (`20241399`), two-digit revision, round-trip `ToString`; `TryParse(null)`.
* `ClientHash`: valid, missing trailing colon, 4/6 components, empty components, round-trip. `ClientSerial`: one part, three parts, empty parts.
* `ScoreSubmission.Parse`: valid, 17 fields, 19 fields, non-numeric, overflow, "True"/"true"/"False", date formats, flags with bit 4 set and space counts 0..7.
* `ScoreSubmissionCipher`: known vector (encrypt with BC in the test), wrong IV length, odd osu version length, bad padding, non-block-aligned data, bad base64.
* IRC `Parse`: prefix only, no command, multiple spaces, `:` inside a middle param, trailing with leading colon, empty trailing (`CMD :`), 15+ params, CR/LF inside, lowercase command, tags, `null`.
* IRC `ToString`: last param with space / leading colon / empty, middle param with space (documented behaviour), CR/LF, null prefix; parse-then-format fixed point.
* `IrcMessage.Numeric`: code padding (`001`, `005`, `433`), target first; each factory (`Privmsg`, `Part` with/without reason, `Ping`, `Pong`).
* `IrcUserPrefix`: `nick`, `nick@host`, `nick!user@host`, `@host`, `nick@`, `nick!@host`, `!user@host`, `nick!user`, `a@b!c`, `default` value `ToString`, user-without-host `ToString` (throws).
* `RoomSlotPacket.HasPlayer` for every status byte (exists); `ServerPacketType`/`ClientPacketType` id table against a golden list (guards accidental renumbering).
