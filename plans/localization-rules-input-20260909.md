Continue. Mình thấy Localization của Bot đang hơi lủng củng ở phần chia các section. Vậy nên, mình đưa cho bạn bộ quy tắc viết Localization kèm skill Humanizer. Phiền bạn từ bộ quy tắc này để 
- Tạo cho mình document cho Developer và Agents về quy tắc viết Localization.
- Ngoài ra, hãy tách Claude.md thành các tài liệu phù hợp trong docs/for-agents để tránh bị phân tán ra ngoài. Như vậy, dù có là bạn (Claude) hay Codex hay agent nào đó khác thì chỉ cần tập trung vào một nơi là docs/for-agents thôi.
Cái này mình không yêu cầu làm luôn. Bạn có thể làm chúng ở phase liên quan đến viết/sửa lại tài liệu là được.

# Localization Structure, Naming & Humanization Rules

## 1. Mục tiêu

Localization phải được tổ chức theo cách:

* Dễ tìm và dễ đoán vị trí của message.
* Giữ được cấu trúc theo domain/feature thay vì flatten toàn bộ key.
* Có tính nhất quán về naming giữa các module.
* Ưu tiên cách diễn đạt tự nhiên như người thật viết.
* Tránh văn phong máy móc, cứng, generic hoặc mang dấu vết của AI-generated text.
* Có thể mở rộng khi thêm command, feature, subsystem hoặc giao diện mới.

Các quy tắc này áp dụng cho **toàn bộ localization**, không chỉ Bot Commands. Chúng bao gồm nhưng không giới hạn ở IRC, Bot, Web, UI, notifications, errors, validation messages, system messages và các feature khác.

---

# 2. Tư duy tổ chức: Hierarchy trước, message sau

Localization phải được tổ chức theo hierarchy phản ánh cấu trúc của chương trình.

Ưu tiên:

```text
Domain
└── Feature
    └── Command / Operation / Context
        └── Message
```

Ví dụ:

```text
Commands
└── Mp
    ├── Join
    │   ├── Usage
    │   ├── AlreadyInAMatch
    │   ├── IncorrectPassword
    │   └── Joined
    ├── Move
    │   ├── Usage
    │   └── Moved
    └── Settings
        ├── RoomName
        ├── Beatmap
        └── Players
```

Không flatten thành một namespace khổng lồ:

```text
CommandsMpJoinUsage
CommandsMpJoinAlreadyInAMatch
CommandsMpMoveUsage
CommandsMpMoveMoved
CommandsMpSettingsRoomName
...
```

Cấu trúc localization phải phản ánh cấu trúc domain của hệ thống.

---

# 3. Section phải đại diện cho một khái niệm có ý nghĩa

Một section không nhất thiết lúc nào cũng phải là command.

Section có thể đại diện cho:

* Một feature.
* Một command.
* Một subsystem.
* Một context.
* Một loại interaction.
* Một domain object.
* Một nhóm message có cohesion cao.

Ví dụ hợp lệ:

```text
Commands
  Mp
    Join
    Move
    Settings

Irc
  Connection
  Channel
  User

Authentication
  Login
  Registration

Notifications
  Update
  Moderation
```

Điểm quan trọng là section phải có **ý nghĩa riêng**, có ranh giới tự nhiên và có khả năng chứa nhiều message liên quan.

Không tạo section chỉ để tránh một vài field dài.

---

# 4. Một section phải có cohesion cao

Các key nằm cùng một section phải liên quan chặt chẽ về mặt hành vi hoặc khái niệm.

Ví dụ:

```text
Mp
├── Join
│   ├── Usage
│   ├── AlreadyInAMatch
│   ├── IncorrectPassword
│   └── Joined
```

có cohesion tốt vì tất cả thuộc cùng một operation.

Ngược lại:

```text
Mp
├── Joined
├── BeatmapNotFound
├── RefereeAdded
├── ServerStarted
├── IrcDisconnected
└── InvalidPassword
```

