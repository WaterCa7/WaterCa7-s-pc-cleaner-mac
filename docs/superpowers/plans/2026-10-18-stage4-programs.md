# Этап 4 «Программы» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** раздел «Программы»: установленные программы (версия, разработчик и подпись, App Store, архитектура, размер,
последний запуск), удаление программы вместе с остатками в `~/Library` (всё — в Корзину, после подтверждения со
списком), свой деинсталлятор программы — первым, нежелательные программы, находки для «Главной».

**Architecture:** список — обход папок программ и чтение `Info.plist` (двоичные — через `plutil`); архитектура — чистый
разбор заголовка Mach-O основного файла (первые байты, без `lipo`); последний запуск — один вызов
`mdls -raw -name kMDItemLastUsedDate` на все программы (значения разделены `\0`); подпись — `CodeSignatureService`
без полной проверки файлов; размер — `FileWalker.Measure` в фоне. Остатки — чистые правила по точному bundle id и
имени над временной «домашней» папкой в тестах. Удаление — `ITrash` (программа, принадлежащая системе, — через Finder,
пароль спросит macOS); запущенная программа закрывается пользователем, принудительно — `ProcessesService`.

**Tech Stack:** как в этапах 1–3.

**Спецификация:** раздел «Программы». Уточнения этапа: (1) программы `/Applications/*/*.app` берутся только из папок,
которые сами не `.app` (папки производителей и «Утилиты»), символические ссылки не открываются; (2) удалить нельзя
встроенные программы Apple (`/System/Applications`), саму эту программу и то, что защищено SIP (ошибка Корзины
показывается как есть); (3) «Не запускалась больше 180 дней и занимает ≥ 500 МБ» и «только для Intel» — по одному
совету со списком программ, а не по совету на программу; (4) `Containers/<id>` проверяется только с полным доступом к
диску (иначе macOS спросит доступ к данным других программ) — без него на странице пояснение.

## Global Constraints

- Всё из этапов 1–3. Ни один тест не меняет систему: Корзина — подставная, `ps`, `kill`, `mdls`, `plutil`, `codesign`
  — через подставной исполнитель; тесты `[MacFact]` только читают (`/Applications`, `/System/Applications`, `lipo`,
  `mdls`).
- Удаление: подтверждение со списком (программа, размер, каждый отмеченный остаток), только в Корзину; без «Да» ничего
  не меняется. Остатки — только внутри `~/Library`, без перехода по ссылкам, никогда сама `~/Library` или её корневые
  папки.
- Остатки по точному bundle id отмечены; по имени — отмечены, только если нет другой программы с тем же именем;
  `Group Containers/<Team ID>.*` — без галочки.
- Нежелательная программа — «потенциально нежелательная», не «вирус».

## Задачи

### Task 1: Образцы и разбор

`tools/mac/capture-fixtures.sh` (сделано до плана): `Info.plist`, `lipo -archs`, `mdls` и `Contents` каждой программы,
списки папок `~/Library`. Из артефакта CI — образцы в `Fixtures/apps/` (после просмотра). `Modules/Programs/MachO.cs`:
`MachO.Read(ReadOnlySpan<byte> header) → MachOArchs` (флаги `Arm64`, `X86_64`, `Other`): тонкие 64/32-битные файлы,
`FAT_MAGIC`/`FAT_MAGIC_64` (big-endian, не больше 30 записей — иначе это не Mach-O, а, например, Java class),
`AppArchitecture` (Apple Silicon / Universal / только Intel / неизвестно). `AppParsers.LastUsed(mdlsRaw, count)` —
значения через `\0`, `(null)` → нет даты. Тесты: байты заголовков (тонкий arm64, тонкий x86_64, fat из двух, fat_64,
Java class, мусор, короткий файл), образцы `mdls`; `[MacFact]`: разбор совпадает с `lipo -archs` для программ в
`/Applications` и `/System/Applications`.

### Task 2: Список программ

