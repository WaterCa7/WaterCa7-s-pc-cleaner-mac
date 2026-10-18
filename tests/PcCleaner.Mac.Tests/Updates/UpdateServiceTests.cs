using PcCleaner.Core;
using PcCleaner.Core.Updates;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Updates;

/// <summary>The whole flow on temporary folders with a fake GitHub and a fake codesign (no network, nothing is started).</summary>
public sealed class UpdateServiceTests : IDisposable
{
    private const string ManifestUrl = "https://github.com/WaterCa7/WaterCa7-s-pc-cleaner-mac/releases/latest/download/update.json";
    private const string ZipName = "WaterCa7sPcCleaner-osx-arm64.zip";

    private readonly TempDir _dir = new();
    private readonly UpdateTestKit _kit = new();
    private readonly FakeHttp _http = new();
    private readonly FakeCommandRunner _runner = new();
    private readonly AppSettings _settings = new();
    private readonly List<(string App, IReadOnlyList<string> Args)> _relaunched = new();
    private readonly string _app;
    private bool _writable = true;

    public UpdateServiceTests()
    {
        _app = MacUpdateKit.InstalledApp(_dir, "1.1.0");
        _runner.On("/usr/bin/codesign", new[] { "--verify", "--deep", "--strict", _app + BundleInstaller.NewSuffix }, new CommandResult(0, "", ""));
    }

    private string Executable => Path.Combine(_app, "Contents", "MacOS", "WaterCa7sPcCleaner");

    private UpdateService Service(string current = "1.1.0", string? executable = null) => new(new UpdateEnvironment
    {
        ExecutablePath = executable ?? Executable,
        UpdatesDir = Path.Combine(_dir.Path, "Updates"),
        CurrentVersion = Version.Parse(current + ".0"),
        Runner = _runner,
        PublicKeyPem = _kit.PublicKeyPem,
        ManifestUrl = ManifestUrl,
        HttpHandler = _http,
        Relaunch = (app, args) =>
        {
            _relaunched.Add((app, args));
            return true;
        },
        CanWrite = _ => _writable,
        SaveSettings = _ => { },
    }, () => _settings);

    private void Publish(string version, byte[]? zip = null, bool badSignature = false)
    {
        zip ??= MacUpdateKit.BundleZip(version);
        var json = UpdateTestKit.ManifestJson(version, zip.Length, UpdateTestKit.Sha256(zip), notes: "Автообновление");
        _http.Add(ManifestUrl, json)
            .Add(ManifestUrl + ".sig", badSignature ? _kit.Sign(new byte[] { 0 }) : _kit.Sign(json))
            .Add(UpdateTestKit.Url(version, ZipName), zip);
    }

    private string InstalledVersion => File.ReadAllText(Path.Combine(_app, "Contents", "Info.plist"));

