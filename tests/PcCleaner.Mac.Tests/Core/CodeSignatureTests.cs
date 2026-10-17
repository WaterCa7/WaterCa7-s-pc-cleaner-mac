using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class CodeSignatureTests
{
    [Theory]
    [InlineData("codesign/apple-ls.txt", SignatureKind.Apple, "Apple")]
    [InlineData("codesign/apple-calculator.txt", SignatureKind.Apple, "Apple")]
    [InlineData("codesign/developer-id-dotnet.txt", SignatureKind.DeveloperId, "Developer ID: Microsoft Corporation (UBF8T346G9)")]
    [InlineData("codesign/app-store.txt", SignatureKind.AppStore, "App Store (74J34U3R6X)")]
    [InlineData("codesign/self-signed-app.txt", SignatureKind.OtherCertificate, "сертификат «WaterCa7s PC Cleaner Signing» (не проверен Apple)")]
    [InlineData("codesign/adhoc-app.txt", SignatureKind.AdHoc, "ad-hoc — без разработчика")]
    public void Parse_RealOutput(string fixture, SignatureKind kind, string text)
    {
        var info = CodeSignatureParser.Parse(Fixture.Text(fixture), 0);
        Assert.Equal(kind, info.Kind);
        Assert.Equal(text, info.Text);
    }

    [Fact]
    public void Parse_Unsigned() => Assert.Equal(SignatureKind.Unsigned, CodeSignatureParser.Parse(Fixture.Text("codesign/unsigned.txt"), 1).Kind);

    [Fact]
    public void Parse_MissingFile_IsUnknown()
        => Assert.Equal(SignatureKind.Unknown, CodeSignatureParser.Parse("/x: No such file or directory", 1).Kind);

    [Fact]
    public void Parse_KeepsTeamAndIdentifier()
    {
        var info = CodeSignatureParser.Parse(Fixture.Text("codesign/developer-id-dotnet.txt"), 0);
        Assert.Equal(("UBF8T346G9", "com.microsoft.dotnet-host"), (info.TeamId, info.Identifier));
        Assert.True(info.FromKnownDeveloper);
        Assert.False(CodeSignatureParser.Parse(Fixture.Text("codesign/adhoc-app.txt"), 0).FromKnownDeveloper);
    }

    [Fact]
    public void Verify()
    {
        Assert.False(CodeSignatureParser.Tampered(new CommandResult(0, "", "")));
        Assert.True(CodeSignatureParser.Tampered(new CommandResult(1, "", Fixture.Text("codesign/verify-invalid.txt"))));
        Assert.True(CodeSignatureParser.Tampered(new CommandResult(1, "", "/Applications/X.app: a sealed resource is missing or invalid\nfile added: /Applications/X.app/Contents/Frameworks/evil.dylib")));
        // Not proof of tampering: a timeout, a file codesign may not read, an old signature format.
        Assert.False(CodeSignatureParser.Tampered(new CommandResult(1, "", Fixture.Text("codesign/verify-invalid.txt"), TimedOut: true)));
        Assert.False(CodeSignatureParser.Tampered(new CommandResult(1, "", "/Applications/X.app: Permission denied")));
        Assert.False(CodeSignatureParser.Tampered(new CommandResult(3, "", "/Applications/Old.app: resource envelope is obsolete (version 1 signature)")));
    }

    [Theory]
    [InlineData("spctl/apple.txt", 0, true, "Apple System", false)]
    [InlineData("spctl/notarized.txt", 0, true, "Notarized Developer ID", true)]
    [InlineData("spctl/rejected.txt", 3, false, null, false)]
    public void Spctl(string fixture, int exit, bool accepted, string? source, bool notarized)
    {
        var verdict = CodeSignatureParser.ParseSpctl(Fixture.Text(fixture), exit);
        Assert.Equal((accepted, source, notarized), (verdict.Accepted, verdict.Source, verdict.Notarized));
    }

    [Fact]
    public async Task Service_BrokenSignature_IsInvalid_AndCached()
    {
        using var dir = new TempDir();
        var app = dir.File("Broken.app/Contents/MacOS/Broken", "x");
        var fake = new FakeCommandRunner()
            .On("/usr/bin/codesign", new[] { "-dv", "--verbose=2", app }, new CommandResult(0, "", Fixture.Text("codesign/developer-id-dotnet.txt")))
            .On("/usr/bin/codesign", new[] { "--verify", "--strict", app }, new CommandResult(1, "", Fixture.Text("codesign/verify-invalid.txt")));
        var service = new CodeSignatureService(fake);
        Assert.Equal(SignatureKind.Invalid, (await service.GetAsync(app, CancellationToken.None)).Kind);
        await service.GetAsync(app, CancellationToken.None);
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public async Task Service_WithoutVerify_RunsOnlyTheDetails()
    {
        var fake = new FakeCommandRunner()
            .On("/usr/bin/codesign", new[] { "-dv", "--verbose=2", "/Applications/Big.app" }, new CommandResult(0, "", Fixture.Text("codesign/developer-id-dotnet.txt")));
        var service = new CodeSignatureService(fake, _ => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(SignatureKind.DeveloperId, (await service.GetAsync("/Applications/Big.app", CancellationToken.None, verify: false)).Kind);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task Service_MissingFile_DoesNotRunCodesign()
    {
        var fake = new FakeCommandRunner();
        Assert.Equal(SignatureKind.Unknown, (await new CodeSignatureService(fake).GetAsync("/no/such/file", CancellationToken.None)).Kind);
        Assert.Empty(fake.Calls);
    }

    [MacFact]
    public async Task RealMac_LsIsApple()
        => Assert.Equal(SignatureKind.Apple, (await new CodeSignatureService(new ProcessCommandRunner()).GetAsync("/bin/ls", CancellationToken.None)).Kind);

    [MacFact]
    public async Task RealMac_CalculatorIsAccepted()
        => Assert.True((await new CodeSignatureService(new ProcessCommandRunner()).AssessAsync("/System/Applications/Calculator.app", CancellationToken.None)).Accepted);
}
