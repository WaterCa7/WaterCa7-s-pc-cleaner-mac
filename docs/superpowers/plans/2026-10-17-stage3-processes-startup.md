# Этап 3 «Процессы и автозагрузка» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** раздел «Процессы и автозагрузка»: что запущено (с подписью и подсказками), завершение своих процессов,
записи автозагрузки launchd и объекты входа с обратимым отключением, удаление сломанных пользовательских записей в
Корзину, резервные копии перед каждым изменением и их восстановление в «Настройках», находки для «Главной».

**Architecture:** разбор вывода `ps`, `codesign`, `spctl`, `launchctl` — чистые парсеры на образцах с машин CI;
правила подсказок — чистые функции; действия — через `ICommandRunner` (`/bin/kill`, `/bin/launchctl`, `osascript`) и
`AdminPrompt` (окно пароля macOS, команды как аргументы). Резервные копии — `BackupStore` в
`~/Library/Application Support/WaterCa7sPcCleaner/Backups`.

**Tech Stack:** как в этапах 1–2.

**Спецификация:** разделы «Процессы и автозагрузка», «Сервисы Core» (`AdminPrompt`, `CodeSignature`, `Backups`).
Уточнения этапа: (1) запись автозагрузки без подписи разработчика (или ad-hoc), программа которой в папке пользователя,
— **средняя**, а не важная: так подписаны службы Homebrew в `/opt/homebrew`; **важная** — только при сильных
признаках: временная папка, `/Users/Shared`, скрытая папка, маскировка под Apple (метка `com.apple.*`, а программа не
от Apple), известная нежелательная программа; (2) процессы на «Главной» — только с сильными признаками.

## Global Constraints

- Всё из этапов 1–2. Ни один тест не меняет систему: `launchctl`, `kill`, `osascript` — только через подставной
  исполнитель; тесты `[MacFact]` читают (`ps`, `codesign`, `launchctl print`).
- Завершать можно только процессы текущего пользователя; нельзя PID 0/1, `kernel_task`, `launchd`, `WindowServer`,
  `loginwindow`, саму программу.
- Отключение — `launchctl disable` (+ `bootout`), обратимо; удаление — только `~/Library/LaunchAgents`, в Корзину,
  после копии; системные записи (`/Library/LaunchDaemons`) — только отключение и только через окно пароля.
- Перед любым изменением автозагрузки — копия в `Backups`; не удалось сохранить копию — изменение отменяется.
- Объекты входа читаются только по кнопке на странице (macOS спросит разрешение управлять System Events).

## Задачи

### Task 1: Подпись кода

`Core/CodeSignature.cs`: `SignatureKind { Apple, AppStore, DeveloperId, OtherCertificate, AdHoc, Unsigned, Invalid,
Unknown }`; `SignatureInfo(Kind, Authority?, TeamId?, Identifier?, Notarized?)` + `Text` по-русски («Apple»,
«App Store», «Developer ID: Microsoft Corporation (UBF8T346G9)», «ad-hoc (без разработчика)», «без подписи», «подпись
повреждена»); `CodeSignatureParser.Parse(codesignDv, exitCode)`, `ParseVerify(exit, stderr)`,
`ParseSpctl(output, exit)`; `CodeSignatureService(ICommandRunner)` — `GetAsync(path, ct)` (`codesign -dv
--verbose=2` + `codesign --verify --strict`, кэш по пути и времени изменения), `AssessAsync(appPath, ct)` (`spctl
--assess --type execute -vv`). Образцы: `/bin/ls`, Calculator, dotnet (Developer ID), программа с самоподписанным
сертификатом, ad-hoc, «not signed at all», повреждённая подпись, `spctl` accepted/rejected/Notarized Developer ID.

### Task 2: Окно пароля администратора

`Core/AdminPrompt.cs`: `AdminCommand(Exe, Args)`; `AdminPrompt(ICommandRunner).RunAsync(string reason,
IReadOnlyList<AdminCommand> commands, ct) → AdminResult(Ok, Cancelled, Error)`. Скрипт постоянный:
`on run argv` — первая строка — подсказка окна, дальше элементы команд через разделитель `\u0001&&\u0001`, каждый —
`quoted form of`, команды соединяются `&&`; `do shell script … with prompt … with administrator privileges`. Аргумент,
равный разделителю, — отказ. Отмена (-128) → `Cancelled`. Тесты: аргументы с кавычками/пробелами/`$()`/`;` не
попадают в текст скрипта; две команды → один вызов; отмена; ошибка.

### Task 3: Резервные копии