    [Fact]
    public async Task FullCycle_DownloadThenInstallAtTheNextStartThenCleanUp()
    {
        Publish("1.2.0");
        var first = Service();
        await first.CheckAsync(manual: false, CancellationToken.None);
        Assert.True(first.State == UpdateState.Ready, first.StatusText);
        Assert.Equal(new Version(1, 2, 0), first.Manifest!.Version);
        Assert.NotNull(_settings.LastUpdateCheckUtc);
        Assert.Contains("<string>1.1.0</string>", InstalledVersion, StringComparison.Ordinal);

        var second = Service();
        Assert.True(await second.TryInstallStagedAsync(new[] { "--page", "Cleaner" }));
        Assert.Contains("<string>1.2.0</string>", InstalledVersion, StringComparison.Ordinal);
        var (app, args) = Assert.Single(_relaunched);
        Assert.Equal(_app, app);
        Assert.Equal(new[] { "--page", "Cleaner", UpdateService.UpdatedFromArg, "1.1.0" }, args);
        Assert.True(Directory.Exists(_app + BundleInstaller.OldSuffix));

        var updated = Service("1.2.0");
        updated.CompleteUpdate("1.1.0");
        Assert.Equal(UpdateState.Updated, updated.State);
        Assert.Equal("Автообновление", updated.Manifest!.Notes);
        Assert.False(Directory.Exists(_app + BundleInstaller.OldSuffix));
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "Updates", "1.2.0")));
    }

    [Fact]
    public async Task UpToDate_AndBadSignature_ChangeNothing()
    {
        Publish("1.1.0");
        var service = Service();
        await service.CheckAsync(manual: true, CancellationToken.None);
        Assert.Equal(UpdateState.UpToDate, service.State);

        Publish("1.2.0", badSignature: true);
        await service.CheckAsync(manual: true, CancellationToken.None);
        Assert.Equal(UpdateState.Failed, service.State);
        Assert.Contains("подпись", service.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(_http.Requests, r => r.EndsWith(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AutomaticCheckOff_DoesNotGoOnline_ButTheButtonDoes()
    {
        Publish("1.2.0");
        _settings.AutoUpdate = false;
        var service = Service();
        await service.CheckAsync(manual: false, CancellationToken.None);
        Assert.Empty(_http.Requests);
        Assert.False(await service.TryInstallStagedAsync(Array.Empty<string>()));
        await service.CheckAsync(manual: true, CancellationToken.None);
        Assert.Equal(UpdateState.Ready, service.State);
    }

    [Fact]
    public async Task DevelopmentBuild_NeverUpdates()
    {
        Publish("1.2.0");
        var dev = Service(executable: Path.Combine(_dir.Path, "bin", "Release", "WaterCa7sPcCleaner"));
        Assert.False(dev.CanUpdate);
        await dev.CheckAsync(manual: true, CancellationToken.None);
        Assert.Equal(UpdateState.Failed, dev.State);
        Assert.Contains("сборка для разработки", dev.StatusText, StringComparison.Ordinal);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task FolderNotWritable_AsksToDownloadByHand()
    {
        Publish("1.2.0");
        _writable = false;
        var service = Service();
        await service.CheckAsync(manual: false, CancellationToken.None);
        Assert.Equal(UpdateState.ManualRequired, service.State);
        Assert.Contains("нет прав на запись", service.StatusText, StringComparison.Ordinal);
        Assert.Equal("Скачать вручную", UpdateBanner.For(service.State, service.Manifest, service.CurrentVersion)!.Action);
    }

    [Fact]
    public void TranslocatedOrOnADiskImage_IsNotReplaceable()
    {
        var env = new UpdateEnvironment
        {
            ExecutablePath = "/private/var/folders/zz/T/AppTranslocation/1234/d/WaterCa7s PC Cleaner.app/Contents/MacOS/WaterCa7sPcCleaner",
            UpdatesDir = _dir.Path, CurrentVersion = new Version(1, 0, 0), Runner = _runner, CanWrite = _ => true,
        };
        Assert.Contains("временной копии", env.WhyNotReplaceable, StringComparison.Ordinal);
        var dmg = new UpdateEnvironment
        {
            ExecutablePath = "/Volumes/WaterCa7s/WaterCa7s PC Cleaner.app/Contents/MacOS/WaterCa7sPcCleaner",
            UpdatesDir = _dir.Path, CurrentVersion = new Version(1, 0, 0), Runner = _runner, CanWrite = _ => true,
        };
        Assert.Contains("образа диска", dmg.WhyNotReplaceable, StringComparison.Ordinal);
        Assert.Equal("/Volumes/WaterCa7s/WaterCa7s PC Cleaner.app", dmg.AppBundlePath);
    }

    [Fact]
    public async Task FailedInstall_KeepsTheApp_AndAfterTwoFailuresAsksForAManualDownload()
    {
        Publish("1.2.0");
        await Service().CheckAsync(manual: false, CancellationToken.None);
        _runner.On("/usr/bin/codesign", new[] { "--verify", "--deep", "--strict", _app + BundleInstaller.NewSuffix }, new CommandResult(1, "", "invalid signature"));

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var service = Service();
            Assert.False(await service.TryInstallStagedAsync(Array.Empty<string>()));
            Assert.Equal(UpdateState.Failed, service.State);
            Assert.Contains("<string>1.1.0</string>", InstalledVersion, StringComparison.Ordinal);
        }
        var third = Service();
        Assert.False(await third.TryInstallStagedAsync(Array.Empty<string>()));
        Assert.Equal(UpdateState.ManualRequired, third.State);
        Assert.Empty(_relaunched);
    }

    [Fact]
    public async Task WindowStrip_AndSettings_FollowTheService()
    {
        Publish("1.2.0");
        _writable = false;
        var service = Service();
        var opened = new List<string>();
        var main = new MainViewModel(Array.Empty<PageViewModel>()) { Updates = service, OpenUrl = opened.Add };
        Assert.False(main.ShowUpdateBanner);

        await service.CheckAsync(manual: false, CancellationToken.None);
        Assert.True(main.ShowUpdateBanner);
        Assert.Equal(("Версию 1.2.0 не получилось установить автоматически.", "Скачать вручную"), (main.UpdateBannerText, main.UpdateBannerAction));
        await main.UpdateBannerCommand.ExecuteAsync();
        Assert.Equal(new[] { UpdateService.ReleasesUrl }, opened);
        main.DismissUpdateBannerCommand.Execute(null);
        Assert.False(main.ShowUpdateBanner);

        var settings = new PcCleaner.Modules.Settings.SettingsViewModel(new MacShell(_runner), () => FdaState.Granted);
        Assert.False(settings.HasUpdates);
        settings.UseUpdates(service, _settings);
        Assert.True(settings.HasUpdates);
        Assert.True(settings.AutoUpdate);
        Assert.Contains("не получилось установить автоматически", settings.UpdateStatus, StringComparison.Ordinal);
        Assert.StartsWith("Последняя проверка: сегодня", settings.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartArgs_ReplaceAnOlderMarker()
        => Assert.Equal(new[] { "--x", UpdateService.UpdatedFromArg, "1.1.0" },
            UpdateService.RestartArgs(new[] { UpdateService.UpdatedFromArg, "1.0.0", "--x" }, new Version(1, 1, 0)));

    public void Dispose()
    {
        _kit.Dispose();
        _dir.Dispose();
    }
}