là flatten và phải tránh.

---

# 5. Không flatten chỉ vì "đỡ nhiều section"

Không có mục tiêu giảm số lượng section bằng mọi giá.

Nếu một domain tự nhiên có hierarchy:

```text
Irc
├── Connection
├── Channel
├── User
└── Message
```

thì giữ hierarchy đó.

Không gộp thành:

```text
Irc
├── ConnectionFailed
├── ChannelJoined
├── UserJoined
├── MessageReceived
...
```

chỉ để có ít section hơn.

**Khả năng định vị và tính cohesion quan trọng hơn số lượng section.**

---

# 6. General không bắt buộc phải là một section phẳng

`General` chỉ là nơi chứa các message không thuộc một feature cụ thể.

Nó **được phép có hierarchy bên trong**.

Ví dụ:

```text
General
├── Validation
│   ├── NotFound
│   ├── InvalidArgument
│   └── Required
├── Permissions
│   ├── Forbidden
│   └── NotAllowed
├── Commands
│   ├── Unknown
│   └── InvalidSyntax
└── System
    ├── Unavailable
    └── UnexpectedError
```

Không flatten tất cả vào:

```text
General
├── NotFound
├── InvalidArgument
├── Required
├── Forbidden
├── UnknownCommand
├── Unavailable
└── UnexpectedError
```

Khi `General` có nhiều loại message khác nhau, phải xem xét chia thành các subsection có semantic rõ ràng.

---

# 7. Không tạo hierarchy giả

Ngược lại, không chia nhỏ chỉ vì có thể chia.

Ví dụ không cần:

```text
General
└── Validation
    └── User
        └── Registration
            └── Username
                └── Required
```

nếu chỉ có một message.

Hierarchy chỉ nên được tạo khi nó giúp:

* thể hiện domain;
* gom những message thực sự liên quan;
* cải thiện khả năng tìm kiếm;
* hoặc tạo không gian hợp lý cho mở rộng trong tương lai.

---

# 8. Hãy tư duy như thiết kế object/domain tree

Khi quyết định section, hãy tự hỏi:

> "Nếu localization tree là một object model, các object nào thực sự tồn tại?"

Ví dụ:

```text
Irc
└── Channel
    ├── Joined
    ├── Left
    └── Full
```

tự nhiên hơn:

```text
IrcChannelJoined
IrcChannelLeft
IrcChannelFull
```

Tư duy chính là:

> **Localization tree nên phản ánh conceptual model của hệ thống, không chỉ là danh sách string.**

---

# 9. Section không được phụ thuộc vào cách viết message

Hai message nói về cùng một domain vẫn nên cùng section dù wording khác nhau.

Ngược lại, hai message có wording tương tự nhưng thuộc hai context khác nhau không nhất thiết phải cùng section.

Section được quyết định bởi **context và ownership**, không phải bởi từ ngữ xuất hiện trong message.

---

# 10. Context thuộc về section, semantic thuộc về field

Đây là nguyên tắc naming quan trọng nhất.

Section cung cấp context:

```text
Mp.Join
Mp.Move
Irc.Channel
Authentication.Login
```

Field mô tả semantic:

```text
Usage
Joined
Failed
NotFound
AlreadyJoined
```

Do đó:

```text
Mp.Join.Usage
Mp.Move.Usage
Irc.Channel.Usage
```

được ưu tiên hơn:

```text
Mp.Join.JoinUsage
Mp.Move.MoveUsage
Irc.Channel.ChannelUsage
```

Không lặp lại context đã có trong đường dẫn.

---

# 11. Các semantic giống nhau phải có field name giống nhau

Nếu hai section có cùng loại message, ưu tiên cùng naming.

Ví dụ:

```text
Mp.Join.Usage
Mp.Move.Usage
Mp.Map.Usage
```

không dùng:

```text
JoinUsage
MoveUsage
MapSyntax
```

Tương tự:

```text
Connection.Failed
Authentication.Failed
Generation.Failed
```