`InstalledApp` (Path, Name, BundleId, Version, Executable, Location `Applications|UserApplications|Apple`, IsAppStore
(`Contents/_MASReceipt`), Architecture, LastUsed, Signature, Uninstaller, PupReason, Size (позже), IsProtected).
`AppCatalog.ListAsync(includeApple, ct)`: папки, `Info.plist`, Mach-O, `mdls` одним вызовом, подписи (по 4).
`Uninstaller`: `Uninstall*.app` / `*Uninstaller.app` внутри `Contents` (`Contents/Resources`, `Contents/Library`) или
рядом с программой в той же папке. `MeasureAsync(app)` — размер в фоне. Тесты на временной папке с поддельными
программами (plist, исполняемый файл с заголовком, `_MASReceipt`, папка производителя с двумя программами и
деинсталлятором, ссылка на программу — пропускается, `.app` без `Info.plist` — показывается с пометкой).

### Task 3: Остатки

`LeftoverFinder.Find(app, home, otherApps, fdaGranted) → IReadOnlyList<Leftover(Path, What, Checked, Note)>`: по id —
`Application Support/<id>`, `Caches/<id>`, `Preferences/<id>.plist`, `Preferences/ByHost/<id>.*.plist`,
`Saved Application State/<id>.savedState`, `HTTPStorages/<id>`, `HTTPStorages/<id>.binarycookies`, `WebKit/<id>`,
`Cookies/<id>.binarycookies`, `LaunchAgents/<id>*.plist`, `Containers/<id>` (с полным доступом); по имени —
`Application Support/<имя>`, `Caches/<имя>`, `Logs/<имя>` (галочка — если имя не делит другая программа);
`Group Containers/<TeamId>.*` — без галочки. Ссылки не берутся; размеры — `FileWalker.Measure`. Тесты: временная
«домашняя» папка с остатками двух программ одного разработчика, общая папка по имени, ссылка вместо папки, без
полного доступа — контейнер не проверяется.

### Task 4: Удаление

`ProgramsService.UninstallAsync(app, leftovers, ct) → UninstallResult(Ok, Freed, Message, Failed)`: защищённые —
отказ; программа → `ITrash` (ошибка — остатки не трогаются); затем отмеченные остатки → `ITrash`; `Log.Action` на
каждый. `RunningCheck`: процессы текущего пользователя внутри программы (`ProcessesService.ListAsync`);
`QuitAsync(app, force)` — `TerminateAsync` каждого. Модель страницы: подтверждение со списком → если запущена —
«Закройте X и нажмите «Да»» (повтор проверки), затем отдельное «Завершить принудительно?» → если есть свой
деинсталлятор — предложить его (открыть и остановиться) → удаление. Тесты: при «Нет» ничего не меняется, порядок
действий, ошибка Корзины на программе — остатки на месте, защищённые программы.

### Task 5: Нежелательные программы и «Главная»

`MacPups` — дополнить списком из спецификации (VSearch — только по bundle id `com.vsearch…`, имя слишком общее).
`ProgramsContributor` (модуль «Программы», страница `Programs`): нежелательная — важная (по одной на программу); «не
запускалась больше 180 дней и занимает ≥ 500 МБ» — совет со списком (размер считается только для давно не
запускавшихся); «только для Intel» — совет со списком. Тесты порогов.

### Task 6: Страница, CI

`ProgramsViewModel` (Key `Programs`, «Программы», Icon `Apps`): поиск, фильтр (все / давно не запускались / только
для Intel / нежелательные / из App Store), сортировка (имя / размер / последний запуск), подробности выбранной (версия,
id, путь, подпись и источник, архитектура, размер, последний запуск, остатки с галочками и размерами, пояснение про
полный доступ); «Удалить…», «Показать в Finder», «Найти в интернете»; встроенные программы Apple — отдельным списком
по флажку, только просмотр. Тесты модели (нет «Да» — ничего не меняется), окна, снимки; CI на обеих машинах; `.app`
пользователю.
