using PcCleaner.Mac.Tests.Support;
using System.Text;
using PcCleaner.Core.Updates;
using Xunit;

namespace PcCleaner.Mac.Tests.Updates;

public class UpdateSignatureTests
{
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("{\"version\":\"1.2.0\"}");

    [Fact]
    public void Verify_AcceptsTheSignatureOfTheSameBytes()
    {
        using var kit = new UpdateTestKit();
        Assert.True(UpdateSignature.Verify(Data, kit.Sign(Data), kit.PublicKeyPem));
    }

    [Fact]
    public void Verify_RejectsChangedBytesAnotherKeyAndGarbage()
    {
        using var kit = new UpdateTestKit();
        using var other = new UpdateTestKit();
        var signature = kit.Sign(Data);
        var changed = Encoding.UTF8.GetBytes("{\"version\":\"9.9.9\"}");

        Assert.False(UpdateSignature.Verify(changed, signature, kit.PublicKeyPem));
        Assert.False(UpdateSignature.Verify(Data, signature, other.PublicKeyPem));
        Assert.False(UpdateSignature.Verify(Data, new byte[] { 1, 2, 3 }, kit.PublicKeyPem));
        Assert.False(UpdateSignature.Verify(Data, signature, "not a key"));
        Assert.False(UpdateSignature.Verify(Data, Array.Empty<byte>(), kit.PublicKeyPem));
    }

    [Fact]
    public void EmbeddedKey_IsTheOneInTools()
    {
        var file = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "update-signing-public.pem"));
        Assert.Equal(Normalize(file), Normalize(UpdateKeys.PublicKeyPem));
        Assert.DoesNotContain("PRIVATE", UpdateKeys.PublicKeyPem);
    }

    /// <summary>Runs when UPDATE_MANIFEST_DIR points to the output of tools/make-update-manifest.ps1 (CI dry run, release check).</summary>
    [ManifestDirFact]
    public void ScriptManifest_IsAcceptedByTheApp()
    {
        var dir = Environment.GetEnvironmentVariable(ManifestDirFactAttribute.Variable)!;
        var json = File.ReadAllBytes(Path.Combine(dir, "update.json"));
        var keyFile = Path.Combine(dir, "public.pem");
        var key = File.Exists(keyFile) ? File.ReadAllText(keyFile) : UpdateKeys.PublicKeyPem;

        Assert.True(UpdateSignature.Verify(json, File.ReadAllBytes(Path.Combine(dir, "update.json.sig")), key));
        foreach (var platform in ReleasePlatforms)
        {
            var manifest = UpdateManifest.Parse(json, platform, out var problem);
            Assert.True(manifest is not null, $"{platform}: {problem}");
            Assert.False(string.IsNullOrWhiteSpace(manifest!.Notes));
            Assert.Equal(PcCleaner.Core.AppInfo.Version, manifest.Version.ToString(3));
        }
    }

    /// <summary>
    /// The real zips of the build (Apple Silicon and Intel) unpack with the app's own unpacker and pass the install checks
    /// (macOS: codesign).
    /// </summary>
    [ManifestDirFact]
    public async Task ScriptManifest_ZipsUnpackAndVerify()
    {
        var dir = Environment.GetEnvironmentVariable(ManifestDirFactAttribute.Variable)!;
        var json = File.ReadAllBytes(Path.Combine(dir, "update.json"));
        foreach (var platform in ReleasePlatforms)
        {
            var manifest = UpdateManifest.Parse(json, platform, out var problem);
            Assert.True(manifest is not null, $"{platform}: {problem}");
            using var temp = new TempDir();
            var app = Path.Combine(temp.Path, AppZip.BundleName);
            AppZip.Extract(Path.Combine(dir, manifest!.Download.Name), manifest.Download, app);
            if (OperatingSystem.IsMacOS())
                await new BundleInstaller(new PcCleaner.Core.ProcessCommandRunner()).VerifyAsync(app, manifest.Version, app, CancellationToken.None);
        }
    }

    /// <summary>Every release has a zip for Apple Silicon and one for Intel Macs.</summary>
    private static readonly string[] ReleasePlatforms = { "osx-arm64", "osx-x64" };

    private static string Normalize(string pem) => string.Join("\n", pem.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    private sealed class ManifestDirFactAttribute : FactAttribute
    {
        public const string Variable = "UPDATE_MANIFEST_DIR";

        public ManifestDirFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
                Skip = $"Нужна папка с update.json из tools/make-update-manifest.ps1 ({Variable}); её задают шаги CI.";
        }
    }
}