nếu chúng thực sự mang cùng semantic.

Consistency quan trọng hơn việc cố tạo ra một tên "hay hơn" cho từng trường hợp riêng lẻ.

---

# 12. Không lặp danh từ đã được section xác định

Ví dụ:

```text
Mp.Settings.RoomName
Mp.Settings.Beatmap
Mp.Settings.Players
```

ưu tiên hơn:

```text
Mp.Settings.SettingsRoomName
Mp.Settings.SettingsBeatmap
Mp.Settings.SettingsPlayers
```

Tương tự:

```text
Mp.Referee.Added
Mp.Referee.Removed
```

thay vì:

```text
Mp.Referee.AddedReferee
Mp.Referee.RemovedReferee
```

trừ khi tên ngắn gây mơ hồ trong cùng section.

---

# 13. Tên field phải vừa ngắn vừa đủ rõ

Không đặt tên dài chỉ để "chắc chắn không mất context".

Ưu tiên:

```text
AlreadyInAMatch
NotFound
IncorrectPassword
Joined
Changed
Removed
```

thay vì:

```text
UserIsAlreadyInAnotherActiveMultiplayerMatch
NoBeatmapCouldBeFoundWithTheSpecifiedId
SuccessfullyJoinedTheRequestedMultiplayerMatch
```

Tên key là identifier, không phải câu mô tả.

---

# 14. Không dùng generic field nếu semantic có thể mô tả cụ thể

Tránh:

```text
Message
Result
Response
Success
Error
Failure
Text
Content
```

nếu có thể dùng tên cụ thể hơn.

Ví dụ:

```text
Joined
NotFound
AlreadyInAMatch
IncorrectPassword
```

tốt hơn:

```text
Success
Error
Error2
Result
```

---

# 15. Giữ vocabulary thống nhất toàn hệ thống

Một khái niệm phải ưu tiên một vocabulary chung.

Ví dụ nếu project dùng:

```text
Created
Updated
Changed
Removed
Enabled
Disabled
Started
Stopped
Failed
```

thì không tùy ý đổi sang:

```text
Made
Modified
Deleted
TurnedOn
TurnedOff
Began
Ended
CouldNot...
```

trừ khi semantic thực sự khác.

Naming phải tạo cảm giác đây là **một hệ thống duy nhất**, không phải nhiều người viết độc lập.

---

# 16. Ưu tiên domain-specific section trước General

Nếu một message chỉ thuộc về một feature cụ thể, đặt nó trong feature đó.

Ví dụ:

```text
Mp.Join.NotFound
```

thay vì:

```text
General.NotFound
```

nếu message có ý nghĩa đặc thù đối với việc join match.

`General` chỉ nên dùng cho những message thực sự dùng chung hoặc không có owner rõ ràng.

---

# 17. Có thể dùng shared subsection khi semantic thực sự dùng chung

Nếu nhiều feature thực sự sử dụng cùng một concept, có thể tạo shared namespace.

Ví dụ:

```text
General
├── Validation
├── Permissions
└── Errors
```

Nhưng không được chuyển tất cả message giống nhau về mặt từ ngữ vào shared section.

Ví dụ hai `NotFound` khác nhau:

```text
Mp.Map.NotFound
User.Search.NotFound
```

không nên ép thành:

```text
General.NotFound
```

chỉ vì cả hai đều nói "not found".

**Shared localization phải chia sẻ semantic, không chỉ chia sẻ wording.**

---

# 18. Humanizer hóa toàn bộ message

Localization text phải được viết như **một người thật đang nói với người dùng**.

Không chấp nhận wording có cảm giác:

* máy móc;
* literal translation;
* template-heavy;
* generic;
* overly formal;
* repetitive;
* AI-generated.

Khi tạo hoặc chỉnh sửa localization, phải tận dụng **Humanizer skill** có sẵn để review và cải thiện wording.

Humanizer không chỉ sửa grammar.

Nó phải kiểm tra:

