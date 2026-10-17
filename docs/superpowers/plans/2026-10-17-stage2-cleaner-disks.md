# Этап 2 «Очистка и диски» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** раздел «Очистка и диски»: кэши программ, журналы, кэши браузеров и разработки удаляются безопасно, Корзина
очищается через Finder, видны тома, «очищаемое» место и снимки Time Machine, находятся большие папки, большие файлы и
старые загрузки (в Корзину); «Главная» получает находки о кэшах и Корзине.

**Architecture:** чистые части (каталог категорий, правила отбора, разбор `df`/`tmutil`/`ps`, итоги) тестируются на
временной «домашней папке» и образцах; всё, что трогает macOS напрямую, — за интерфейсами: `IFileRemover`
(`openat/unlinkat` без перехода по ссылкам), `ITrash` (`NSFileManager`, запасной путь — Finder), `MacNative`
(вызовы Objective-C), `ICommandRunner`. Настоящие реализации проверяются тестами `[MacFact]` на машинах CI во
временных папках.

**Tech Stack:** как в этапе 1; P/Invoke в `libc` и `libobjc` (Foundation).

**Спецификация:** `docs/superpowers/specs/2026-10-17-mac-version-design.md`, разделы «Очистка и диски», «Правила
безопасности». Уточнение этапа: Homebrew — только папка загрузок `~/Library/Caches/Homebrew`, без `brew cleanup`
(та ещё удаляет старые версии пакетов). На «Главной» место на диске и находки очистки идут от одного раздела
(`CleanerContributor` заменяет `DiskSpaceContributor`).

**Исполнение:** inline в этой сессии; тесты пишутся первыми по случаям, перечисленным в каждой задаче.

## Global Constraints

- Всё из этапа 1 (тесты не меняют систему, русский интерфейс, полный путь к утилитам, LF, коммиты с Co-Authored-By).
- Удаляются только файлы внутри корня цели; корень цели и папки верхнего уровня категорий не удаляются; символические
  ссылки не проходятся и не удаляются; файлы кэшей моложе 1 часа и журналы моложе 7 дней пропускаются.
- Кэши браузеров — только папки с именами `Cache`, `Code Cache`, `GPUCache`, `DawnCache`, `DawnGraphiteCache`,
  `DawnWebGPUCache`, `ShaderCache`, `GrShaderCache`, `GraphiteDawnCache`, `cache2` в известных корнях; кэш запущенного
  браузера пропускается.
- В `~/Library/Caches` не трогаются `com.apple.*`, папки других категорий и кэши запущенных программ.
- Безвозвратное удаление — только кэши/журналы/кэши разработки (после подтверждения) и «Очистить корзину» (отдельное
  подтверждение); файлы пользователя (большие файлы, старые загрузки) — только в Корзину.
- Анализ для «Главной» не обходит «Рабочий стол», «Документы», «Загрузки» (не вызывает запросов macOS).

## Файлы

| Файл | Ответственность |
|---|---|
| `Core/MacNative.cs` | мост к Objective-C: классы, селекторы, `NSString`, пул autorelease, доступное место тома |
| `Core/Trash.cs` | `ITrash`, `TrashOutcome`, `MacTrash` (NSFileManager → Finder) |
| `Core/FileRemover.cs` | `IFileRemover`, `RemoveOutcome`, `UnixFileRemover` (libc), `ManagedFileRemover` |
| `Core/FileWalker.cs` | обход без ссылок: файлы с размером и датой, размер папки, вложенные папки снизу вверх |
| `Modules/Cleaner/RunningApps.cs` | какие программы запущены (`ps` → `.app` → Info.plist) |
| `Modules/Cleaner/CleanerCatalog.cs` | категории и цели очистки |
| `Modules/Cleaner/CleanerScanner.cs` | что можно удалить в каждой категории (только чтение) |
| `Modules/Cleaner/CleanerService.cs` | проверка всех категорий, очистка, «Очистить корзину» |
| `Modules/Cleaner/CleanerContributor.cs` | находки «Очистки и дисков» для «Главной» (вместе с местом на диске) |
| `Modules/Cleaner/DiskParsers.cs`, `DiskService.cs` | тома (`df -k -P`), снимки Time Machine, «очищаемое» место |
| `Modules/Cleaner/SpaceAnalyzer.cs` | самые большие папки, большие файлы, старые загрузки |
| `Modules/Cleaner/CleanerViewModel.cs`, `CleanerItems.cs`, `CleanerView.axaml(.cs)` | страница с вкладками «Очистка» и «Диски и место» |

