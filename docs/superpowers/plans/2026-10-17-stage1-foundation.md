# Этап 1 «Фундамент» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mac-программа запускается, выглядит как Windows-версия, показывает «Главную» (сведения о Mac, проверка места на
диске, оценка 0–100) и «Настройки», снимает скриншоты без экрана; CI на macOS собирает, тестирует и выкладывает
подписанный `.app`.

**Architecture:** Один проект Avalonia `src/PcCleaner.Mac` (net10.0) и тестовый проект `tests/PcCleaner.Mac.Tests`
(xUnit v3 + Avalonia.Headless). Вся работа с macOS идёт через `ICommandRunner` — в тестах его заменяет
`FakeCommandRunner` с образцами вывода из `Fixtures`. Общий с Windows код скопирован в `src/PcCleaner.Mac/Shared`.

**Tech Stack:** C# / .NET 10, Avalonia 12.1.4 (Fluent, Headless, Skia, HarfBuzz), xUnit v3 3.2.2, bash и
PowerShell 5.1 для инструментов, GitHub Actions `macos-14` / `macos-latest`.

**Спецификация:** `docs/superpowers/specs/2026-10-17-mac-version-design.md`.

**Исполнение:** план выполняет та же сессия, что его написала (inline, superpowers:executing-plans). Тесты в плане даны
полностью — они задают поведение; для кода даны точные файлы, типы и сигнатуры, а тела пишутся сразу в репозиторий
(образцы API Avalonia 12 проверены на пробных проектах: тема Fluent с палитрой, классы стилей, `NativeMenu`, навигация
`ListBox` + `DataTemplate`, `CaptureRenderedFrame`, xUnit v3 + `[AvaloniaFact]`).

## Global Constraints

- Тесты никогда не меняют систему: временные папки (`TempDir`), `FakeCommandRunner`; настоящие утилиты — только на чтение
  и только в тестах `[MacFact]`.