* cách nói có tự nhiên không;
* sentence flow có giống người thật không;
* có từ nào thừa không;
* có đang lặp lại information mà UI đã cung cấp không;
* tone có phù hợp với context không;
* câu có quá formal hoặc robotic không;
* cách dùng dấu gạch ngang, apostrophe, capitalization và punctuation có tự nhiên không;
* cùng một concept có đang được diễn đạt nhất quán ở nơi khác không.

---

# 19. Ưu tiên human wording thay vì literal wording

Ví dụ:

```text
"Unable to perform the requested operation."
```

có thể đúng về mặt grammar nhưng thường quá generic và máy móc.

Nếu context đã biết:

```text
"Couldn't join the match."
```

tự nhiên hơn.

Tương tự, ưu tiên:

```text
"You're already in a match."
```

thay vì:

```text
"You are currently participating in another multiplayer match."
```

khi không cần mức chi tiết đó.

---

# 20. Không viết message để làm hài lòng cấu trúc key

Key và message là hai concerns khác nhau.

Không nên tạo wording kỳ quặc chỉ để match một field name.

Ví dụ field:

```text
Joined
```

có thể chứa:

```text
"Joined match #12."
```

không cần ép message phải có cấu trúc kiểu:

```text
"You have successfully joined..."
```

chỉ vì field tên `Joined`.

**Key mô tả semantic; message phục vụ người dùng.**

---

# 21. Tránh AI-style repetition

Đặc biệt tránh các pattern như:

```text
Successfully ...
Please ...
You have successfully ...
Unable to ...
It was not possible to ...
The requested ...
```

khi chúng làm câu trở nên cứng hoặc dài một cách không cần thiết.

Ví dụ:

```text
"Successfully removed the referee."
```

thường có thể trở thành:

```text
"Removed {0} from the match referees."
```

Tương tự:

```text
"Unable to join the match."
```

có thể là:

```text
"Couldn't join the match."
```

Tuy nhiên không áp dụng máy móc. Naturalness và context quan trọng hơn việc thay từ theo công thức.

---

# 22. Humanizer phải giữ nguyên semantic

Humanization không được làm thay đổi behavior hoặc information.

Không được:

* làm mất placeholder;
* thay đổi meaning;
* bỏ điều kiện quan trọng;
* đổi số liệu;
* đổi command syntax;
* đổi terminology có ý nghĩa kỹ thuật.

Ví dụ:

```text
Usage: !mp move <name/id> <slot 1-16>
```

có thể được humanize về punctuation hoặc wording nếu cần, nhưng syntax phải giữ nguyên.

---

# 23. Placeholder phải nhất quán

Placeholder phải phản ánh cùng loại dữ liệu bằng cùng convention.

Ví dụ nếu `{0}` là username trong một context thì các message cùng context không được tùy ý đổi thành:

```text
{name}
{username}
{player}
{0}
```

trừ khi localization system của project chủ động sử dụng named placeholders.

Không thay đổi placeholder khi chỉ đang humanize message.

---

# 24. Không over-structure

Cấu trúc phải có chiều sâu vừa đủ.

Tốt:

```text
Irc
├── Connection
├── Channel
└── User
```

Không tốt:

```text
Irc
└── User
    └── State
        └── Connection
            └── Channel
                └── Messages
                    └── Errors
```

mỗi level phải mang thêm semantic.

Nếu một level không giúp hiểu domain thì không nên tồn tại.

---

# 25. Khi thêm message mới, phải tìm nơi có semantic gần nhất

Không tạo section hoặc naming convention mới ngay lập tức.

Trước tiên kiểm tra:

1. Message thuộc domain nào?
2. Feature/context nào sở hữu nó?
3. Đã có section tương ứng chưa?
4. Đã có field cùng semantic chưa?
5. Đã có vocabulary tương đương ở nơi khác chưa?

Chỉ tạo section hoặc naming mới khi cấu trúc hiện tại thực sự không thể biểu diễn message đó một cách hợp lý.

---