---

### Task 1: Мост к Objective-C, Корзина, доступное место тома

- `MacNative`: `IsAvailable` (только macOS, Foundation загружена через `NativeLibrary.Load`), `Class`, `Selector`,
  `Send…` (P/Invoke `objc_msgSend` с точными сигнатурами, `BOOL` — `I1`), `NSString(string)`, `ToManaged(IntPtr)`,
  `AutoreleasePool : IDisposable` (`objc_autoreleasePoolPush/Pop`), `long? AvailableForImportantUsage(string path)`
  (`NSURL getResourceValue:forKey:error:` с `NSURLVolumeAvailableCapacityForImportantUsageKey`).
- `ITrash.MoveAsync(path, ct) → TrashOutcome(bool Ok, string? Error)`; `MacTrash(ICommandRunner)`: `NSFileManager
  trashItemAtURL:resultingItemURL:error:`; при отказе из-за прав — Finder: `osascript -e 'on run argv' -e 'tell
  application "Finder" to delete (POSIX file (item 1 of argv) as alias)' -e 'end run' -- <путь>`; не macOS — отказ.
- Тесты: `[MacFact]` строка туда-обратно через `NSString` (кириллица); `[MacFact]` временный файл уходит в Корзину и
  исчезает с места; `[MacFact]` доступное место `/` > 0; отказ `NSFileManager` → вызывается Finder с путём как
  аргументом (через подставной исполнитель и подставную функцию NSFileManager); не macOS — `Ok=false` с русским текстом.

### Task 2: Удаление без перехода по ссылкам, обход папок

- `IFileRemover`: `RemoveOutcome RemoveFile(string root, string relative)`, `RemoveOutcome RemoveEmptyDir(string root,
  string relative)`; `RemoveOutcome { Deleted, Gone, Denied, Busy, Link, Failed }`.
- `UnixFileRemover`: `open(root, O_RDONLY|O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC)`, по одной папке `openat(… O_NOFOLLOW)`,
  `unlinkat(fd, name, 0 | AT_REMOVEDIR)`; `ELOOP/ENOTDIR` → `Link`, `ENOENT` → `Gone`, `EACCES/EPERM` → `Denied`,
  `EBUSY` → `Busy`, `ENOTEMPTY` → `Failed`; пустой или `..`/абсолютный относительный путь — отказ.
- `ManagedFileRemover` (не macOS: разработка и тесты на Windows): каждая папка пути проверяется на ссылку
  (`LinkTarget`), потом `File.Delete` / `Directory.Delete` (непустая не удаляется).
- `FileRemover.ForCurrentOs()`.
- `FileWalker.Files(root, ct, skipDir?)` → `FileEntry(FullPath, Relative, Length, LastWriteUtc)` (ссылки не
  отдаются и не проходятся, недоступные папки пропускаются и считаются); `FileWalker.Directories(root)` — вложенные
  папки снизу вверх; `FileWalker.Measure(root, ct) → (Bytes, Files)`.
- Тесты (оба удалителя, общие случаи): файл в подпапке удаляется; корень не удаляется; `..` и абсолютный путь — отказ;
  несуществующий — `Gone`; непустая папка не удаляется. `[MacFact]`: папка на пути заменена символической ссылкой на
  другую папку → `Link`, файл в той папке цел; ссылка на файл не проходится. Обход: размеры, даты, вложенность,
  ссылка не отдаётся (`[MacFact]`), снизу вверх.

### Task 3: Запущенные программы

- `RunningApps.BundlesFromPs(string psComm) → IReadOnlyList<string>` — внешние `.app` из путей вида
  `…/X.app/Contents/MacOS/…` (помощники внутри `.app` считаются за внешнюю программу), без повторов.