`Core/BackupStore.cs`: `BackupRecord(Id, Time, Kind, Title, OriginalPath?, StoredFile, Details)`; `Kind`:
`LaunchdPlist`, `LoginItem`; `SavePlist(path, title)` (копия + запись в `index.json`), `SaveLoginItem(name, path,
hidden)`, `List()`, `Remove(id)`. Ошибка записи → исключение, действие отменяется. Тесты на временной папке:
копия байт-в-байт, индекс переживает перезапуск, повреждённый индекс — пустой список и запись в журнал.

### Task 4: Процессы

`Modules/Processes/ProcessParsers.cs`: `ParseComm(text)`, `ParseArgs(text)`, `ParseElapsed("2-03:04:05")`;
`ProcessInfo(Pid, Ppid, Uid, CpuPercent, MemoryBytes, Elapsed, Path, CommandLine)`; `ProcessRules`:
`IsSystemPath`, `IsProtected(info, ownPid, ownUid)`, `Hints(info, signature?, fileExists) → IReadOnlyList<Hint(Text,
Strong)>`; `MacPups` (`Core/MacPups.cs`) — известные нежелательные программы для Mac с причинами (используется и на
этапе 4). `ProcessesService(ICommandRunner, CodeSignatureService)`: `ListAsync(ct)` (подписи — только для
несистемных путей), `TerminateAsync(pid, force)` (`/bin/kill -TERM|-KILL`), `IsAliveAsync(pid)` (`/bin/kill -0`),
`Sha256Async(path)`. Тесты: образцы `ps` (реальные с машин CI + свои с пробелами в путях), защищённые процессы,
подсказки (временная папка, `/Users/Shared`, скрытая папка, удалённый файл, PUP, ad-hoc вне `/Applications`).

### Task 5: Автозагрузка

`Modules/Startup/LaunchdParsers.cs`: `ParseList(launchctl list)`, `ParsePrintDisabled(text)`, `ParsePrint(text)` →
`state`, `pid`, `last exit code`. `LaunchEntry(Source, PlistPath, Label, Program, Arguments, RunAtLoad, KeepAlive,
State(Loaded/Running/Pid/LastExit), Disabled, Broken(Problem), Signature, Hints)`; `Source`: `UserAgent`
(`~/Library/LaunchAgents`), `GlobalAgent` (`/Library/LaunchAgents`), `Daemon` (`/Library/LaunchDaemons`), `Apple`
(`/System/Library/Launch*`, по умолчанию скрыты). `StartupService(home, ICommandRunner, CodeSignatureService,
BackupStore, AdminPrompt, ITrash, uid)`: `ListAsync(includeApple, ct)`, `DisableAsync(entry)`, `EnableAsync(entry)`,
`RemoveAsync(entry)` (только `UserAgent`), `LoginItemsAsync()` / `RemoveLoginItemAsync(item)` / `AddLoginItemAsync`
(System Events, имена и пути — аргументы). Тесты: образцы plist с машин CI, сломанные записи (нет программы, plist не
читается, нет метки), отключённые, запущенные; команды отключения/включения для своего домена и через окно пароля
для демонов; удаление: копия → `bootout` → Корзина; удаление системной записи — отказ; копия не сохранилась — ничего.

### Task 6: Находки для «Главной»

`ProcessesContributor` (модуль «Процессы и автозагрузка»): процесс с сильным признаком — важная; запись с сильным
признаком — важная; сломанная пользовательская запись — средняя (`AutoFixSafe`: отключить); запись без подписи
разработчика в папке пользователя — средняя; больше 15 сторонних записей — совет. Тесты порогов и исправления.

### Task 7: Резервные копии в «Настройках»

Список копий (дата, что, откуда), «Показать в Finder», «Восстановить»: plist — на место (пользовательский — копией,
системный — через окно пароля), затем `launchctl enable` + `bootstrap`; объект входа — снова добавить. Подтверждение
перед восстановлением. Тесты модели.

### Task 8: Страница, CI

`ProcessesViewModel` (Key `Processes`, «Процессы и автозагрузка», Icon `Pulse`), вкладки «Процессы» (поиск, «Скрыть
системные», сортировка по ЦП/памяти, подробности: путь, подпись, подсказки; «Завершить», «Показать в Finder»,
«Найти в интернете», «VirusTotal») и «Автозагрузка» (записи по источникам, «Показать записи Apple», состояние,
подпись, подсказки; «Отключить»/«Включить», «Удалить» для своих, «Показать объекты входа», «Объекты входа в
настройках»). Тесты модели (нет «Да» — ничего не меняется), окна, снимки; CI на обеих машинах; `.app` пользователю.