# 26. Khi review localization, kiểm tra cả structure và wording

Một localization change chỉ được xem là tốt khi đáp ứng đồng thời:

```text
Structure
├── Đúng domain
├── Đúng ownership
├── Không flatten không cần thiết
├── Không tạo hierarchy giả
└── Naming nhất quán

Wording
├── Tự nhiên
├── Dễ hiểu
├── Đúng tone
├── Không robotic
├── Không AI-generated
├── Không dư thừa
└── Không thay đổi semantic
```

Không đánh giá localization chỉ bằng việc "key có compile" hoặc "grammar có đúng".

---

# 27. Quy tắc ưu tiên khi có xung đột

Khi có nhiều cách tổ chức hoặc đặt tên, ưu tiên:

```text
1. Correct semantic / correct ownership
2. Natural human wording
3. Consistency với localization hiện có
4. Cohesion của section
5. Khả năng mở rộng
6. Đơn giản
7. Ngắn gọn
```

Không hy sinh cấu trúc domain để có ít key hơn.

Không hy sinh natural wording để giữ một naming pattern máy móc.

Không tạo abstraction chỉ để làm cấu trúc "đẹp" trên giấy.

---

# 28. Checklist bắt buộc

Trước khi hoàn thành thay đổi localization, phải tự kiểm tra:

* Message này thuộc domain nào?
* Section hiện tại có phải owner tự nhiên của nó không?
* Có đang flatten một hierarchy vốn có không?
* Có đang tạo section chỉ vì một message không?
* Có đang lặp lại context trong field name không?
* Semantic này đã có field name dùng ở nơi khác chưa?
* Vocabulary có nhất quán với project không?
* Message đã được humanizer review chưa?
* Câu có giống cách người thật nói không?
* Có từ nào thừa hoặc robotic không?
* Có giữ nguyên placeholder và semantic không?

Nếu câu trả lời hợp lý cho tất cả các điểm trên, localization có thể được xem là đạt convention.


Ngoài ra, hãy chuyển docs-guideline.md sang docs/... để mọi người đều có thể tuân thủ và nhìn thấy. Ngoài ra, bổ sung thêm những quy tắc sau:
# Additional documentation guidelines

The following rules supplement the existing documentation guidelines.

## Documentation ownership and source of truth

Before documenting a behavior, identify its authoritative source.

The source of truth may be:

* the current implementation;
* a configuration schema;
* an API contract;
* a protocol specification;
* an ADR or other explicitly authoritative design document.

Do not infer undocumented behavior from nearby code when a more authoritative source exists.

When documentation conflicts with the current implementation, do not silently preserve outdated documentation. Update it to match the authoritative current behavior or explicitly identify the discrepancy when it cannot be resolved as part of the change.

Do not create multiple competing sources of truth for the same contract.

If a document is intended to be authoritative for a topic, make that scope clear.

---

## Documentation hierarchy and information architecture

Treat the `docs/` tree as an information architecture, not as a collection of independent Markdown files.

Each directory and page should represent a meaningful concept or audience boundary.

Avoid flattening unrelated documentation into a single large page merely to reduce the number of files.

Likewise, avoid creating deeply nested directory structures that do not represent meaningful conceptual boundaries.

Prefer a structure such as:

```text
docs/
├── for-client/
│   ├── multiplayer.md
│   └── commands.md
├── for-technicians/
│   ├── installation.md
│   ├── configuration.md
│   └── troubleshooting.md
├── for-developers/
│   ├── architecture.md
│   └── testing.md
└── for-agents/
    ├── localization.md
    └── verification.md
```

over either:

```text
docs/
├── everything.md
```

or unnecessary hierarchy such as:

```text
docs/
└── architecture/
    └── server/
        └── core/
            └── implementation/
                └── overview.md
```

unless every level represents a useful navigation boundary.

---

## Scope and cohesion

Every page should have a clear responsibility.

A page may link to related topics, but should not gradually expand into the general documentation for an entire subsystem.

