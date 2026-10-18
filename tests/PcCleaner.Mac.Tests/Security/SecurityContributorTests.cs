using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Security;
using Xunit;

namespace PcCleaner.Mac.Tests.Security;

public class SecurityContributorTests
{
    [Fact]
    public async Task CiMac_Findings()
    {
        using var mac = new SecurityMac();
        mac.Fake.OnFixture("/usr/sbin/softwareupdate", new[] { "-l" }, "security/softwareupdate-26.txt");
        using var dir = new TempDir();
        var fixes = new SecurityFixes(new AdminPrompt(mac.Fake), new BackupStore(dir.Path), dir.Path);
        var issues = await new SecurityContributor(mac.Service, fixes).AnalyzeAsync(CancellationToken.None);
        var bySeverity = issues.ToLookup(i => i.Severity, i => i.Title);

        Assert.Equal(new[] { "Защита целостности системы (SIP): выключена" }, bySeverity[IssueSeverity.Critical]);
        Assert.Equal(new[]
            {
                "FileVault (шифрование диска): выключен",
                "Автоматические обновления: выключены: установка ответов безопасности и системных файлов",
                "Вход без пароля: включён", "Обновления macOS: есть обновления безопасности",
            },
            bySeverity[IssueSeverity.High].OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal(new[] { "Брандмауэр: выключен", "Удалённый вход (SSH): включён" }, bySeverity[IssueSeverity.Medium].OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal(new[] { "Режим невидимости: выключен" }, bySeverity[IssueSeverity.Info]);
        Assert.All(issues, i =>
        {
            Assert.False(i.AutoFixSafe);
            Assert.Equal(("Безопасность и сеть", "Security"), (i.Module, i.TargetPageKey));
        });
        Assert.NotNull(issues.Single(i => i.Title.StartsWith("Брандмауэр", StringComparison.Ordinal)).FixAsync);
        Assert.Null(issues.Single(i => i.Title.StartsWith("FileVault", StringComparison.Ordinal)).FixAsync);
    }

    [Fact]
    public async Task Infected_HostsAreFixedOnThePage_ProxyFromTheDashboardToo()
    {
        using var mac = new SecurityMac().Infected();
        using var dir = new TempDir();
        var fixes = new SecurityFixes(new AdminPrompt(mac.Fake), new BackupStore(dir.Path), dir.Path);
        var issues = await new SecurityContributor(mac.Service, fixes).AnalyzeAsync(CancellationToken.None);
        Assert.Null(issues.Single(i => i.Title.StartsWith("Файл hosts", StringComparison.Ordinal)).FixAsync);
        Assert.NotNull(issues.Single(i => i.Title.StartsWith("Прокси", StringComparison.Ordinal)).FixAsync);
        Assert.Contains(issues, i => i.Title == "Профиль «Search Settings»: прокси, настройки браузера" && i.Severity == IssueSeverity.High);
        Assert.Contains(issues, i => i.Title == "Wi-Fi: открытая сеть без пароля" && i.Severity == IssueSeverity.Medium);
        // The update list did not answer (no fixture): no finding about it, and nothing failed.
        Assert.DoesNotContain(issues, i => i.Title.StartsWith("Обновления macOS", StringComparison.Ordinal));
    }
}