- `RunningAppsService(ICommandRunner).LoadAsync(ct) → RunningAppSet` — `/bin/ps -xww -o comm=`, Info.plist каждой
  программы: `CFBundleIdentifier`, `CFBundleName`, `CFBundleExecutable`; `RunningAppSet.Matches(string folderName)`
  (без учёта регистра); `RunningAppSet.IsRunning(string executableName)`.
- Тесты: образец `ps` (Chrome с помощниками, Safari, Xcode, процессы без `.app`); пустой вывод; ошибка `ps` → пустой
  набор и запись в журнал; совпадение по id, имени, исполняемому файлу.

### Task 4: Категории и проверка (только чтение)

- `CleanerCatalog`: `AppCaches` «Кэши программ», `Logs` «Журналы и отчёты о сбоях», `Browsers` «Кэши браузеров»,
  `Developer` «Кэши разработки», `Trash` «Корзина»; `CleanAction { Delete, EmptyTrash }`.
- `CleanTarget(Title, Root, Files, Bytes, SkipReason, ShowOnly)`; `CategoryScan(Category, Targets, State, Note)` с
  `Bytes`, `FileCount`, `ShowOnlyBytes`; `ScanState { Ready, Empty, NeedsFullDiskAccess, Error }`.
- `CleanerScanner(string home, RunningAppSet running, DateTime nowUtc, FdaState fda)`, `Scan(category, ct)`:
  - кэши: каждая папка/файл `~/Library/Caches` (кроме исключений) — своя цель; запущенная программа → `SkipReason`;
  - журналы: `~/Library/Logs` без своей папки и без `DiagnosticReports`, отдельно `DiagnosticReports`; старше 7 дней;
  - браузеры: Chrome, Edge, Brave, Opera, Яндекс (профили `Default`, `Profile N`, `Guest Profile`, `System Profile`),
    Firefox (`Profiles/*/cache2`), Safari (`~/Library/Caches/com.apple.Safari`, контейнер — только с полным доступом);
  - разработка: DerivedData (Xcode запущен → пропуск), `CoreSimulator/Caches` (Simulator), Homebrew, npm, pip, Yarn,
    CocoaPods; `ShowOnly`: Xcode Archives, iOS DeviceSupport;
  - Корзина: `~/.Trash`; нет доступа → `NeedsFullDiskAccess`.
- Тесты на временной домашней папке: исключения `com.apple.*` и папок других категорий; свежие файлы пропущены;
  запущенная программа пропущена с причиной; пароли/история браузера (`Login Data`, `History`, `Cookies`,
  `Bookmarks`, `Local Storage`) не попадают; Firefox `cache2`; Safari без полного доступа — нет целей и заметка;
  Корзина без доступа — `NeedsFullDiskAccess`; `ShowOnly` не входит в `Bytes`; пустая категория — `Empty`.

### Task 5: Очистка и «Очистить корзину»

- `CleanerService(string home, ICommandRunner runner, IFileRemover remover, Func<FdaState> fda)`:
  `ScanAllAsync(ct)`, `ScanAsync(category, ct)`, `CleanAsync(IReadOnlyCollection<CleanerCategory>, IProgress<double>?, ct)
  → CleanResult(FreedBytes, Deleted, Skipped, Message)` — заново проверяет выбранные категории (запущенные программы и
  возраст файлов — на момент очистки), удаляет файлы, затем опустевшие вложенные папки (не корни целей), пишет
  `Log.Action`; `EmptyTrashAsync(ct) → string` — `osascript -e 'tell application "Finder" to empty the trash'`.
- Тесты: только файлы целей удалены (подставной `ManagedFileRemover` во временной папке), корни целей на месте, пустые
  подпапки убраны; `ShowOnly` и пропущенные цели не тронуты; итог и текст («Удалено 1,5 МБ (3 файла)», «Пропущено 1»);
  отмена посередине — остановка и честный итог; Корзина — правильная команда, ошибка Finder → «Не удалось …».

### Task 6: Находки для «Главной»

