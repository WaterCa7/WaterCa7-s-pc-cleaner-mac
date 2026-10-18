using PcCleaner.Core;
using PcCleaner.Core.Updates;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Updates;

public class BundleInstallerTests
{
    private const string Requirement = "identifier \"io.github.waterca7.pccleaner\" and certificate root = H\"8a8b802b4d202b596decf1f4e1dd69a219854f34\"";

    private static StagedUpdate Stage(TempDir dir, byte[] zip, string version)
    {
        var path = Path.Combine(dir.Path, "Updates", version, "WaterCa7sPcCleaner-osx-arm64.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, zip);
        var manifest = new UpdateManifest(new Version(version), DateTimeOffset.UtcNow, "Что нового", MacUpdateKit.FileFor(zip, version));
        return new StagedUpdate(manifest, path);
    }

    private static FakeCommandRunner Codesign(string app, bool verifies = true, string? requirement = Requirement, bool sameSigner = true)
    {
        var newApp = app + BundleInstaller.NewSuffix;
        var fake = new FakeCommandRunner()
            .On("/usr/bin/codesign", new[] { "--verify", "--deep", "--strict", newApp }, new CommandResult(verifies ? 0 : 1, "", verifies ? "" : $"{newApp}: invalid signature"))
            .On("/usr/bin/codesign", new[] { "-d", "-r-", app },
                new CommandResult(0, requirement is null ? "" : $"designated => {requirement}\n", $"Executable={app}/Contents/MacOS/WaterCa7sPcCleaner\n"));
        if (requirement is not null)
            fake.On("/usr/bin/codesign", new[] { "--verify", "-R=" + requirement, newApp }, new CommandResult(sameSigner ? 0 : 3, "", sameSigner ? "" : "test-requirement: code failed to satisfy specified code requirement(s)"));
        return fake;
    }

    [Fact]
    public async Task Install_SwapsTheBundle_AfterTheChecks()
    {
        using var dir = new TempDir();
        var app = MacUpdateKit.InstalledApp(dir);
        var staged = Stage(dir, MacUpdateKit.BundleZip("1.1.0"), "1.1.0");
        var fake = Codesign(app);
        await new BundleInstaller(fake).InstallAsync(staged, app, CancellationToken.None);

        Assert.Contains("<string>1.1.0</string>", File.ReadAllText(Path.Combine(app, "Contents", "Info.plist")), StringComparison.Ordinal);
        Assert.Equal("old 1.0.0", File.ReadAllText(Path.Combine(app + BundleInstaller.OldSuffix, "Contents", "MacOS", "WaterCa7sPcCleaner")));
        Assert.False(Directory.Exists(app + BundleInstaller.NewSuffix));
        Assert.Contains($"/usr/bin/codesign --verify -R={Requirement} {app}{BundleInstaller.NewSuffix}", fake.Calls);

        BundleInstaller.Cleanup(app);
        Assert.False(Directory.Exists(app + BundleInstaller.OldSuffix));
        Assert.True(Directory.Exists(app));
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("signer")]
    [InlineData("version")]
    [InlineData("id")]
    public async Task FailedCheck_KeepsTheRunningApp(string what)
    {
        using var dir = new TempDir();
        var app = MacUpdateKit.InstalledApp(dir);
        var zip = what switch
        {
            "version" => MacUpdateKit.BundleZip("1.2.0"),
            "id" => MacUpdateKit.BundleZip("1.1.0", id: "com.example.other"),
            _ => MacUpdateKit.BundleZip("1.1.0"),
        };
        var staged = Stage(dir, zip, "1.1.0");
        var fake = Codesign(app, verifies: what != "signature", sameSigner: what != "signer");
        await Assert.ThrowsAsync<UpdateException>(() => new BundleInstaller(fake).InstallAsync(staged, app, CancellationToken.None));
        Assert.Equal("old 1.0.0", File.ReadAllText(Path.Combine(app, "Contents", "MacOS", "WaterCa7sPcCleaner")));
        Assert.False(Directory.Exists(app + BundleInstaller.NewSuffix));
        Assert.False(Directory.Exists(app + BundleInstaller.OldSuffix));
    }

    [Fact]
    public async Task AdHocRunningApp_HasNoSignerToCompare()
    {
        using var dir = new TempDir();
        var app = MacUpdateKit.InstalledApp(dir);
        var fake = Codesign(app, requirement: "cdhash H\"1234\"");
        fake.On("/usr/bin/codesign", new[] { "-d", "-r-", app }, new CommandResult(0, "designated => cdhash H\"1234abcd\"\n", ""));
        Assert.Null(await new BundleInstaller(fake).RequirementAsync(app, CancellationToken.None));
        await new BundleInstaller(fake).InstallAsync(Stage(dir, MacUpdateKit.BundleZip("1.1.0"), "1.1.0"), app, CancellationToken.None);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("-R=", StringComparison.Ordinal));
    }

    [MacFact]
    public async Task RealMac_AdHocSignedCopyVerifies()
    {
        using var dir = new TempDir();
        var target = Path.Combine(dir.Path, "Applications", "Copy.app");
        AppZip.ExtractBytes(MacUpdateKit.BundleZip("1.1.0"), target);
        var runner = new ProcessCommandRunner();
        // A shell-script "executable" is enough for an ad-hoc signature of the bundle.
        File.WriteAllText(Path.Combine(target, "Contents", "MacOS", "WaterCa7sPcCleaner"), "#!/bin/sh\nexit 0\n");
        var sign = await runner.RunAsync("/usr/bin/codesign", new[] { "--force", "--sign", "-", target });
        Assert.True(sign.Ok, sign.StdErr);
        var installer = new BundleInstaller(runner);
        await installer.VerifyAsync(target, new Version(1, 1, 0), target, CancellationToken.None);
        File.AppendAllText(Path.Combine(target, "Contents", "Resources", "AppIcon.icns"), "changed");
        await Assert.ThrowsAsync<UpdateException>(() => installer.VerifyAsync(target, new Version(1, 1, 0), target, CancellationToken.None));
    }
}