- Весь текст для пользователя — по-русски; код, имена и комментарии — по-английски.
- Пакеты: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Headless` —
  `12.1.4`; тесты: `Microsoft.NET.Test.Sdk` `18.11.0`, `xunit.v3` `3.2.2`, `xunit.runner.visualstudio` `3.1.5`,
  `Avalonia.Headless.XUnit` `12.1.4`.
- `TargetFramework` `net10.0`, `AssemblyName` `WaterCa7sPcCleaner`, `RootNamespace` `PcCleaner`, `<Version>1.0.0</Version>`.
- Пространства имён как в Windows-версии: `PcCleaner.Core`, `PcCleaner.Modules.<Key>`, оболочка — `PcCleaner`.
- Утилиты macOS вызываются только по полному пути, аргументы — списком.
- Все текстовые файлы — LF (`.gitattributes`); скрипты `.sh` — с битом исполнения (`git update-index --chmod=+x`).
- Команды проверки (из корня репозитория): `dotnet build WaterCa7sPcCleaner.Mac.sln -c Release` и
  `dotnet test WaterCa7sPcCleaner.Mac.sln -c Release --no-build`; один класс —
  `dotnet test tests/PcCleaner.Mac.Tests -c Release --no-build --filter "FullyQualifiedName~<Класс>"`.
- Каждый коммит заканчивается строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; ветка `main`,
  отправка `git push`. Секреты репозитория — только после согласия пользователя.

## Файлы

| Файл | Ответственность |
|---|---|
| `WaterCa7sPcCleaner.Mac.sln` | решение: программа + тесты |
| `src/PcCleaner.Mac/PcCleaner.Mac.csproj` | проект программы |
| `src/PcCleaner.Mac/Shared/*.cs`, `Shared/SOURCE.md` | копии файлов Windows-версии и откуда они |
| `src/PcCleaner.Mac/Core/AppInfo.cs`, `AppPaths.cs` | имя, адрес репозитория, версия; папки программы |
| `src/PcCleaner.Mac/Core/Ui.cs`, `Log.cs`, `ErrorText.cs` | UI-поток, журнал, русский текст ошибок |
| `src/PcCleaner.Mac/Core/AppSettings.cs` | `settings.json` |
| `src/PcCleaner.Mac/Core/Commands.cs`, `Dialogs.cs`, `PageViewModel.cs`, `INavigator.cs` | команды, диалоги, база страницы |
| `src/PcCleaner.Mac/Core/CommandRunner.cs` | `CommandResult`, `ICommandRunner`, `ProcessCommandRunner` |
| `src/PcCleaner.Mac/Core/Plist.cs` | разбор plist |
| `src/PcCleaner.Mac/Core/MacShell.cs`, `FullDiskAccess.cs`, `SystemSettingsLinks.cs` | `open`, полный доступ к диску |
| `src/PcCleaner.Mac/Core/AppServices.cs` | общие экземпляры сервисов |
| `src/PcCleaner.Mac/Modules/Dashboard/*` | сведения о Mac, место на диске, оценка, страница «Главная» |
| `src/PcCleaner.Mac/Modules/Settings/*` | страница «Настройки» |
| `src/PcCleaner.Mac/Shell/*` | страницы будущих разделов, конвертеры, окно диалога, скриншоты |
| `src/PcCleaner.Mac/Themes/*.axaml` | цвета, стили, значки |
| `src/PcCleaner.Mac/App.axaml(.cs)`, `MainWindow.axaml(.cs)`, `MainViewModel.cs`, `Program.cs` | запуск и оболочка |
| `src/PcCleaner.Mac/Assets/AppIcon.icns`, `LICENSE-FluentUI.txt` | иконка, лицензия значков |
| `tests/PcCleaner.Mac.Tests/**` | тесты и образцы |
| `tools/mac/Info.plist`, `make-app.sh`, `sign.sh`, `capture-fixtures.sh`, `make-icon.ps1` | упаковка, подпись, образцы, иконка |
| `.github/workflows/ci.yml` | CI на macOS |

---

### Task 1: Решение, проекты, общие файлы, журнал

**Files:**
- Create: `WaterCa7sPcCleaner.Mac.sln`, `src/PcCleaner.Mac/PcCleaner.Mac.csproj`
- Create: `src/PcCleaner.Mac/Shared/{Format,ObservableObject,Health,HealthScoring,SmartScanner,ScanSummary}.cs`, `Shared/SOURCE.md`
- Create: `src/PcCleaner.Mac/Core/{AppInfo,AppPaths,Ui,Log,ErrorText}.cs`, `src/PcCleaner.Mac/Modules/Dashboard/ScoreModels.cs`
- Create: `tests/PcCleaner.Mac.Tests/PcCleaner.Mac.Tests.csproj`, `Support/{TestEnvironment,TempDir,Fixture,MacFactAttribute,FakeCommandRunner}.cs`
- Test: `tests/PcCleaner.Mac.Tests/Core/{LogTests,ErrorTextTests,SharedFilesTests}.cs`

**Interfaces:**
- Produces: `AppInfo.Name/RepoUrl/BundleId/Version`; `AppPaths.Home/DataDir/LogsDir {get;set;}`, `BackupsDir`, `UpdatesDir`,
  `SettingsFile`, `EnsureDir(string)`, `UseRoot(string root)`; `Ui.Attach(Dispatcher?)`, `Ui.Post/Invoke/Invoke<T>`,
  `Ui.InvokeAsync(Func<Task>)`, `Ui.InvokeAsync<T>(Func<Task<T>>)`; `Log` (API Windows-версии: `LogLevel`, `LogEntry`,
  `Entries`, `EntryAdded`, `Info/Warn/Error/Action`); `ErrorText.For/Sentence`; `ScoreDelta`, `ReclaimableSplit`
  (как в Windows `ReportModels.cs`); тестам — `TempDir`, `Fixture.Text(path)`, `[MacFact]`, `FakeCommandRunner`.

Общие файлы копируются из `A:\WaterCa7s-pc-cleaner` (коммит `ad2a9fb`) без изменений: `Core/Format.cs`,
`Core/ObservableObject.cs`, `Core/Health.cs`, `Modules/Dashboard/HealthScoring.cs`, `Modules/Dashboard/SmartScanner.cs`,
`Modules/Dashboard/ScanSummary.cs` (ему нужны `ScoreDelta` и `ReclaimableSplit` — они в `ScoreModels.cs`, скопированы
из `ReportModels.cs` строки 1–103). `SOURCE.md` — таблица «файл → путь в Windows-репозитории → коммит».

`ErrorText` — как в Windows, но тексты для macOS: нет доступа → «нет доступа — macOS не разрешила это действие (может
понадобиться полный доступ к диску или пароль администратора)»; `IOException` с «No space left on device» → «на диске
недостаточно места»; `Win32Exception` → «не удалось запустить программу macOS (код N)»; остальное — как в Windows.

`Ui` хранит диспетчер, который передал `App` (`Ui.Attach(Dispatcher.UIThread)`); без него (модульные тесты) действие
выполняется сразу в вызывающем потоке.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/Core/LogTests.cs
using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class LogTests
{
    [Fact]
    public void Action_GoesToEntriesAndToTheDailyFile()
    {
        var marker = "Тест журнала " + Guid.NewGuid();
        Log.Action(marker);

        Assert.Contains(Log.Entries.ToList(), e => e.Message == marker && e.Level == LogLevel.Action);
        var file = Path.Combine(AppPaths.LogsDir, $"{DateTime.Now:yyyy-MM-dd}.log");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.Contains(marker, new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public void Error_ShowsRussianText_TheFileKeepsTheOriginal()
    {
        var marker = "Ошибка теста " + Guid.NewGuid();
        Log.Error(marker, new UnauthorizedAccessException("Access to the path '/x' is denied."));

        var entry = Log.Entries.ToList().Last(e => e.Message.StartsWith(marker));
        Assert.Contains("нет доступа", entry.Message);
        Assert.DoesNotContain("Access to the path", entry.Message);
        var file = Path.Combine(AppPaths.LogsDir, $"{DateTime.Now:yyyy-MM-dd}.log");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.Contains("Access to the path", new StreamReader(stream).ReadToEnd());
    }
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Core/ErrorTextTests.cs
using System.ComponentModel;
using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class ErrorTextTests
{
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "нет доступа — macOS не разрешила")]
    [InlineData(typeof(FileNotFoundException), "файл или папка не найдены")]
    [InlineData(typeof(DirectoryNotFoundException), "файл или папка не найдены")]
    [InlineData(typeof(TimeoutException), "превышено время ожидания")]
    [InlineData(typeof(OperationCanceledException), "операция отменена")]
    public void For_KnownExceptions_IsRussian(Type type, string expected)
        => Assert.StartsWith(expected, ErrorText.For((Exception)Activator.CreateInstance(type)!));

    [Fact]
    public void For_DiskFull_IsRecognisedByTheUnixMessage()
        => Assert.Equal("на диске недостаточно места", ErrorText.For(new IOException("No space left on device : '/x'")));

    [Fact]
    public void For_FailedStart_NamesTheCode()
        => Assert.Equal("не удалось запустить программу macOS (код 2)", ErrorText.For(new Win32Exception(2)));

    [Fact]
    public void For_RussianMessage_IsKept()
        => Assert.Equal("своя ошибка", ErrorText.For(new InvalidOperationException("своя ошибка.")));

    [Fact]
    public void Sentence_IsCapitalizedWithAPeriod()
        => Assert.Equal("Файл или папка не найдены.", ErrorText.Sentence(new FileNotFoundException()));
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Core/SharedFilesTests.cs
using PcCleaner.Core;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

/// <summary>The copies of the Windows files compile and behave the same on the Mac side.</summary>
public class SharedFilesTests
{
    [Fact]
    public void Format_IsRussian() => Assert.Equal("1,5 ГБ", Format.Bytes(1536L * 1024 * 1024));

    [Fact]
    public void HealthScoring_CapsAModuleAt40()
    {
        var issues = Enumerable.Range(0, 5).Select(i => new HealthIssue
        {
            Module = "Очистка и диски",
            Title = "x" + i,
            Severity = IssueSeverity.Critical,
        });
        Assert.Equal(60, HealthScoring.ComputeScore(issues));
    }

    [Fact]
    public void ScanSummary_RoundTripsThroughSettings()
    {
        var settings = new AppSettings();
        new ScanSummary { Score = 81, Time = new DateTime(2026, 10, 17, 12, 0, 0), IssueCount = 2, TipCount = 1 }.Save(settings);
        var loaded = ScanSummary.Load(settings);
        Assert.Equal(81, loaded!.Score);
        Assert.Equal(1, loaded.ProblemCount);
    }
}
```

`SharedFilesTests.ScanSummary_RoundTripsThroughSettings` использует `AppSettings` из Task 2 — до Task 2 этот тест
не компилируется, поэтому в Task 1 файл добавляется без него, а тест дописывается в Task 2.

```csharp
// tests/PcCleaner.Mac.Tests/Support/TestEnvironment.cs
using System.Runtime.CompilerServices;
using PcCleaner.Core;

namespace PcCleaner.Mac.Tests.Support;

/// <summary>Runs before any test: program data, logs and the home folder point to a temp folder.</summary>
internal static class TestEnvironment
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "WaterCa7sMacTests-" + Guid.NewGuid().ToString("N")[..8]);

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Root);
        AppPaths.Home = Path.Combine(Root, "home");
        Directory.CreateDirectory(AppPaths.Home);
        AppPaths.UseRoot(Root);
    }
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Support/TempDir.cs
namespace PcCleaner.Mac.Tests.Support;

public sealed class TempDir : IDisposable
{
    public TempDir() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(TestEnvironment.Root, "t-" + Guid.NewGuid().ToString("N")[..8]);

    public string File(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Support/Fixture.cs
namespace PcCleaner.Mac.Tests.Support;

/// <summary>Captured macOS tool output in tests/PcCleaner.Mac.Tests/Fixtures (copied next to the test assembly).</summary>
public static class Fixture
{
    public static string PathOf(string relative) => Path.Combine(AppContext.BaseDirectory, "Fixtures", relative);

    public static string Text(string relative) => File.ReadAllText(PathOf(relative));
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Support/MacFactAttribute.cs
using Xunit;

namespace PcCleaner.Mac.Tests.Support;

/// <summary>A test that runs real macOS tools (read-only); skipped elsewhere.</summary>
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "только macOS";
    }
}
```

`FakeCommandRunner` пишется в Task 4 вместе с `ICommandRunner`.

- [ ] **Step 2: Run tests to verify they fail** — `dotnet build WaterCa7sPcCleaner.Mac.sln -c Release`: ошибки
  компиляции (нет `PcCleaner.Core.Log`, `AppPaths` и т. д.).
- [ ] **Step 3: Implement** — `.sln` (`dotnet new sln`, `dotnet sln add`), оба `.csproj` (пакеты из Global Constraints;
  в тестовом: `OutputType Exe`, `<None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />`), копии в
  `Shared/` + `SOURCE.md`, `ScoreModels.cs`, `AppInfo`, `AppPaths`, `Ui`, `Log` (как в Windows), `ErrorText`.
- [ ] **Step 4: Run tests** — `dotnet test … --filter "FullyQualifiedName~Core"`: PASS.
- [ ] **Step 5: Commit** — `Mac app: projects, shared files, log`.

### Task 2: Настройки программы (`AppSettings`)

**Files:** Create `src/PcCleaner.Mac/Core/AppSettings.cs`; Test `tests/PcCleaner.Mac.Tests/Core/AppSettingsTests.cs`;
дописать `ScanSummary_RoundTripsThroughSettings` в `SharedFilesTests`.

**Interfaces:** Produces `AppSettings { ModuleData; static Load(); Save(); GetModuleValue(module, key);
SetModuleValue(module, key, value) }` — как в Windows (атомарная запись через `.tmp`, снимок под `lock (this)`, старый
снимок не перезаписывает новый). Без настроек Windows-очистки и точек восстановления; `AutoUpdate` и др. добавят
следующие этапы.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/PcCleaner.Mac.Tests/Core/AppSettingsTests.cs
using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

[Collection("settings-file")]
public class AppSettingsTests
{
    [Fact]
    public void SetModuleValue_IsSavedAndLoaded()
    {
        var settings = new AppSettings();
        settings.SetModuleValue("Dashboard", "Test", "значение");
        Assert.Equal("значение", AppSettings.Load().GetModuleValue("Dashboard", "Test"));
    }

    [Fact]
    public void SetModuleValue_Null_RemovesTheValue()
    {
        var settings = new AppSettings();
        settings.SetModuleValue("Dashboard", "Gone", "1");
        settings.SetModuleValue("Dashboard", "Gone", null);
        Assert.Null(AppSettings.Load().GetModuleValue("Dashboard", "Gone"));
    }

    [Fact]
    public void Load_BrokenFile_GivesDefaults()
    {
        AppPaths.EnsureDir(AppPaths.DataDir);
        File.WriteAllText(AppPaths.SettingsFile, "{ не json");
        Assert.Empty(AppSettings.Load().ModuleData);
    }
}
```

- [ ] **Step 2: Run** — не компилируется (нет `AppSettings`).
- [ ] **Step 3: Implement** `AppSettings` по образцу Windows `Core/AppSettings.cs`.
- [ ] **Step 4: Run** — `--filter "FullyQualifiedName~AppSettings|FullyQualifiedName~SharedFiles"`: PASS.
- [ ] **Step 5: Commit** — `Mac app: settings.json`.

### Task 3: Команды, диалоги, база страницы

**Files:** Create `src/PcCleaner.Mac/Core/{Commands,Dialogs,PageViewModel,INavigator}.cs`;
Test `tests/PcCleaner.Mac.Tests/Core/{CommandsTests,DialogsTests,PageViewModelTests}.cs`.

**Interfaces:**
- `CommandBase : ObservableObject, ICommand` (`CanExecuteChanged`, `RaiseCanExecuteChanged()` через `Ui.Post`,
  `static Refresh()` — все живые команды, список слабых ссылок); `RelayCommand(Action, Func<bool>? = null)`,
  `RelayCommand(Action<object?>, Func<object?, bool>? = null)`; `AsyncRelayCommand(Func<Task>, Func<bool>? = null)`,
  `AsyncRelayCommand(Func<object?, Task>, Func<object?, bool>? = null)`, `IsRunning`, `ExecuteAsync(object? = null)`.
  Исключение в команде → `Log.Error` + `Dialogs.Error("Ошибка", ErrorText.Sentence(ex))`, отмена → `Log.Info`.
- `enum DialogKind { Warning, Question, Info, Error }`, `record DialogRequest(string Title, string Message, DialogKind Kind)`;
  `Dialogs.ConfirmHandler : Func<DialogRequest, Task<bool>>`, `Dialogs.MessageHandler : Func<DialogRequest, Task>`;
  `ConfirmAsync(title, message)` (Warning), `AskAsync` (Question), `InfoAsync`, `ErrorAsync`, `Error` (без ожидания),
  `UseSilentMode()`. **По умолчанию — тихий режим** (на всё «Нет», ошибки → `Log.Warn`); окна подключает `App`.
- `PageViewModel` — как в Windows, но `Icon` (имя значка без `Icon.`) вместо `IconGlyph`, ошибки через `Dialogs.Error`.
- `INavigator { IEnumerable<PageViewModel> Pages; void NavigateTo(string pageKey); }`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/Core/CommandsTests.cs
using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class CommandsTests
{
    [Fact]
    public void RelayCommand_Exception_IsLoggedNotThrown()
    {
        var marker = "сбой " + Guid.NewGuid();
        new RelayCommand(() => throw new InvalidOperationException(marker)).Execute(null);
        Assert.Contains(Log.Entries.ToList(), e => e.Level == LogLevel.Error && e.Message.Contains(marker));
    }

    [Fact]
    public void Refresh_RaisesCanExecuteChanged()
    {
        var raised = 0;
        var command = new RelayCommand(() => { });
        command.CanExecuteChanged += (_, _) => raised++;
        RelayCommand.Refresh();
        Assert.True(raised >= 1);
    }

    [Fact]
    public async Task AsyncRelayCommand_BlocksReentryWhileRunning()
    {
        var gate = new TaskCompletionSource();
        var runs = 0;
        var command = new AsyncRelayCommand(async () => { runs++; await gate.Task; });
        var first = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        gate.SetResult();
        await first;
        Assert.Equal(1, runs);
        Assert.True(command.CanExecute(null));
    }
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Core/DialogsTests.cs
using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

[Collection("dialogs")]
public class DialogsTests
{
    [Fact]
    public async Task ByDefault_EveryConfirmationIsDeclined()
    {
        Dialogs.UseSilentMode();
        Assert.False(await Dialogs.ConfirmAsync("Удалить?", "Список"));
        Assert.False(await Dialogs.AskAsync("Открыть?", "Отчёт"));
    }

    [Fact]
    public async Task Handlers_GetTheKind()
    {
        var seen = new List<DialogRequest>();
        Dialogs.ConfirmHandler = r => { seen.Add(r); return Task.FromResult(true); };
        try
        {
            Assert.True(await Dialogs.ConfirmAsync("Удалить?", "Список"));
            Assert.True(await Dialogs.AskAsync("Открыть?", "Отчёт"));
            Assert.Equal(new[] { DialogKind.Warning, DialogKind.Question }, seen.Select(r => r.Kind));
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Core/PageViewModelTests.cs
using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class PageViewModelTests
{
    private sealed class TestPage : PageViewModel
    {
        public override string Title => "Тест";
        public override string Subtitle => "";
        public override string Icon => "Home";
        public Task Run(Func<CancellationToken, Task> work) => RunBusyAsync("Работаю", work);
    }

    [Fact]
    public void Key_IsTheTypeNameWithoutViewModel() => Assert.Equal("TestPage", new TestPage().Key);

    [Fact]
    public async Task RunBusy_SetsAndClearsBusy()
    {
        var page = new TestPage();
        bool? busyInside = null;
        await page.Run(_ => { busyInside = page.IsBusy; return Task.CompletedTask; });
        Assert.True(busyInside);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task RunBusy_Cancel_StopsTheWorkQuietly()
    {
        var page = new TestPage();
        var started = new TaskCompletionSource();
        var run = page.Run(async ct => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); });
        await started.Task;
        page.CancelCommand.Execute(null);
        await run;
        Assert.False(page.IsBusy);
    }
}
```

`[Collection("dialogs")]` и `[Collection("settings-file")]` не дают тестам, меняющим общие статические объекты,
идти параллельно друг с другом.

- [ ] **Step 2: Run** — не компилируется.
- [ ] **Step 3: Implement** `Commands.cs`, `Dialogs.cs`, `PageViewModel.cs`, `INavigator.cs`.
- [ ] **Step 4: Run** — `--filter "FullyQualifiedName~Commands|FullyQualifiedName~Dialogs|FullyQualifiedName~PageViewModel"`: PASS.
- [ ] **Step 5: Commit** — `Mac app: commands, dialogs, page base`.

### Task 4: Запуск утилит (`ICommandRunner`)

**Files:** Create `src/PcCleaner.Mac/Core/CommandRunner.cs`, `tests/PcCleaner.Mac.Tests/Support/FakeCommandRunner.cs`;
Test `tests/PcCleaner.Mac.Tests/Core/CommandRunnerTests.cs`.

**Interfaces:**
- `sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut = false) { bool Ok; }`
  (`Ok` = код 0 и не таймаут); `CommandResult.NotStarted(string error)` = код 127.
- `interface ICommandRunner { Task<CommandResult> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default); }`
- `sealed class ProcessCommandRunner : ICommandRunner` — `DefaultTimeout` 30 с; относительный путь → `ArgumentException`;
  `UseShellExecute=false`, перехват stdout/stderr в UTF-8, `LC_ALL`/`LANG` = `en_US.UTF-8`; по таймауту процесс
  (с потомками) убивается, `TimedOut = true`; отмена `ct` → процесс убивается, `OperationCanceledException`;
  не запустился → `NotStarted(сообщение)`.
- Тестам: `FakeCommandRunner.On(exe, args, result)`, `OnFixture(exe, args, fixture, exitCode = 0)`, `Calls`; без образца
  → `NotStarted("нет образца для …")`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/Core/CommandRunnerTests.cs
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class CommandRunnerTests
{
    private static (string Exe, string[] Args) Echo(string text) => OperatingSystem.IsWindows()
        ? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/c", "echo " + text })
        : ("/bin/echo", new[] { text });

    [Fact]
    public async Task Run_ReturnsStdoutAndExitCode()
    {
        var (exe, args) = Echo("hello");
        var result = await new ProcessCommandRunner().RunAsync(exe, args);
        Assert.True(result.Ok);
        Assert.Equal("hello", result.StdOut.Trim());
    }

    [Fact]
    public async Task Run_RelativePath_IsRefused()
        => await Assert.ThrowsAsync<ArgumentException>(() => new ProcessCommandRunner().RunAsync("echo", new[] { "x" }));

    [Fact]
    public async Task Run_MissingProgram_IsNotStarted()
    {
        var missing = Path.Combine(TestEnvironment.Root, "no-such-program");
        var result = await new ProcessCommandRunner().RunAsync(missing, Array.Empty<string>());
        Assert.Equal(127, result.ExitCode);
        Assert.False(result.Ok);
    }

    [MacFact]
    public async Task Run_Timeout_KillsTheProcess()
    {
        var result = await new ProcessCommandRunner().RunAsync("/bin/sleep", new[] { "30" }, TimeSpan.FromMilliseconds(300));
        Assert.True(result.TimedOut);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Fake_ReturnsTheFixtureAndRecordsTheCall()
    {
        var fake = new FakeCommandRunner().OnFixture("/usr/bin/sw_vers", Array.Empty<string>(), "sw_vers/macos15.txt");
        var result = await fake.RunAsync("/usr/bin/sw_vers", Array.Empty<string>());
        Assert.Contains("ProductVersion", result.StdOut);
        Assert.Equal(new[] { "/usr/bin/sw_vers" }, fake.Calls);
        Assert.Equal(127, (await fake.RunAsync("/bin/ls", new[] { "-l" })).ExitCode);
    }
}
```

Образец `tests/PcCleaner.Mac.Tests/Fixtures/sw_vers/macos15.txt` (табуляции, как у настоящей утилиты):

```
ProductName:		macOS
ProductVersion:		15.1
BuildVersion:		24B83
```

- [ ] **Step 2: Run** — не компилируется.
- [ ] **Step 3: Implement** `CommandRunner.cs`, `FakeCommandRunner.cs`, образец.
- [ ] **Step 4: Run** — `--filter "FullyQualifiedName~CommandRunner"`: PASS (на Windows тест таймаута пропускается).
- [ ] **Step 5: Commit** — `Mac app: command runner`.

### Task 5: Разбор plist

**Files:** Create `src/PcCleaner.Mac/Core/Plist.cs`; Test `tests/PcCleaner.Mac.Tests/Core/PlistTests.cs`;
Fixtures `plist/all-types.plist`, `plist/launch-agent.plist`, `plist/broken.plist`, `plist/entity-bomb.plist`.

**Interfaces:**
- `PlistException : Exception` (сообщение по-русски).
- `static class Plist`: `object? Parse(string xml)` — `dict` → `Dictionary<string, object?>` (Ordinal), `array` →
  `List<object?>`, `string` → `string`, `integer` → `long`, `real` → `double`, `true/false` → `bool`, `date` →
  `DateTimeOffset` (UTC), `data` → `byte[]`; `Dictionary<string, object?> ParseDict(string xml)` (корень не `dict` →
  `PlistException`). XML читается с `DtdProcessing.Ignore`, без `XmlResolver`.
- `sealed record PlistReadResult(Dictionary<string, object?>? Value, string? Problem)`;
  `static class PlistFile { Task<PlistReadResult> ReadAsync(string path, ICommandRunner runner, CancellationToken ct); }`
  — файл начинается с `bplist` → `/usr/bin/plutil -convert xml1 -o - <path>`; иначе текст разбирается сам; ошибки —
  в `Problem` по-русски.
- Расширения `PlistExtensions` для `IReadOnlyDictionary<string, object?>`: `GetString`, `GetBool`, `GetLong`,
  `GetDict`, `GetArray`, `GetStrings` (только строки из массива).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/Core/PlistTests.cs
using System.Text;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class PlistTests
{
    [Fact]
    public void Parse_AllTypes()
    {
        var d = Plist.ParseDict(Fixture.Text("plist/all-types.plist"));
        Assert.Equal("строка", d.GetString("Text"));
        Assert.Equal(42L, d.GetLong("Number"));
        Assert.Equal(1.5, (double)d["Real"]!);
        Assert.True(d.GetBool("Yes"));
        Assert.False(d.GetBool("No"));
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 12, 30, 0, TimeSpan.Zero), d["When"]);
        Assert.Equal("hello", Encoding.ASCII.GetString((byte[])d["Blob"]!));
        Assert.Equal(new[] { "a", "b" }, d.GetStrings("List"));
        Assert.Equal("inner", d.GetDict("Nested")!.GetString("Key"));
    }

    [Fact]
    public void Parse_LaunchAgent()
    {
        var d = Plist.ParseDict(Fixture.Text("plist/launch-agent.plist"));
        Assert.Equal("com.example.helper", d.GetString("Label"));
        Assert.Equal(new[] { "/Applications/Example.app/Contents/MacOS/helper", "--background" }, d.GetStrings("ProgramArguments"));
        Assert.True(d.GetBool("RunAtLoad"));
    }

    [Fact]
    public void Parse_Broken_ThrowsRussian()
        => Assert.Contains("plist", Assert.Throws<PlistException>(() => Plist.ParseDict(Fixture.Text("plist/broken.plist"))).Message);

    [Fact]
    public void Parse_EntitiesAreNotExpanded()
        => Assert.Throws<PlistException>(() => Plist.ParseDict(Fixture.Text("plist/entity-bomb.plist")));

    [Fact]
    public void Missing_Keys_AreNull()
    {
        var d = Plist.ParseDict("<plist><dict><key>A</key><integer>1</integer></dict></plist>");
        Assert.Null(d.GetString("A"));
        Assert.Null(d.GetBool("B"));
        Assert.Empty(d.GetStrings("C"));
    }

    [Fact]
    public async Task ReadAsync_Binary_GoesThroughPlutil()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "b.plist");
        File.WriteAllBytes(file, Encoding.ASCII.GetBytes("bplist00..."));
        var fake = new FakeCommandRunner().On("/usr/bin/plutil", new[] { "-convert", "xml1", "-o", "-", file },
            new CommandResult(0, Fixture.Text("plist/launch-agent.plist"), ""));
        var result = await PlistFile.ReadAsync(file, fake, CancellationToken.None);
        Assert.Null(result.Problem);
        Assert.Equal("com.example.helper", result.Value!.GetString("Label"));
    }

    [Fact]
    public async Task ReadAsync_Broken_GivesAProblem()
    {
        using var dir = new TempDir();
        var file = dir.File("x.plist", Fixture.Text("plist/broken.plist"));
        var result = await PlistFile.ReadAsync(file, new FakeCommandRunner(), CancellationToken.None);
        Assert.Null(result.Value);
        Assert.NotNull(result.Problem);
    }
}
```

`plist/all-types.plist` — `dict` с ключами `Text` (`строка`), `Number` (42), `Real` (1.5), `Yes` (`<true/>`), `No`
(`<false/>`), `When` (`2026-10-17T12:30:00Z`), `Blob` (`aGVsbG8=`), `List` (`a`, `b`), `Nested` (`Key` = `inner`), с
обычным `<!DOCTYPE plist …>`. `plist/launch-agent.plist` — `Label`, `ProgramArguments`, `RunAtLoad`. `broken.plist` —
обрезанный XML. `entity-bomb.plist` — внутренний DTD с сущностью `&lol;` внутри `<string>`.

- [ ] **Step 2: Run** — не компилируется.
- [ ] **Step 3: Implement** `Plist.cs` и образцы.
- [ ] **Step 4: Run** — `--filter "FullyQualifiedName~Plist"`: PASS.
- [ ] **Step 5: Commit** — `Mac app: plist reader`.

### Task 6: Сведения о Mac

**Files:** Create `src/PcCleaner.Mac/Modules/Dashboard/{MacInfo,MacInfoParsers,MacInfoService}.cs`;
Test `tests/PcCleaner.Mac.Tests/Dashboard/{MacInfoParsersTests,MacInfoServiceTests}.cs`;
Fixtures `sw_vers/macos14.txt`, `sysctl/boottime.txt`, `system_profiler/hardware-m2.json`,
`system_profiler/hardware-intel.json`, `df/data-volume.txt`, `df/low-space.txt`, `scutil/computername.txt`.

**Interfaces:**
- `MacInfoParsers.ParseSwVers(string) → (string? Name, string? Version, string? Build)`;
  `ParseBootTime(string) → DateTimeOffset?` (`{ sec = N, usec = M } …`); `ParseHardware(string json) → HardwareInfo?`
  (`SPHardwareDataType[0]`: `machine_name`, `machine_model`, `chip_type` или `cpu_type`, `physical_memory`,
  `number_processors`; серийный номер и UUID не читаются); `ParseMemoryText("16 GB") → long`;
  `ParseDf(string) → DiskUsage?` (последняя строка `df -k -P`, «1024-blocks»).
- `record HardwareInfo(string? ModelName, string? ModelId, string? Chip, long MemoryBytes, string? Cores)`.
- `record DiskUsage(string Mount, long TotalBytes, long UsedBytes, long FreeBytes) { double FreePercent; }`.
- `static class MacOsNames { string Marketing(string? version); }` — 26 Tahoe, 15 Sequoia, 14 Sonoma, 13 Ventura,
  12 Monterey, 11 Big Sur; неизвестная — «macOS <версия>».
- `record MacInfo { ComputerName, Hardware, OsVersion, OsBuild, BootTime, Disk; IReadOnlyList<(string Label, string Value)> Rows(DateTimeOffset now); }`
  — строки «Имя Mac», «Модель», «Чип», «Память», «macOS», «Диск» («свободно X из Y (Z %)»), «Без перезагрузки»;
  нет данных → «нет данных».
- `MacInfoService(ICommandRunner)`: `Task<MacInfo> LoadAsync(CancellationToken)` (команды параллельно:
  `/usr/sbin/scutil --get ComputerName`, `/usr/bin/sw_vers`, `/usr/sbin/sysctl -n kern.boottime`,
  `/usr/sbin/system_profiler SPHardwareDataType -json` (таймаут 30 с), диск); `Task<DiskUsage?> DiskAsync(CancellationToken)`
  — `/bin/df -k -P /System/Volumes/Data`, при ошибке — `/bin/df -k -P /`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/Dashboard/MacInfoParsersTests.cs
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

public class MacInfoParsersTests
{
    [Fact]
    public void SwVers()
    {
        var (name, version, build) = MacInfoParsers.ParseSwVers(Fixture.Text("sw_vers/macos15.txt"));
        Assert.Equal(("macOS", "15.1", "24B83"), (name, version, build));
    }

    [Fact]
    public void SwVers_Garbage_GivesNulls() => Assert.Equal((null, null, null), MacInfoParsers.ParseSwVers("что-то не то"));

    [Fact]
    public void BootTime()
        => Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1729152000), MacInfoParsers.ParseBootTime(Fixture.Text("sysctl/boottime.txt")));

    [Fact]
    public void Hardware_AppleSilicon()
    {
        var hw = MacInfoParsers.ParseHardware(Fixture.Text("system_profiler/hardware-m2.json"))!;
        Assert.Equal("MacBook Air", hw.ModelName);
        Assert.Equal("Mac14,2", hw.ModelId);
        Assert.Equal("Apple M2", hw.Chip);
        Assert.Equal(16L << 30, hw.MemoryBytes);
    }

    [Fact]
    public void Hardware_Intel_UsesCpuType()
        => Assert.Equal("Quad-Core Intel Core i5", MacInfoParsers.ParseHardware(Fixture.Text("system_profiler/hardware-intel.json"))!.Chip);

    [Theory]
    [InlineData("16 GB", 16L << 30)]
    [InlineData("8 GB", 8L << 30)]
    [InlineData("512 MB", 512L << 20)]
    [InlineData("1 TB", 1L << 40)]
    [InlineData("много", 0L)]
    public void MemoryText(string text, long bytes) => Assert.Equal(bytes, MacInfoParsers.ParseMemoryText(text));

    [Fact]
    public void Df()
    {
        var disk = MacInfoParsers.ParseDf(Fixture.Text("df/data-volume.txt"))!;
        Assert.Equal("/System/Volumes/Data", disk.Mount);
        Assert.Equal(482797652L * 1024, disk.TotalBytes);
        Assert.Equal(150123456L * 1024, disk.FreeBytes);
    }

    [Fact]
    public void Df_Empty_IsNull() => Assert.Null(MacInfoParsers.ParseDf("Filesystem 1024-blocks Used Available Capacity Mounted on\n"));

    [Theory]
    [InlineData("15.1", "macOS Sequoia 15.1")]
    [InlineData("14.7.1", "macOS Sonoma 14.7.1")]
    [InlineData("26.0", "macOS Tahoe 26.0")]
    [InlineData("27.0", "macOS 27.0")]
    [InlineData(null, "macOS")]
    public void Names(string? version, string expected) => Assert.Equal(expected, MacOsNames.Marketing(version));
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Dashboard/MacInfoServiceTests.cs
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

public class MacInfoServiceTests
{
    internal static FakeCommandRunner Mac() => new FakeCommandRunner()
        .OnFixture("/usr/sbin/scutil", new[] { "--get", "ComputerName" }, "scutil/computername.txt")
        .OnFixture("/usr/bin/sw_vers", Array.Empty<string>(), "sw_vers/macos15.txt")
        .OnFixture("/usr/sbin/sysctl", new[] { "-n", "kern.boottime" }, "sysctl/boottime.txt")
        .OnFixture("/usr/sbin/system_profiler", new[] { "SPHardwareDataType", "-json" }, "system_profiler/hardware-m2.json")
        .OnFixture("/bin/df", new[] { "-k", "-P", "/System/Volumes/Data" }, "df/data-volume.txt");

    [Fact]
    public async Task Load_FillsTheRows()
    {
        var info = await new MacInfoService(Mac()).LoadAsync(CancellationToken.None);
        var rows = info.Rows(DateTimeOffset.FromUnixTimeSeconds(1729152000).AddHours(5)).ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("Mac Тестовый", rows["Имя Mac"]);
        Assert.Equal("MacBook Air (Mac14,2)", rows["Модель"]);
        Assert.Equal("Apple M2", rows["Чип"]);
        Assert.Equal("16 ГБ", rows["Память"]);
        Assert.Equal("macOS Sequoia 15.1 (24B83)", rows["macOS"]);
        Assert.StartsWith("свободно 143 ГБ из 460 ГБ", rows["Диск"]);
        Assert.Equal("5 ч 0 мин", rows["Без перезагрузки"]);
    }

    [Fact]
    public async Task Load_NothingWorks_SaysNoData()
    {
        var info = await new MacInfoService(new FakeCommandRunner()).LoadAsync(CancellationToken.None);
        Assert.All(info.Rows(DateTimeOffset.Now), r => Assert.Equal("нет данных", r.Value));
    }

    [Fact]
    public async Task Disk_FallsBackToRoot()
    {
        var fake = new FakeCommandRunner().OnFixture("/bin/df", new[] { "-k", "-P", "/" }, "df/data-volume.txt");
        Assert.NotNull(await new MacInfoService(fake).DiskAsync(CancellationToken.None));
    }

    [MacFact]
    public async Task RealMac_GivesVersionAndDisk()
    {
        var info = await new MacInfoService(new ProcessCommandRunner()).LoadAsync(CancellationToken.None);
        Assert.NotNull(info.OsVersion);
        Assert.True(info.Disk!.TotalBytes > 0);
    }
}
```

Образцы: `scutil/computername.txt` = `Mac Тестовый`; `sysctl/boottime.txt` =
`{ sec = 1729152000, usec = 123456 } Thu Oct 17 10:00:00 2024`; `df/data-volume.txt`:

```
Filesystem     1024-blocks      Used Available Capacity  Mounted on
/dev/disk3s5     482797652 312345678 150123456    68%    /System/Volumes/Data
```

`df/low-space.txt` — тот же диск со свободными `20971520` (20 ГБ, 4 %). `hardware-m2.json` — ответ
`system_profiler SPHardwareDataType -json` для MacBook Air M2 16 ГБ (`proc 8:4:4`), серийный номер и UUID — заглушки;
`hardware-intel.json` — с `cpu_type` «Quad-Core Intel Core i5» вместо `chip_type`, `physical_memory` «8 GB».
Образцы заменяются настоящими из CI в Task 14.

- [ ] **Step 2: Run** — не компилируется.
- [ ] **Step 3: Implement** три файла и образцы.
- [ ] **Step 4: Run** — `--filter "FullyQualifiedName~MacInfo"`: PASS.
- [ ] **Step 5: Commit** — `Mac app: Mac info`.

### Task 7: Свободное место — первая проверка «Главной»

**Files:** Create `src/PcCleaner.Mac/Modules/Dashboard/DiskSpace.cs` (`DiskSpaceRules`, `DiskSpaceContributor`);
Test `tests/PcCleaner.Mac.Tests/Dashboard/DiskSpaceTests.cs`.

**Interfaces:** `DiskSpaceRules.Evaluate(DiskUsage) → HealthIssue?`; `DiskSpaceContributor(MacInfoService) : IHealthContributor`
(`ModuleName` = «Очистка и диски»). Пороги из спецификации: свободно < 10 % или < 10 ГБ — `High`; < 20 % — `Medium`;
`TargetPageKey` = `Cleaner`; исправления нет (место освобождает раздел очистки).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/PcCleaner.Mac.Tests/Dashboard/DiskSpaceTests.cs
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

public class DiskSpaceTests
{
    private const long Gb = 1L << 30;

    [Theory]
    [InlineData(500, 200, null)]
    [InlineData(500, 90, IssueSeverity.Medium)]
    [InlineData(500, 40, IssueSeverity.High)]
    [InlineData(2000, 9, IssueSeverity.High)]
    public void Evaluate(long totalGb, long freeGb, IssueSeverity? expected)
    {
        var issue = DiskSpaceRules.Evaluate(new DiskUsage("/System/Volumes/Data", totalGb * Gb, (totalGb - freeGb) * Gb, freeGb * Gb));
        Assert.Equal(expected, issue?.Severity);
        if (issue is not null)
        {
            Assert.Equal("Очистка и диски", issue.Module);
            Assert.Equal("Cleaner", issue.TargetPageKey);
            Assert.Null(issue.FixAsync);
        }
    }

    [Fact]
    public async Task Contributor_ReadsTheDisk()
    {
        var fake = new FakeCommandRunner().OnFixture("/bin/df", new[] { "-k", "-P", "/System/Volumes/Data" }, "df/low-space.txt");
        var issues = await new DiskSpaceContributor(new MacInfoService(fake)).AnalyzeAsync(CancellationToken.None);
        Assert.Equal(IssueSeverity.High, Assert.Single(issues).Severity);
    }

    [Fact]
    public async Task Contributor_NoDisk_Throws_SoTheScanShowsTheModuleAsFailed()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DiskSpaceContributor(new MacInfoService(new FakeCommandRunner())).AnalyzeAsync(CancellationToken.None));
}
```

Сообщение `InvalidOperationException` — по-русски («не удалось узнать свободное место на диске»): его покажет
`SmartScanner` через `ErrorText`.

- [ ] **Step 2–4:** не компилируется → реализация → `--filter "FullyQualifiedName~DiskSpace"`: PASS.
- [ ] **Step 5: Commit** — `Mac app: free disk space check`.

### Task 8: Модель страницы «Главная»

**Files:** Create `src/PcCleaner.Mac/Modules/Dashboard/{DashboardViewModel,DashboardItems}.cs`;
Test `tests/PcCleaner.Mac.Tests/Dashboard/DashboardViewModelTests.cs`.

**Interfaces:**
- `DashboardViewModel(IReadOnlyList<IHealthContributor> contributors, MacInfoService info, AppSettings settings, INavigator? navigator)`
  : `PageViewModel` (Key `Dashboard`, Title «Главная», Icon `Home`). Свойства: `Score` (`int?`), `ScoreText` («—» или
  число), `Verdict`, `VerdictDescription`, `ScoreBrushKey` (`HealthScoring.ScoreBrushKey(score, failed)`),
  `FindingsText`, `LastScanText` («Последняя проверка: сегодня в 14:32»), `DeltaText`, `HasDelta`, `HasScan`,
  `Groups` (`ObservableCollection<IssueGroupItem>`), `Modules` (`ObservableCollection<ModuleRow>`), `InfoRows`
  (`ObservableCollection<InfoRow>`), `FixableCount`, `FixAllText` («Исправить найденное (N)»), `ScanCommand`,
  `FixAllCommand`; методы `Task ScanAsync(CancellationToken)`, `Task FixAllAsync()`, `Task FixAsync(IssueItem)`.
  `OnNavigatedToAsync` — один раз читает сведения о Mac и прошлую сводку; `PreviewScanAsync` = `ScanAsync`.
- Сводка проверки сохраняется через `ScanSummary` (как в Windows: `MeasuredScore`, базовая оценка для «Было → стало»,
  неполная проверка не становится базой).
- «Исправить найденное»: находки с `CanFix && AutoFixSafe`; подтверждение `Dialogs.ConfirmAsync("Исправить найденное",
  "Будет сделано:\n• …")`; «Нет» — ничего не делается; каждое исправление: `FixResult`, `IsFixed =
  !HealthScoring.LooksLikeFailure(result)`, `Log.Action`; потом пересчёт оценки и сводки.
- `DashboardItems.cs`: `IssueItem` (`Issue`, `Title`, `Details`, `GainText`, `SeverityText`, `SeverityBrushKey`,
  `CanFix`, `CanOpen`, `FixCommand`, `OpenCommand`), `IssueGroupItem(Title, Hint, Items)`, `ModuleRow(Name, StateText,
  IsDone, IsFailed)`, `InfoRow(Label, Value)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/Dashboard/DashboardViewModelTests.cs
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

[Collection("dialogs")]
public class DashboardViewModelTests
{
    private sealed class FakeContributor(string module, params HealthIssue[] issues) : IHealthContributor
    {
        public string ModuleName => module;
        public Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<HealthIssue>>(issues);
    }

    private sealed class FailingContributor : IHealthContributor
    {
        public string ModuleName => "Программы";
        public Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct) => throw new InvalidOperationException("сломалось");
    }

    private static HealthIssue Issue(string title, IssueSeverity severity, Func<CancellationToken, Task<string>>? fix = null, bool autoFix = false)
        => new() { Module = "Очистка и диски", Title = title, Severity = severity, FixAsync = fix, AutoFixSafe = autoFix };

    private static DashboardViewModel Create(params IHealthContributor[] contributors)
        => new(contributors, new MacInfoService(MacInfoServiceTests.Mac()), new AppSettings(), null);

    [Fact]
    public async Task Scan_ScoresAndGroups()
    {
        var vm = Create(new FakeContributor("Очистка и диски", Issue("Мало места", IssueSeverity.High), Issue("Совет", IssueSeverity.Info)));
        await vm.ScanAsync(CancellationToken.None);
        Assert.Equal(88, vm.Score);
        Assert.Equal("Хорошо", vm.Verdict);
        Assert.Equal(new[] { "Важно", "Советы" }, vm.Groups.Select(g => g.Title));
        Assert.Equal("1 проблема и 1 совет", vm.FindingsText);
    }

    [Fact]
    public async Task Scan_FailedModule_IsIncomplete()
    {
        var vm = Create(new FakeContributor("Очистка и диски"), new FailingContributor());
        await vm.ScanAsync(CancellationToken.None);
        Assert.Equal(HealthScoring.IncompleteVerdict, vm.Verdict);
        Assert.Contains(vm.Modules, m => m.Name == "Программы" && m.IsFailed);
    }

    [Fact]
    public async Task FixAll_Declined_ChangesNothing()
    {
        Dialogs.UseSilentMode();
        var fixedIt = false;
        var vm = Create(new FakeContributor("Очистка и диски",
            Issue("Кэши", IssueSeverity.Medium, _ => { fixedIt = true; return Task.FromResult("Удалено 1 ГБ"); }, autoFix: true)));
        await vm.ScanAsync(CancellationToken.None);
        await vm.FixAllAsync();
        Assert.False(fixedIt);
        Assert.Equal(1, vm.FixableCount);
    }

    [Fact]
    public async Task FixAll_Confirmed_FixesOnlyAutoFixSafe_AndRescores()
    {
        string? question = null;
        Dialogs.ConfirmHandler = r => { question = r.Message; return Task.FromResult(true); };
        try
        {
            var manual = false;
            var vm = Create(new FakeContributor("Очистка и диски",
                Issue("Кэши", IssueSeverity.Medium, _ => Task.FromResult("Удалено 1 ГБ"), autoFix: true),
                Issue("Ручное", IssueSeverity.Medium, _ => { manual = true; return Task.FromResult("ok"); })));
            await vm.ScanAsync(CancellationToken.None);
            Assert.Equal(88, vm.Score);
            await vm.FixAllAsync();
            Assert.Contains("• Кэши", question);
            Assert.DoesNotContain("Ручное", question);
            Assert.False(manual);
            Assert.Equal(94, vm.Score);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task FixAll_FailureText_IsNotMarkedFixed()
    {
        Dialogs.ConfirmHandler = _ => Task.FromResult(true);
        try
        {
            var vm = Create(new FakeContributor("Очистка и диски",
                Issue("Кэши", IssueSeverity.Medium, _ => Task.FromResult("Не удалось удалить файлы"), autoFix: true)));
            await vm.ScanAsync(CancellationToken.None);
            await vm.FixAllAsync();
            Assert.False(vm.Groups.Single().Items.Single().Issue.IsFixed);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task SecondScan_ShowsTheDelta()
    {
        var settings = new AppSettings();
        var first = new DashboardViewModel(new IHealthContributor[] { new FakeContributor("Очистка и диски", Issue("Мало места", IssueSeverity.High)) },
            new MacInfoService(MacInfoServiceTests.Mac()), settings, null);
        await first.ScanAsync(CancellationToken.None);
        var second = new DashboardViewModel(new IHealthContributor[] { new FakeContributor("Очистка и диски") },
            new MacInfoService(MacInfoServiceTests.Mac()), settings, null);
        await second.OnNavigatedToAsync();
        await second.ScanAsync(CancellationToken.None);
        Assert.True(second.HasDelta);
        Assert.Equal("Было 88 → стало 100 (+12)", second.DeltaText);
    }

    [Fact]
    public async Task OpenNavigatesToTheTargetPage()
    {
        var nav = new RecordingNavigator();
        var vm = new DashboardViewModel(new IHealthContributor[] { new FakeContributor("Очистка и диски",
                new HealthIssue { Module = "Очистка и диски", Title = "Мало места", Severity = IssueSeverity.High, TargetPageKey = "Cleaner" }) },
            new MacInfoService(MacInfoServiceTests.Mac()), new AppSettings(), nav);
        await vm.ScanAsync(CancellationToken.None);
        vm.Groups[0].Items[0].OpenCommand.Execute(null);
        Assert.Equal("Cleaner", nav.Last);
    }

    private sealed class RecordingNavigator : INavigator
    {
        public string? Last { get; private set; }
        public IEnumerable<PageViewModel> Pages => Array.Empty<PageViewModel>();
        public void NavigateTo(string pageKey) => Last = pageKey;
    }
}
```

- [ ] **Step 2–4:** не компилируется → реализация → `--filter "FullyQualifiedName~DashboardViewModel"`: PASS.
- [ ] **Step 5: Commit** — `Mac app: dashboard model`.

### Task 9: Оболочка без окна: страницы, навигация, «Настройки»

**Files:** Create `src/PcCleaner.Mac/Core/{MacShell,FullDiskAccess,SystemSettingsLinks,AppServices}.cs`,
`src/PcCleaner.Mac/Shell/UpcomingPageViewModel.cs`, `src/PcCleaner.Mac/Modules/Settings/SettingsViewModel.cs`,
`src/PcCleaner.Mac/MainViewModel.cs`; Test `tests/PcCleaner.Mac.Tests/Shell/{FullDiskAccessTests,MacShellTests,SettingsViewModelTests,MainViewModelTests}.cs`.

**Interfaces:**
- `MacShell(ICommandRunner)`: `Task<bool> OpenAsync(string target)` (`/usr/bin/open <target>`), `Task<bool> RevealAsync(string path)`
  (`/usr/bin/open -R <path>`); ошибка → `Log.Warn`, `false`.
- `enum FdaState { Granted, Denied, Unknown }`; `FullDiskAccess.ProbePath(string home)` =
  `<home>/Library/Application Support/com.apple.TCC/TCC.db`; `FullDiskAccess.Check(string home, Func<string, Stream>? open = null)`
  — открыл → `Granted` (поток сразу закрывается, ничего не читается), `UnauthorizedAccessException` → `Denied`, нет
  файла или папки → `Unknown`.
- `SystemSettingsLinks.FullDiskAccess` = `x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles`.
- `AppServices`: `Runner`, `Settings`, `Shell`, `MacInfo`, `Navigator`, `Init()`; `PageFactory.Create() → IReadOnlyList<PageViewModel>`
  (Dashboard с `DiskSpaceContributor`, четыре `UpcomingPageViewModel`, Settings).
- `UpcomingPageViewModel(string key, string title, string subtitle, string icon, string description)` — ключи
  `Cleaner` («Очистка и диски», `Broom`), `Processes` («Процессы и автозагрузка», `Pulse`), `Programs` («Программы», `Apps`),
  `Security` («Безопасность и сеть», `Shield`).
- `SettingsViewModel(MacShell shell, Func<FdaState> checkFda)` (Key `Settings`, Icon `Settings`): `VersionText`,
  `Fda`, `FdaText`, `IsFdaGranted`, `RefreshFdaCommand`, `OpenFdaSettingsCommand`, `OpenRepoCommand`,
  `OpenLogsCommand`, `OpenDataCommand`, `LogEntries` (= `Log.Entries`). Тексты: Granted — «Полный доступ к диску
  разрешён — проверяются Safari, Корзина и контейнеры программ»; Denied — «Полного доступа к диску нет — Safari, Корзина
  и контейнеры программ не проверяются»; Unknown — «Не удалось узнать, есть ли полный доступ к диску».
- `MainViewModel(IReadOnlyList<PageViewModel> pages) : ObservableObject, INavigator` — `Pages`, `SelectedPage`
  (переход: `OnNavigatedFrom` старой, `OnNavigatedToAsync` новой), `StatusText` (последняя запись журнала),
  `VersionText` («v1.0.0»), `NavigateTo(key)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/Shell/FullDiskAccessTests.cs
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class FullDiskAccessTests
{
    [Fact]
    public void Readable_IsGranted()
    {
        using var home = new TempDir();
        home.File("Library/Application Support/com.apple.TCC/TCC.db", "x");
        Assert.Equal(FdaState.Granted, FullDiskAccess.Check(home.Path));
    }

    [Fact]
    public void Denied_IsDenied()
        => Assert.Equal(FdaState.Denied, FullDiskAccess.Check("/Users/x", _ => throw new UnauthorizedAccessException()));

    [Fact]
    public void Missing_IsUnknown()
    {
        using var home = new TempDir();
        Assert.Equal(FdaState.Unknown, FullDiskAccess.Check(home.Path));
    }
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Shell/MacShellTests.cs
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class MacShellTests
{
    [Fact]
    public async Task Open_And_Reveal_UseOpen()
    {
        var fake = new FakeCommandRunner()
            .On("/usr/bin/open", new[] { SystemSettingsLinks.FullDiskAccess }, new CommandResult(0, "", ""))
            .On("/usr/bin/open", new[] { "-R", "/Users/x/file.txt" }, new CommandResult(0, "", ""));
        var shell = new MacShell(fake);
        Assert.True(await shell.OpenAsync(SystemSettingsLinks.FullDiskAccess));
        Assert.True(await shell.RevealAsync("/Users/x/file.txt"));
    }

    [Fact]
    public async Task Failure_IsFalse() => Assert.False(await new MacShell(new FakeCommandRunner()).OpenAsync("https://example.com"));
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Shell/SettingsViewModelTests.cs
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Settings;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class SettingsViewModelTests
{
    [Fact]
    public async Task Fda_IsCheckedOnNavigation()
    {
        var state = FdaState.Denied;
        var vm = new SettingsViewModel(new MacShell(new FakeCommandRunner()), () => state);
        await vm.OnNavigatedToAsync();
        Assert.False(vm.IsFdaGranted);
        Assert.StartsWith("Полного доступа к диску нет", vm.FdaText);
        state = FdaState.Granted;
        vm.RefreshFdaCommand.Execute(null);
        Assert.True(vm.IsFdaGranted);
    }

    [Fact]
    public void OpenFdaSettings_OpensThePane()
    {
        var fake = new FakeCommandRunner();
        new SettingsViewModel(new MacShell(fake), () => FdaState.Unknown).OpenFdaSettingsCommand.Execute(null);
        Assert.Contains($"/usr/bin/open {SystemSettingsLinks.FullDiskAccess}", fake.Calls);
    }
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Shell/MainViewModelTests.cs
using PcCleaner;
using PcCleaner.Core;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class MainViewModelTests
{
    private static MainViewModel Create() => new(new PageViewModel[]
    {
        new UpcomingPageViewModel("Cleaner", "Очистка и диски", "", "Broom", ""),
        new UpcomingPageViewModel("Programs", "Программы", "", "Apps", ""),
    });

    [Fact]
    public void StartsOnTheFirstPage() => Assert.Equal("Cleaner", Create().SelectedPage!.Key);

    [Fact]
    public void NavigateTo_SelectsByKey()
    {
        var vm = Create();
        vm.NavigateTo("programs");
        Assert.Equal("Programs", vm.SelectedPage!.Key);
        vm.NavigateTo("НетТакой");
        Assert.Equal("Programs", vm.SelectedPage!.Key);
    }

    [Fact]
    public void StatusText_FollowsTheLog()
    {
        var vm = Create();
        Log.Info("Статус для теста");
        Assert.Equal("Статус для теста", vm.StatusText);
    }
}
```

- [ ] **Step 2–4:** не компилируется → реализация → `--filter "FullyQualifiedName~Shell"`: PASS.
- [ ] **Step 5: Commit** — `Mac app: navigation, settings page model`.

### Task 10: Окно, тема, страницы

**Files:** Create `src/PcCleaner.Mac/Themes/{Theme,Styles,Icons}.axaml`, `src/PcCleaner.Mac/Assets/LICENSE-FluentUI.txt`,
`src/PcCleaner.Mac/Shell/{Converters,ScoreGauge,DialogWindow.axaml(.cs),UpcomingView.axaml(.cs)}`,
`src/PcCleaner.Mac/Modules/Dashboard/DashboardView.axaml(.cs)`, `src/PcCleaner.Mac/Modules/Settings/SettingsView.axaml(.cs)`,
`src/PcCleaner.Mac/{App.axaml,App.axaml.cs,MainWindow.axaml,MainWindow.axaml.cs,Program.cs}`;
Test `tests/PcCleaner.Mac.Tests/{TestApp.cs,Ui/WindowTests.cs}`.

**Interfaces:**
- Ресурсы: кисти с ключами Windows-версии (`BackgroundBrush`, `SidebarBrush`, `SurfaceBrush`, `Surface2Brush`,
  `Surface3Brush`, `LineBrush`, `TextBrush`, `MutedBrush`, `AccentBrush`, `AccentHoverBrush`, `AccentPressedBrush`,
  `AccentTextBrush`, `AccentSoftBrush`, `DangerBrush`, `DangerSoftBrush`, `WarningBrush`, `WarningSoftBrush`,
  `SuccessBrush`, `SuccessSoftBrush`, `InfoBrush`, `InfoSoftBrush`, `HoverBrush`, `SelectionBrush`); значки
  `Icon.<Имя>` (`StreamGeometry`) — Home, Broom, Pulse, Apps, Shield, Settings, Warning, Info, Checkmark, Dismiss,
  Folder, Sync, Lock, ShieldCheckmark, Question, Error, Download, HardDrive, Globe, Document, Open: контуры `d` из
  `fluentui-system-icons/assets/<Name>/SVG/ic_fluent_<name>_20_regular.svg` (raw.githubusercontent.com, MIT; лицензия
  копируется в `Assets/LICENSE-FluentUI.txt`).
- Стили-классы: `TextBlock.H1/H2/H3/Muted/BigNumber`, `Border.Card/InnerCard/Badge`, `Button.AccentButton/DangerButton/GhostButton/LinkButton`, `ListBox.Nav`.
- Конвертеры (`Shell/Converters.cs`): `IconConverter` (имя → `Geometry` из ресурсов `Icon.<имя>`), `BrushKeyConverter`
  (ключ кисти → `IBrush`).
- `ScoreGauge : Control` — `Value` (0–100), `Stroke` (`IBrush`), `TrackBrush`; кольцо толщиной 10, дуга от верха по
  часовой стрелке.
- `DialogWindow` — тёмное окно с значком по `DialogKind`, текстом и кнопками «Да»/«Нет» или «ОК»; статические
  `Task<bool> ConfirmAsync(DialogRequest)` и `Task ShowAsync(DialogRequest)` (владелец — главное окно; без него —
  подтверждение = «Нет»). `App` подключает их к `Dialogs`.
- `App.axaml`: `FluentTheme` с палитрой `Dark` (`Accent="#34D399"`, `RegionColor="#0F1218"`), `Styles.axaml`,
  `Theme.axaml` + `Icons.axaml`, `DataTemplates` для `DashboardViewModel`, `SettingsViewModel`, `UpcomingPageViewModel`,
  `NativeMenu` («О программе WaterCa7s PC Cleaner», «Настройки…» с `Cmd+OemComma`). `App.axaml.cs`:
  `Ui.Attach(Dispatcher.UIThread)`, `AppServices.Init()`, диалоги, обработчики необработанных исключений → `Log.Error`.
- `MainWindow` — как Windows `MainWindow.xaml`: колонка 236 с логотипом (тот же рисунок 100×100) и `ListBox.Nav`,
  справа заголовок и подзаголовок страницы, полоса занятости с «Отмена», `ContentControl` страницы, строка состояния с
  версией.
- `DashboardView` — карточка оценки (`ScoreGauge`, вердикт, описание, чипы «находки», «было → стало», кнопки
  «Проверить» и «Исправить найденное»), строки разделов во время проверки, группы находок (заголовок, подсказка, карточки
  с бейджем важности, «Исправить», «Открыть раздел»), карточка «Этот Mac» (`InfoRows`).
- `SettingsView` — карточки «О программе», «Полный доступ к диску» (состояние, инструкция в 3 шага, «Открыть
  настройки», «Проверить снова»), «Журнал» (последние записи, «Открыть папку журналов»), «Данные программы».
- `Program.cs`: `Main` → `ScreenshotRunner` (Task 11) или `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`;
  `BuildAvaloniaApp() = AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()`.
- Тестам: `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`, `TestAppBuilder.BuildAvaloniaApp()` =
  `Configure<App>().UseSkia().UseHarfBuzz().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont()`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/TestApp.cs
using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(PcCleaner.Mac.Tests.TestAppBuilder))]

namespace PcCleaner.Mac.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PcCleaner.App>()
        .UseSkia()
        .UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Ui/WindowTests.cs
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Dashboard;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using PcCleaner.Modules.Settings;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

public class WindowTests
{
    internal static MainViewModel DemoViewModel(FakeCommandRunner runner)
    {
        var info = new MacInfoService(runner);
        return new MainViewModel(new PageViewModel[]
        {
            new DashboardViewModel(new IHealthContributor[] { new DiskSpaceContributor(info) }, info, new AppSettings(), null),
            new UpcomingPageViewModel("Cleaner", "Очистка и диски", "Кэши, журналы, Корзина и место на дисках", "Broom", "Раздел появится на этапе 2."),
            new SettingsViewModel(new MacShell(runner), () => FdaState.Denied),
        });
    }

    private static List<string?> Texts(Window window)
        => window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    [AvaloniaFact]
    public void Sidebar_ListsThePages_AndShowsTheFirst()
    {
        var window = new MainWindow { DataContext = DemoViewModel(MacInfoServiceTests.Mac()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(window);
        Assert.Contains("Главная", texts);
        Assert.Contains("Очистка и диски", texts);
        Assert.Contains("Настройки", texts);
        Assert.NotNull(window.CaptureRenderedFrame());
    }

    [AvaloniaFact]
    public async Task Dashboard_AfterScan_ShowsTheScore()
    {
        var vm = DemoViewModel(MacInfoServiceTests.Mac());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var dashboard = (DashboardViewModel)vm.Pages[0];
        await dashboard.OnNavigatedToAsync();
        await dashboard.ScanAsync(CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(window);
        Assert.Contains("100", texts);
        Assert.Contains("Отлично", texts);
        Assert.Contains("Apple M2", texts);
    }

    [AvaloniaFact]
    public void Navigation_ShowsTheSettingsPage()
    {
        var vm = DemoViewModel(MacInfoServiceTests.Mac());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.NavigateTo("Settings");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Texts(window), t => t is not null && t.StartsWith("Полный доступ к диску"));
    }
}
```

- [ ] **Step 2: Run** — не компилируется.
- [ ] **Step 3: Implement** — значки: `curl -sf <raw>/assets/<Name>/SVG/ic_fluent_<name>_20_regular.svg` в scratchpad,
  контуры `d` → `Icons.axaml`; остальное по интерфейсам выше и Windows `MainWindow.xaml` / `Theme.xaml`.
- [ ] **Step 4: Run** — `--filter "FullyQualifiedName~Ui"`: PASS; весь набор: PASS.
- [ ] **Step 5: Commit** — `Mac app: window, theme, dashboard and settings pages`.

### Task 11: Скриншоты (`--screenshots`) и снимки на образцах

**Files:** Create `src/PcCleaner.Mac/Shell/{ScreenshotOptions,PageCapture,ScreenshotRunner}.cs`;
Test `tests/PcCleaner.Mac.Tests/Shell/ScreenshotOptionsTests.cs`, `tests/PcCleaner.Mac.Tests/Ui/DemoScreenshotTests.cs`.

**Interfaces:**
- `record ScreenshotOptions(string Dir, IReadOnlyList<string> Pages, bool Scan, int Width, int Height)`;
  `static bool TryParse(IReadOnlyList<string> args, out ScreenshotOptions? options, out string? error)` —
  `--screenshots <папка>` обязателен, `--page A,B`, `--no-scan`, `--size 1040x660` (по умолчанию 1100×720; размер меньше
  минимального окна → ошибка).
- `static class PageCapture { Task<IReadOnlyList<string>> CaptureAsync(MainWindow window, MainViewModel vm, ScreenshotOptions o, CancellationToken ct); }`
  — для каждой страницы (или выбранных): перейти, при `Scan` — `PreviewScanAsync` с таймаутом 90 с, `RunJobs`,
  `CaptureRenderedFrame()` → `NN-Key.png`; возвращает пути файлов.
- `ScreenshotRunner.Run(ScreenshotOptions) → int` — `AppPaths.UseRoot(<временная папка>)`, `Dialogs.UseSilentMode()`,
  `AppBuilder … UseHeadless(UseHeadlessDrawing=false) … SetupWithoutStarting()`, работа через
  `Dispatcher.UIThread.Post` + `MainLoop(token)`, затем `log.txt` из `Log.Entries`; код выхода 1, если в журнале есть
  ошибки или страница не снялась.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PcCleaner.Mac.Tests/Shell/ScreenshotOptionsTests.cs
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class ScreenshotOptionsTests
{
    [Fact]
    public void NotAScreenshotRun() => Assert.False(ScreenshotOptions.TryParse(new[] { "--other" }, out _, out _));

    [Fact]
    public void Defaults()
    {
        Assert.True(ScreenshotOptions.TryParse(new[] { "--screenshots", "out" }, out var o, out var error));
        Assert.Null(error);
        Assert.Equal(("out", true, 1100, 720), (o!.Dir, o.Scan, o.Width, o.Height));
        Assert.Empty(o.Pages);
    }

    [Fact]
    public void AllOptions()
    {
        Assert.True(ScreenshotOptions.TryParse(new[] { "--screenshots", "out", "--page", "Dashboard,Settings", "--no-scan", "--size", "1040x660" }, out var o, out _));
        Assert.Equal(new[] { "Dashboard", "Settings" }, o!.Pages);
        Assert.False(o.Scan);
        Assert.Equal((1040, 660), (o.Width, o.Height));
    }

    [Theory]
    [InlineData("--screenshots")]
    [InlineData("--screenshots out --size 10x10")]
    [InlineData("--screenshots out --size большой")]
    public void Errors(string line)
    {
        Assert.True(ScreenshotOptions.TryParse(line.Split(' '), out var o, out var error));
        Assert.Null(o);
        Assert.NotNull(error);
    }
}
```

```csharp
// tests/PcCleaner.Mac.Tests/Ui/DemoScreenshotTests.cs
using Avalonia.Headless.XUnit;
using PcCleaner.Mac.Tests.Dashboard;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

/// <summary>Pages on fixture data; saved to MAC_SCREENSHOTS_DIR when it is set (CI artifact, local layout checks).</summary>
public class DemoScreenshotTests
{
    [AvaloniaFact]
    public async Task EveryPage_Renders()
    {
        var dir = Environment.GetEnvironmentVariable("MAC_SCREENSHOTS_DIR") is { Length: > 0 } d ? d : new TempDir().Path;
        var vm = WindowTests.DemoViewModel(MacInfoServiceTests.Mac());
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 720 };
        window.Show();
        var files = await PageCapture.CaptureAsync(window, vm, new ScreenshotOptions(dir, Array.Empty<string>(), true, 1100, 720), CancellationToken.None);
        Assert.Equal(vm.Pages.Count, files.Count);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length > 10_000));
    }
}
```

- [ ] **Step 2–4:** не компилируется → реализация → весь набор PASS; локально
  `MAC_SCREENSHOTS_DIR=<scratchpad> dotnet test … --filter "FullyQualifiedName~DemoScreenshot"` и посмотреть снимки;
  `dotnet run --project src/PcCleaner.Mac -- --screenshots <scratchpad>/real --no-scan` — код 0 (на Windows утилит
  macOS нет, страницы показывают «нет данных», ошибок в журнале быть не должно: отсутствие утилиты — `Warn`).
- [ ] **Step 5: Commit** — `Mac app: screenshot mode`.

### Task 12: Иконка программы

**Files:** Create `tools/mac/make-icon.ps1`, `src/PcCleaner.Mac/Assets/AppIcon.icns`;
Test `tests/PcCleaner.Mac.Tests/Packaging/AppIconTests.cs` (тестовый `.csproj`: `<None Include="..\..\src\PcCleaner.Mac\Assets\AppIcon.icns" Link="Assets\AppIcon.icns" CopyToOutputDirectory="PreserveNewest" />`).

`make-icon.ps1` (Windows PowerShell 5.1, WPF-рисование как в Windows `tools/make-icon.ps1`): холст 1024, плитка 824×824
со сдвигом 100 (сетка значков macOS), рисунок 100×100 с плиткой `#34D399`, каплей `#06261A` и глазами; размеры
16, 32, 64, 128, 256, 512, 1024 → `.icns`: заголовок `icns` + длина (big-endian), записи PNG `icp4`(16), `icp5`(32),
`icp6`(64), `ic07`(128), `ic08`(256), `ic09`(512), `ic10`(1024), `ic11`(32), `ic12`(64), `ic13`(256), `ic14`(512).
Глаза — с 32 px (меньше сливаются). Параметр `-PreviewDir` сохраняет PNG для просмотра.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/PcCleaner.Mac.Tests/Packaging/AppIconTests.cs
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace PcCleaner.Mac.Tests.Packaging;

public class AppIconTests
{
    [Fact]
    public void Icns_HasEveryMacSize()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.icns"));
        Assert.Equal("icns", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(bytes.Length, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4)));
        var sizes = new Dictionary<string, int>();
        for (var at = 8; at < bytes.Length;)
        {
            var type = Encoding.ASCII.GetString(bytes, at, 4);
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at + 4));
            Assert.Equal(0x89, bytes[at + 8]); // PNG signature
            sizes[type] = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at + 8 + 16)); // IHDR width
            at += length;
        }
        Assert.Equal(1024, sizes["ic10"]);
        Assert.Equal(512, sizes["ic09"]);
        Assert.Equal(16, sizes["icp4"]);
        Assert.Equal(32, sizes["ic11"]);
        Assert.Equal(11, sizes.Count);
    }
}
```

- [ ] **Step 2–4:** тест падает (нет файла) → `powershell -File tools\mac\make-icon.ps1 -PreviewDir <scratchpad>` →
  посмотреть 1024 и 32 → PASS.
- [ ] **Step 5: Commit** — `Mac app: app icon`.

### Task 13: Упаковка `.app`, подпись, сбор образцов

**Files:** Create `tools/mac/Info.plist`, `tools/mac/make-app.sh`, `tools/mac/sign.sh`, `tools/mac/capture-fixtures.sh`;
Test `tests/PcCleaner.Mac.Tests/Packaging/InfoPlistTests.cs` (тестовый `.csproj`: `<None Include="..\..\tools\mac\Info.plist" Link="Assets\Info.plist" CopyToOutputDirectory="PreserveNewest" />`).

- `Info.plist` — ключи из спецификации, версия `@VERSION@`.
- `make-app.sh <out>`: версия из `<Version>` проекта; `dotnet publish … -c Release -r osx-arm64 --self-contained true -p:DebugType=none -o <out>/publish`;
  `<out>/WaterCa7s PC Cleaner.app/Contents/{MacOS,Resources}`; `cp -R publish/. MacOS/`; иконка; `Info.plist` с версией,
  `plutil -lint`; `sign.sh`; `ditto -c -k --keepParent` → `<out>/WaterCa7sPcCleaner-osx-arm64.zip`.
- `sign.sh <app>`: при `MAC_SIGNING_P12` — временная связка ключей (`security create-keychain`, `import -T /usr/bin/codesign`,
  `set-key-partition-list`, в список поиска), личность «WaterCa7s PC Cleaner Signing»; иначе `-`; сначала каждый Mach-O
  в `Contents/MacOS` (кроме главного файла, `file -b` содержит `Mach-O`), потом bundle; `codesign --verify --deep --strict --verbose=2`,
  `codesign -d -r-`; связка ключей удаляется в `trap`.
- `capture-fixtures.sh <out>`: только чтение — `sw_vers`, `sysctl -n kern.boottime`, `sysctl -n hw.memsize`,
  `system_profiler SPHardwareDataType -json`, `df -k -P /System/Volumes/Data`, `df -k -P /`, `diskutil info -plist /`,
  `tmutil listlocalsnapshots /`, `ps` (оба варианта), `launchctl list`, `launchctl print gui/$UID`,
  `launchctl print-disabled gui/$UID`, `launchctl print-disabled system`, `codesign -dv --verbose=2` и
  `codesign --verify --strict` для `/bin/ls`, `/System/Applications/Calculator.app`, `$(command -v dotnet)` и собранного
  `.app` (если есть), `spctl --status`, `spctl --assess --type execute -vv` для Calculator, `csrutil status`,
  `fdesetup status`, `socketfilterfw --getglobalstate/--getstealthmode`, `scutil --proxy`, `scutil --dns`,
  `networksetup -listallnetworkservices`, `system_profiler SPConfigurationProfileDataType -json`,
  `plutil -convert xml1 -o - /System/Library/LaunchDaemons/com.openssh.sshd.plist`, `ls -la` папок Launch*; каждая
  команда → `<имя>.txt` + `<имя>.exit`, таймаут 60 с. Замена на заглушки: `$USER`, `hostname`, `ComputerName`,
  `serial_number`, `platform_UUID`, `provisioning_UDID`, MAC-адреса (`([0-9a-f]{2}:){5}[0-9a-f]{2}`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/PcCleaner.Mac.Tests/Packaging/InfoPlistTests.cs
using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Packaging;

public class InfoPlistTests
{
    [Fact]
    public void Template_HasTheRequiredKeys()
    {
        var d = Plist.ParseDict(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Info.plist")));
        Assert.Equal(AppInfo.BundleId, d.GetString("CFBundleIdentifier"));
        Assert.Equal("WaterCa7sPcCleaner", d.GetString("CFBundleExecutable"));
        Assert.Equal("@VERSION@", d.GetString("CFBundleShortVersionString"));
        Assert.Equal("14.0", d.GetString("LSMinimumSystemVersion"));
        Assert.Equal("AppIcon", d.GetString("CFBundleIconFile"));
        Assert.Equal("ru", d.GetString("CFBundleDevelopmentRegion"));
        Assert.Contains("Finder", d.GetString("NSAppleEventsUsageDescription"));
    }
}
```

- [ ] **Step 2–4:** тест падает → файлы → PASS; `bash -n` для каждого скрипта (синтаксис); `git update-index --chmod=+x tools/mac/*.sh`.
- [ ] **Step 5: Commit** — `Mac app: .app packaging, signing, fixture capture`.

### Task 14: CI на macOS

**Files:** Create `.github/workflows/ci.yml`.

Задача `mac-app`, матрица `macos-14`, `macos-latest`, `fail-fast: false`: `actions/checkout@v4`,
`actions/setup-dotnet@v4` (`10.0.x`), сборка, тесты с `MAC_SCREENSHOTS_DIR=${{ github.workspace }}/demo-screenshots`,
`dotnet src/PcCleaner.Mac/bin/Release/net10.0/WaterCa7sPcCleaner.dll --screenshots screenshots`,
`tools/mac/capture-fixtures.sh fixtures` (`if: always()`); на `macos-latest` — `tools/mac/make-app.sh out` с
секретами `MAC_SIGNING_P12`/`MAC_SIGNING_PASSWORD` и `tools/mac/capture-fixtures.sh` ещё раз для `.app`; артефакты:
`WaterCa7sPcCleaner-osx-arm64` (`out/*.zip`), `mac-screenshots-${{ matrix.os }}` (`screenshots`, `demo-screenshots`),
`mac-fixtures-${{ matrix.os }}` (`fixtures`).

- [ ] **Step 1:** `ci.yml`; коммит `CI: build, test and package on macOS`; `git push`.
- [ ] **Step 2:** `gh run watch` / `gh run view --log-failed`; чинить до зелёного (каждое исправление — свой коммит).
- [ ] **Step 3:** `gh run download` → посмотреть скриншоты обеих машин, `log.txt`, образцы.
- [ ] **Step 4:** заменить образцы из Task 4–6 настоящими (`sw_vers`, `sysctl`, `system_profiler`, `df`) после
  проверки на личные данные; если формат отличается от ожидаемого — сначала тест на настоящий образец, потом парсер.
- [ ] **Step 5:** коммит `Fixtures: real macOS output from CI`, `git push`, CI зелёный.

### Task 15: Сертификат подписи

- [ ] **Step 1:** `openssl` (Git Bash) в `%USERPROFILE%\WaterCa7sPcCleaner-signing\mac\`: ключ RSA 2048, сертификат на 10 лет
  (`CN=WaterCa7s PC Cleaner Signing`, `keyUsage=critical,digitalSignature`, `extendedKeyUsage=critical,codeSigning`,
  `basicConstraints=critical,CA:false`), `.p12` с паролем (`-keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1`),
  пароль — в `password.txt` там же. Ничего из этого не попадает в репозиторий.
- [ ] **Step 2:** спросить пользователя; после «да» — `gh secret set MAC_SIGNING_P12` (base64 `.p12`) и
  `gh secret set MAC_SIGNING_PASSWORD` в `WaterCa7/WaterCa7-s-pc-cleaner-mac`.
- [ ] **Step 3:** перезапустить CI; в журнале `codesign -d -r-` — `certificate leaf = H"…"`, а не `cdhash`; `codesign --verify` зелёный.
- [ ] **Step 4:** если `codesign` не принимает недоверенный сертификат — в `sign.sh` добавить
  `security add-trusted-cert -r trustAsRoot -p codeSign -k <связка> <cert>` (только во временную связку CI) и
  повторить; коммит `Signing: trust the certificate in the CI keychain`.
- [ ] **Step 5:** сказать пользователю, где резервная копия, и попросить сохранить её вместе с ключом обновлений.

### Task 16: Итог этапа

- [ ] **Step 1:** `docs/ARCHITECTURE.md` Mac-репозитория — коротко: структура, Core API этапа 1, тема, правила
  безопасности, режим скриншотов, как добавлять раздел (по образцу Windows-документа).
- [ ] **Step 2:** весь набор тестов локально и в CI зелёный; скриншоты просмотрены.
- [ ] **Step 3:** коммит `Docs: Mac architecture`, `git push`.
- [ ] **Step 4:** пользователю — ссылка на артефакт `.app` последнего зелёного запуска и инструкция первого запуска
  (распаковать, перенести в «Программы», «Всё равно открыть»), что проверить: окно, тема, меню, «Главная» → «Проверить»,
  сведения о Mac, «Настройки» → полный доступ к диску.
- [ ] **Step 5:** после ответа пользователя — план этапа 2.