- `CleanerContributor(CleanerService, MacInfoService)` (`ModuleName` «Очистка и диски»): место на диске
  (`DiskSpaceRules`), кэши+журналы+браузеры ≥ 500 МБ — средняя, ≥ 100 МБ — низкая (`FixAsync` = очистка этих трёх
  категорий без повторного вопроса, `AutoFixSafe`, `ReclaimableBytes`); разработка ≥ 2 ГБ — низкая, без исправления,
  `TargetPageKey = Cleaner`; Корзина ≥ 1 ГБ — низкая, без исправления. `PageFactory` использует его вместо
  `DiskSpaceContributor`.
- Тесты: пороги; исправление удаляет и возвращает текст, который `HealthScoring.LooksLikeFailure` не считает неудачей;
  ошибка удаления → текст неудачи.

### Task 7: Диски, снимки, «очищаемое» место

- `DiskParsers.ParseVolumes(dfAll) → IReadOnlyList<VolumeUsage(Device, Mount, Total, Used, Free)>` — только `/dev/…`;
  `UserVolumes` — `/System/Volumes/Data` (как «Macintosh HD») и `/Volumes/*`; `ParseSnapshots(tmutil) →
  IReadOnlyList<DateTime>` (`com.apple.TimeMachine.YYYY-MM-DD-HHMMSS.local`).
- `DiskService(ICommandRunner, Func<string, long?> availableForImportantUsage)`: `LoadAsync(ct) → DisksInfo(Volumes,
  Snapshots)`; том: имя, всего, свободно, доступно с учётом очищаемого (если больше свободного).
- Тесты: образец `df -k -P` с системными томами, внешним диском с пробелом в имени, `map auto_home`, `devfs`; снимки
  (0, 3, мусор); «очищаемое» меньше свободного не показывается.

### Task 8: Анализ места

- `SpaceAnalyzer(string home)`: `TopFoldersAsync(ct)` — папки верхнего уровня домашней папки (кроме `.Trash`) с
  размером, по убыванию; `LargeFilesAsync(minBytes, ct)` — в домашней папке без `Library` и скрытых папок, не больше
  200, по убыванию; `OldDownloadsAsync(olderThanDays, nowUtc, ct)` — `~/Downloads` (файлы и папки верхнего уровня).
- Тесты на временной папке: порядок, порог, лимит, возраст, пропуск `Library` и ссылок, недоступная папка не ломает
  обход.

### Task 9: Страница

- `CleanerViewModel(CleanerService, DiskService, SpaceAnalyzer, ITrash, MacShell)` (Key `Cleaner`, Icon `Broom`):
  вкладка «Очистка» — категории с галочками (Корзина без галочки, отдельная кнопка), размер, состояние, подробности
  целей; «Проверить снова», «Очистить выбранное» (подтверждение со списком категорий и размеров, «без Корзины — эти
  файлы программы создадут заново»), «Очистить корзину» (отдельное подтверждение: «безвозвратно»), «Открыть настройки»
  для полного доступа; вкладка «Диски и место» — тома с полосой занятости, «очищаемое» место, снимки Time Machine,
  «Самые большие папки», «Большие файлы» (порог 100 МБ / 500 МБ / 1 ГБ), «Старые загрузки» с «Показать в Finder» и
  «В Корзину» (подтверждение). Предупреждение о запросах macOS — над кнопками анализа. `PreviewScanAsync` — проверка
  категорий и тома (без анализа папок пользователя).
- Тесты: при «Нет» ничего не удаляется; выбранные категории уходят в `CleanAsync`; итог в журнале и на странице;
  «В Корзину» после «Да» вызывает `ITrash` и убирает строку; отказ Корзины — текст ошибки.

### Task 10: Вид, CI, проверка

- `CleanerView.axaml` (вкладки, карточки, списки), шаблон в `App.axaml`, `PageFactory`.
- Тесты окна (страница открывается на образцах, вкладки переключаются), снимки страницы в `MAC_SCREENSHOTS_DIR`.
- `capture-fixtures.sh`: `df -k -P` (все тома), `ls -la ~/Library/Caches`, `ls -la ~/Library/Logs`, доступ к `~/.Trash`.
- CI зелёный на обеих машинах, скриншоты просмотрены, `.app` — пользователю на проверку: очистка, Корзина и «Вернуть»,
  запросы доступа к папкам, «Открыть настройки».
