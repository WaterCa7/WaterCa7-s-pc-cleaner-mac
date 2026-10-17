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
    public void Parse_NotADict_Throws() => Assert.Throws<PlistException>(() => Plist.ParseDict("<plist><array/></plist>"));

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

    [Fact]
    public async Task ReadAsync_MissingFile_GivesAProblem()
    {
        var result = await PlistFile.ReadAsync(Path.Combine(TestEnvironment.Root, "нет.plist"), new FakeCommandRunner(), CancellationToken.None);
        Assert.Contains("не найдены", result.Problem);
    }

    [MacFact]
    public async Task RealSystemPlist_IsRead()
    {
        var result = await PlistFile.ReadAsync("/System/Library/CoreServices/SystemVersion.plist", new ProcessCommandRunner(), CancellationToken.None);
        Assert.Equal("macOS", result.Value!.GetString("ProductName"));
    }
}