When a page begins accumulating multiple distinct concerns, consider splitting it into separate pages with clear ownership.

Do not split content merely because it has multiple headings. Split it when the resulting pages would have independently useful scopes.

A reader should be able to answer:

> What problem or topic does this page own?

without needing to inspect the entire documentation tree.

---

## Avoid duplicate explanations

Do not duplicate detailed explanations merely because different audiences encounter the same concept.

Instead:

1. maintain one authoritative explanation where appropriate;
2. link to it from other pages;
3. provide only audience-specific context where necessary.

For example, an operational guide may explain how to configure a feature without repeating its entire internal architecture.

A developer document may link to the operational configuration reference rather than maintaining a second list of configuration keys.

Audience-specific documentation may intentionally present the same system differently, but should not create conflicting technical claims.

---

## Reader orientation

A page should help readers understand where they are before presenting detail.

When useful, begin with a short explanation of:

* what the page covers;
* who it is for;
* what it does not cover;
* what prerequisite knowledge or setup is required.

Do not add boilerplate introductions to every page.

Add orientation only when it reduces ambiguity or helps readers navigate a complex topic.

---

## Prerequisites and assumptions

State prerequisites when missing them would cause the reader to fail, misunderstand the instructions, or use the wrong procedure.

Examples include:

* required software;
* supported operating systems;
* network requirements;
* permissions;
* prior configuration;
* required knowledge;
* commands that must have been run first.

Do not hide important assumptions inside later steps.

Do not list obvious or universal prerequisites merely for completeness.

---

## Procedures

When documenting a procedure:

* order steps according to the order they must be performed;
* make dependencies between steps explicit;
* distinguish required steps from optional steps;
* state expected results when they help verify progress;
* explain destructive or irreversible actions before the action.

Avoid vague procedural instructions such as:

```text
Configure the server correctly.
Set up the required settings.
Ensure everything is working.
```

Prefer instructions that tell the reader what must be true or what action must be performed.

When multiple procedures are valid, document the conditions under which each one should be used.

Do not present one workflow as mandatory when it is only one possible implementation or operational approach.

---

## Verification and expected outcomes

Documentation should distinguish between:

* performing an action;
* verifying that the action worked.

Where practical, document observable expected outcomes.

For example:

* a process starts successfully;
* a command returns a specific type of result;
* a configuration change takes effect after restart;
* a health endpoint reports the expected state.

Do not assume that successfully executing a command proves that the intended result was achieved.

Developer and technician documentation should explicitly include verification when failure would otherwise be difficult to diagnose.

---

## Failure and troubleshooting information

Do not document only the happy path when common or important failure modes exist.

Document failure modes when the reader can reasonably encounter them and knowing about them materially improves diagnosis.

For each documented failure mode, explain when useful:

* what the reader observes;
* likely causes;
* how to distinguish between similar causes;
* what can safely be tried;
* when further investigation is required.

Do not create exhaustive lists of hypothetical failures.

Prioritize failures that are common, expensive, destructive, difficult to diagnose, or important for correct operation.

---

## Safety and destructive operations

Clearly identify actions that may:

* delete or overwrite data;
* invalidate existing configuration;
* disconnect users;
* interrupt service;
* require downtime;
* expose credentials or sensitive information;
* be difficult or impossible to reverse.

Warnings should appear before the relevant action, not only after it.

Do not use generic warning blocks for routine operations.

Warnings should explain the actual consequence and the conditions under which it matters.

---

## Examples versus guarantees

Clearly distinguish examples from guarantees.

Do not allow an example to imply that:

* an identifier always has a particular format unless guaranteed;
* an output is always identical;
* an implementation detail is part of the public contract;
* an example workflow is the only supported workflow.

Use precise language such as:

* "for example";
* "may";
* "typically";
* "currently";

when behavior is not guaranteed.

Use stronger language such as:

* "must";
* "always";
* "only";

only for actual contracts, invariants, or enforced behavior.

---

## Normative language

Use normative words intentionally.

For documentation that defines a contract:

