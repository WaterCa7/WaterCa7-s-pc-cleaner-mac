# Архитектура WaterCa7s PC Cleaner для Mac

Программа на C# и Avalonia 12 (.NET 10, macOS 14+, Apple Silicon). Код на английском, весь интерфейс — на русском.
Как устроена Windows-версия — в [WaterCa7/WaterCa7-s-pc-cleaner](https://github.com/WaterCa7/WaterCa7-s-pc-cleaner)
(`docs/ARCHITECTURE.md`); здесь те же имена и тот же API, где это возможно. Решения и этапы —
`docs/superpowers/specs/2026-10-17-mac-version-design.md`.

```
WaterCa7sPcCleaner.Mac.sln
src/PcCleaner.Mac/
  Program.cs              запуск окна или режима --screenshots
  App.axaml(.cs)          тема, шаблоны страниц, меню macOS, обработчики ошибок, окна диалогов
  MainWindow.axaml        боковое меню, заголовок страницы, полоса занятости с «Отмена», строка состояния
  MainViewModel.cs        список страниц и навигация (INavigator)
  Themes/                 Theme.axaml (цвета и кисти), Styles.axaml (классы стилей), Icons.axaml (значки Fluent UI)
  Shared/                 копии файлов Windows-версии (откуда — Shared/SOURCE.md)
  Core/                   общие сервисы (ниже)
  Modules/<Key>/          раздел = Service + ViewModel + View
  Shell/                  фабрика страниц, конвертеры, диалог, кольцо оценки, будущие разделы, скриншоты
  Assets/                 AppIcon.icns, лицензия значков
tests/PcCleaner.Mac.Tests/  xUnit v3 + Avalonia.Headless; Fixtures/ — сохранённый вывод утилит macOS
tools/mac/                Info.plist, make-app.sh, sign.sh, capture-fixtures.sh, make-icon.ps1
```

## Разделы

| Key        | Раздел                  | Этап | Проверка для «Главной» (IHealthContributor) |
|------------|-------------------------|------|---------------------------------------------|
| Dashboard  | Главная                 | 1    | — (собирает все остальные)                  |
| Cleaner    | Очистка и диски         | 2    | CleanerContributor                          |
| Processes  | Процессы и автозагрузка | 3    | ProcessesContributor                        |
| Programs   | Программы               | 4    | ProgramsContributor                         |
| Security   | Безопасность и сеть     | 5    |                                             |
| Settings   | Настройки               | 1    | —                                           |

Страницы создаёт `Shell/PageFactory.CreateMain(runner, settings, startupFolders?, appFolders?)`; разделы, которых ещё нет, —
`UpcomingPageViewModel`. Новый раздел: сервис + `PageViewModel` + `UserControl` с `x:DataType`, шаблон в
`App.axaml` (`Application.DataTemplates`), строка в `PageFactory`.

## Core API (кратко)

- `ICommandRunner.RunAsync(exe, args, timeout, ct)` → `CommandResult(ExitCode, StdOut, StdErr, TimedOut)` — **вся работа с
  macOS идёт через него**: утилиты только по полному пути (`/bin/launchctl`), аргументы списком, без shell, вывод на
  английском в UTF-8 (`LC_ALL=en_US.UTF-8`). Не запустилась — код 127. В тестах — `FakeCommandRunner` с образцами.
- `Plist.ParseDict(xml)` / `PlistFile.ReadAsync(path, runner, ct)` (двоичные — через `plutil`), `GetString/GetBool/
  GetLong/GetDict/GetArray/GetStrings`.
- `PageViewModel` — `Title`, `Subtitle`, `Icon` (имя значка `Icon.<имя>` из `Icons.axaml`), `Key`, `IsBusy/BusyText/
  Progress`, `RunBusyAsync(text, ct => …)`, `ReportProgress`, `OnNavigatedToAsync`, `OnNavigatedFrom`,
  `PreviewScanAsync(ct)` (только чтение!).
- `HealthIssue` / `IHealthContributor.AnalyzeAsync(ct)` (общие файлы) — находки для «Главной». Анализ только читает
  систему и не вызывает запросов разрешений macOS.
- `ObservableObject.Set`, `RelayCommand`, `AsyncRelayCommand`, `CommandBase.Refresh()` (пересчитать `CanExecute`).
- `Dialogs.ConfirmAsync/AskAsync/InfoAsync/ErrorAsync` — **каждое изменение системы подтверждается через
  `ConfirmAsync`**. Пока программа не подключила окна (тесты, скриншоты), на всё отвечается «Нет».
- `Log.Info/Warn/Error/Action` — `Action` для каждого изменения системы. Файл — `~/Library/Logs/WaterCa7sPcCleaner`.
- `ErrorText.For/Sentence(ex)` — русский текст ошибки; системные исключения переводятся всегда, даже если в их тексте
  есть кириллица из пути.
- `AppPaths.Home/DataDir/LogsDir/BackupsDir/UpdatesDir/SettingsFile` (в тестах — временная папка),
  `AppSettings.Load/Save/GetModuleValue/SetModuleValue`.
- `MacShell.OpenAsync(адрес)` / `RevealAsync(путь)` — `open` и «Показать в Finder».
- `FullDiskAccess.Check(home)` → `Granted/Denied/Unknown`; `SystemSettingsLinks` — адреса панелей настроек.
- `Ui.Post/Invoke/InvokeAsync` — в UI-потоке (без приложения — сразу).
- `CodeSignatureService.GetAsync(path, ct, verify)` → `SignatureInfo(Kind, Authority, TeamId, Identifier)` (Apple, App
  Store, Developer ID, ad-hoc, без подписи, повреждена); `AssessAsync` — вердикт Gatekeeper. «Повреждена» — только
  когда codesign прямо говорит, что файлы изменены (таймаут или нет доступа — не повреждение).
- `AdminPrompt.RunAsync(reason, commands, ct)` — окно пароля macOS; скрипт AppleScript постоянный, команды и их
  аргументы передаются ему как данные (`quoted form of`), отмена — `Cancelled`.
- `BackupStore.SavePlist/SaveLoginItem/List/Remove` — копии перед изменениями автозагрузки
  (`Backups/<id>/` + `index.json`); не удалось сохранить — исключение, и изменение не делается.
- `MacPups.Match(names…)` — известные нежелательные программы для Mac (используется и в «Программах»).
- `WebLinks.Search/VirusTotal` — страницы, которые открываются по кнопке (VirusTotal — по SHA-256, файл не отправляется).

## Процессы и автозагрузка

- `ProcessesService`: `ps` (comm и args отдельно, объединение по PID), подписи — один раз на программу (.app),
  без полной проверки файлов; `ProcessRules` — системные пути, кого нельзя завершать, подсказки (`Hint(Text, Strong)`).
  Завершение — сначала проверка, что PID всё ещё та же программа, затем `kill -TERM`, после «Да» — `-KILL`.
- `StartupService`: plist из `~/Library/LaunchAgents`, `/Library/LaunchAgents`, `/Library/LaunchDaemons` (и macOS — по
  флажку, только просмотр) + службы, зарегистрированные программами (`launchctl list` → `print`). Состояние —
  `launchctl list` / `print`, отключённые — `print-disabled`. Для скриптов (`sh -c`, python, osascript) проверяется
  файл, который они запускают. Действия: `disable`(+`bootout`), `enable`(+`bootstrap`), удаление своих — в Корзину;
  демоны — через `AdminPrompt`; всё — после копии в `BackupStore`.
- `LoginItemsService` — объекты входа через System Events (только по кнопке; имена и пути — аргументы скрипта).
- `BackupRestorer` — «Восстановить» в «Настройках»: отменяет изменение (включает снова, возвращает plist, снова
  добавляет объект входа).

## Программы

- `AppCatalog`: `/Applications/*.app`, `/Applications/<папка>/*.app`, `~/Applications`, встроенные `/System/Applications`
  — по флажку, только просмотр; ссылки не открываются (Safari и псевдонимы Xcode — ссылки). `Info.plist`, `_MASReceipt`,
  архитектура — `MachO.Read` по заголовку основного файла, последний запуск — один вызов `mdls` на все программы
  (значения через ` `), подпись — без полной проверки файлов, свой деинсталлятор, `MacPups`. Размер — `MeasureAsync`
  в фоне.
- `LeftoverFinder`: остатки в `~/Library` только по точному bundle id и точному имени (общие папки и имена других
  программ — без галочки), `Containers` — только с полным доступом, ссылки не берутся.
- `ProgramsService.UninstallAsync`: программа → Корзина, только потом отмеченные остатки; не удалось с программой —
  остатки не трогаются. Модель страницы перед этим: подтверждение со списком → «закройте программу» (принудительно —
  отдельным «Да») → свой деинсталлятор.

## Тема

Кисти с ключами Windows-версии: `BackgroundBrush SidebarBrush SurfaceBrush Surface2Brush Surface3Brush LineBrush
TextBrush MutedBrush AccentBrush AccentHoverBrush AccentPressedBrush AccentTextBrush AccentSoftBrush DangerBrush
DangerSoftBrush WarningBrush WarningSoftBrush SuccessBrush SuccessSoftBrush InfoBrush InfoSoftBrush HoverBrush
SelectionBrush`. Классы стилей: текст `H1 H2 H3 Muted BigNumber`; `Border` — `Card InnerCard Badge Chip`; кнопки —
`AccentButton DangerButton GhostButton LinkButton`; `ListBox` — `Nav Plain`. Переключатель — стандартный `ToggleSwitch`.
Остальные элементы — тёмная тема Fluent с акцентом `#34D399`. Цвет из модели представления — ключом кисти через
`BrushKeyConverter`, значок — именем через `IconConverter`. Привязки компилируются: у каждого шаблона и вида есть
`x:DataType`.

## Правила безопасности

1. Ничего не меняется без подтверждения со списком того, что будет сделано.
2. Проверки только читают; изменения — по кнопке.
3. Перед изменением — резервная копия; без копии изменение отменяется.
4. Сразу удаляются только кэши и журналы; всё остальное — в Корзину; безвозвратно — только «Очистить корзину».
5. Нет перехода по символическим ссылкам и выхода за корень категории; `/System`, пути под SIP, пароли, история,
   cookies и связка ключей не трогаются.
6. Права администратора — только через окно пароля macOS; команды передаются как данные.
7. Эвристика только подсказывает.

## Режим проверки (скриншоты)

```
WaterCa7sPcCleaner --screenshots <папка> [--page Dashboard,Settings] [--no-scan] [--size 1040x660]
```
Данные программы — во временной папке, все вопросы — «Нет», страницы рисуются без экрана после проверки только на
чтение, сохраняются `NN-Key.png` и `log.txt`. Код выхода 1 — если страница не снялась или в журнале есть ошибки.
Тесты (`Ui/DemoScreenshotTests`) снимают те же страницы на образцах вывода утилит в папку `MAC_SCREENSHOTS_DIR`.

## Сборка и проверка

```
dotnet build WaterCa7sPcCleaner.Mac.sln -c Release
dotnet test WaterCa7sPcCleaner.Mac.sln -c Release --no-build
tools/mac/make-app.sh out        # только на macOS: .app и zip
```
CI (`.github/workflows/ci.yml`) — `macos-14` и `macos-latest`: сборка, тесты, скриншоты, образцы утилит (артефакты),
подписанный `.app` в zip. Подпись — самоподписанным сертификатом из секретов `MAC_SIGNING_P12` /
`MAC_SIGNING_PASSWORD`, без них — ad-hoc.