* **must** indicates a requirement;
* **must not** indicates a prohibited behavior;
* **should** indicates a recommendation;
* **should not** indicates a discouraged behavior;
* **may** indicates an allowed possibility.

Do not use these words casually when they could be interpreted as part of a technical contract.

If a document is purely explanatory, ordinary language may be clearer than unnecessarily formal normative language.

---

## Facts, rationale, and speculation

Clearly distinguish between:

* current facts;
* enforced invariants;
* design rationale;
* recommendations;
* future possibilities.

Do not write speculation as current behavior.

Do not describe a design preference as an invariant unless the implementation or an authoritative contract actually enforces it.

When explaining rationale, distinguish:

```text
The system does X.
```

from:

```text
The system does X because Y.
```

and:

```text
Future changes may consider Z.
```

These statements have different levels of authority and should not be blended together.

---

## Current behavior versus future work

Documentation under `docs/` should primarily describe current behavior.

Future plans, proposals, and unresolved design questions should not be mixed into operational or reference documentation as though they already exist.

If future work must be documented, clearly label it as such and keep it separate from current behavior.

Do not make readers infer whether a documented feature is already implemented.

---

## Generated and derived documentation

Do not manually maintain documentation that can reliably be generated from an authoritative source unless the generated output is unsuitable for the intended audience.

Examples may include:

* API schemas;
* configuration references;
* command references;
* generated protocol data.

When documentation is derived from code or another source:

* identify the authoritative source where useful;
* avoid manually editing generated output unless the generation process supports it;
* update the source rather than patching generated artifacts.

Do not claim generated documentation is authoritative when the implementation or schema is the actual source of truth.

---

## Links and link context

Links should tell readers what they will find.

Prefer:

```md
See the [deployment guide](../for-technicians/deployment.md).
```

over:

```md
See [here](../for-technicians/deployment.md).
```

Do not add links merely because a related page exists.

A link should help the reader:

* continue to a related task;
* find the authoritative source;
* understand a referenced concept;
* navigate to more detailed information.

Avoid dense paragraphs containing large numbers of links.

---

## External references

Use external references when they are authoritative or necessary to understand behavior outside Basil.

Prefer primary sources over third-party summaries.

Do not use external links as substitutes for documenting Basil-specific behavior.

If Basil depends on external behavior, document the Basil-facing implication and link to the external source for deeper detail.

Do not copy large portions of external documentation into the project.

---

## Stable links and documentation changes

Treat internal documentation links as part of the documentation interface.

When moving or renaming a page:

* update references to it;
* update navigation;
* check for broken relative links;
* avoid unnecessary renames that provide no organizational benefit.

Do not rename pages casually if existing references, bookmarks, or external links may depend on them.

---

## Terminology definitions

Do not redefine common terms on every page.

Define a term when:

* it is Basil-specific;
* it has multiple plausible meanings;
* misunderstanding it would affect usage or implementation;
* it is introduced for the first time in a context where no authoritative definition is nearby.

When a canonical glossary or terminology page exists, link to it rather than creating competing definitions.

---

## Abbreviations

Avoid introducing abbreviations unless they are established in the project or meaningfully improve readability.

On first use, expand unfamiliar abbreviations when doing so helps the intended audience.

Do not invent abbreviations for concepts that are mentioned only a few times.

Consistency is more important than shortening terminology.

---

## Diagrams and visual explanations

Use diagrams when relationships, flows, ownership, or topology are materially easier to understand visually.

Prefer text when a diagram would only restate a simple linear explanation.

A diagram should communicate something useful that is difficult to infer quickly from prose.

Keep diagrams synchronized with the current implementation.

Do not use decorative diagrams.

When a diagram contains a technical contract, ensure the same contract is also understandable without relying exclusively on the image.

---

## Tables

Use tables for structured comparison.

Good uses include:

* configuration options;
* supported versus unsupported behavior;
* version or compatibility matrices;
* component responsibilities;
* feature comparisons.

Do not use tables merely to force prose into columns.

Avoid tables with long paragraphs in most cells when normal headings or lists would be easier to read.

---

## Commands and copy-paste safety

Commands intended for copy-paste must be complete and safe in the documented context.

Do not omit required arguments merely to shorten examples.

When a command is destructive, environment-specific, or requires replacement values, make that clear before the command.

Clearly distinguish placeholders from literal values.

For example:

```bash
basil --config /path/to/config
```

should not leave the reader guessing whether `/path/to/config` is literal.

---

## Secrets and sensitive information

Never place real secrets, credentials, tokens, private keys, or production-only sensitive values in documentation examples.

Use obviously fictional placeholders.

Do not use examples that resemble real credentials closely enough to be mistaken for active values.

When documenting configuration involving secrets, explain where the value is expected to come from without encouraging insecure storage.

---

## Documentation for automated agents

Documents under `for-agents/` should be especially resistant to ambiguity.

Do not rely on phrases such as:

```text
do the usual thing
follow the existing pattern
handle this appropriately
keep it clean
```

without defining what those expectations mean.

Agent-facing documentation should identify, where relevant:

* the scope of the rule;
* the condition that triggers it;
* the expected outcome;
* important constraints;
* authoritative sources;
* verification expectations;
* exceptions.

However, do not prescribe an implementation strategy unless the strategy itself is a required constraint.

State the required result and boundaries, then allow the agent to determine the implementation approach.

---

## Avoid accidental implementation instructions

Documentation should distinguish between:

* what must be true;
* why it must be true;
* one possible way to make it true.

Do not accidentally turn a current implementation detail into a mandatory procedure unless it is intentionally part of the contract.

This is particularly important in `for-agents/` documentation.

Prefer:

```text
Localization changes must preserve the established hierarchy and naming conventions.
```

over:

```text
First inspect every localization file, then run a specific script, then edit the JSON manually.
```

unless that exact workflow is required.

---

## Humanization and natural writing

Documentation should sound like it was written by a knowledgeable human, not generated from a generic template.

Avoid:

* repetitive transitions;
* unnecessary summaries of the preceding section;
* inflated language;
* artificial formality;
* generic introductions;
* filler such as "it is important to note that";
* repetitive "This section..." constructions;
* writing that merely restates a heading.

Prefer direct, natural phrasing.

When creating or substantially rewriting documentation, use the available Humanizer skill when appropriate to review the final wording for unnatural, robotic, repetitive, or AI-generated phrasing.

Humanization must not:

* change technical meaning;
* weaken an invariant;
* alter command syntax;
* modify identifiers;
* remove required caveats;
* turn precise technical language into vague prose.

Technical precision takes priority over stylistic variation.

---

## Documentation review

Before considering documentation complete, review it as a reader rather than only as an author.

Check:

### Scope

* Does the page have a clear responsibility?
* Does it belong in the current directory?
* Does it duplicate another authoritative page?

### Accuracy

* Does every claim match the current implementation or authoritative source?
* Are assumptions and limitations stated where necessary?
* Are examples representative and current?

### Structure

* Is the hierarchy meaningful?
* Is information grouped by concept and ownership?
* Has unrelated content been flattened together?
* Has unnecessary hierarchy been introduced?

### Usability

* Can a reader find the action or answer they need quickly?
* Are prerequisites visible before they become necessary?
* Are verification and expected outcomes documented where useful?
* Are important failure modes discoverable?

### Writing

* Is the wording direct and natural?
* Is the page free of repetition and filler?
* Does it avoid generic or AI-generated phrasing?
* Are terms and vocabulary consistent?

### Maintenance

* Are links valid?
* Does the page duplicate information likely to drift?
* Is the authoritative source clear where it matters?
* Will future contributors know when this page needs updating?

Documentation should be treated as complete only when it is both technically accurate and usable by its intended audience.


Sau đó chuẩn hóa, viết lại/tổ chức lại hệ thống docs và các tài liệu nếu cần.