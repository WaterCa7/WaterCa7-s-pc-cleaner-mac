using System.Buffers.Binary;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Programs;
using Xunit;

namespace PcCleaner.Mac.Tests.Programs;

public class MachOTests
{
    internal const int Arm64 = 0x0100000C;
    internal const int X86_64 = 0x01000007;

    /// <summary>A thin 64-bit header as it lies on disk (little-endian).</summary>
    internal static byte[] Thin(int cpuType)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xFEEDFACF);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), cpuType);
        return bytes;
    }

    /// <summary>A universal header (big-endian), 32- or 64-bit entries.</summary>
    internal static byte[] Fat(bool wide, params int[] cpuTypes)
    {
        var entry = wide ? 32 : 20;
        var bytes = new byte[8 + entry * cpuTypes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, wide ? 0xCAFEBABF : 0xCAFEBABE);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)cpuTypes.Length);
        for (var i = 0; i < cpuTypes.Length; i++)
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8 + i * entry), cpuTypes[i]);
        return bytes;
    }

    [Fact]
    public void Thin_And_Fat()
    {
        Assert.Equal(MachOArchs.Arm64, MachO.Read(Thin(Arm64)));
        Assert.Equal(MachOArchs.Intel, MachO.Read(Thin(X86_64)));
        Assert.Equal(MachOArchs.Arm64 | MachOArchs.Intel, MachO.Read(Fat(false, X86_64, Arm64)));
        Assert.Equal(MachOArchs.Arm64 | MachOArchs.Intel, MachO.Read(Fat(true, Arm64, X86_64)));
        Assert.Equal(MachOArchs.Intel | MachOArchs.Other, MachO.Read(Fat(false, 7, 18)));
        Assert.Equal(MachOArchs.Other, MachO.Read(Thin(12))); // 32-bit ARM
    }

    [Fact]
    public void NotMachO()
    {
        // A Java class file starts with the same magic; its version (here 52, Java 8) is far more than any fat count.
        Assert.Equal(MachOArchs.None, MachO.Read(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x34, 0, 0, 0, 0 }));
        Assert.Equal(MachOArchs.None, MachO.Read("#!/bin/sh\nexec x\n"u8));
        Assert.Equal(MachOArchs.None, MachO.Read(new byte[] { 0xCF, 0xFA }));
        Assert.Equal(MachOArchs.None, MachO.Read(Fat(false)));
    }

    [Theory]
    [InlineData(MachOArchs.Arm64, AppArchitecture.AppleSilicon)]
    [InlineData(MachOArchs.Arm64 | MachOArchs.Intel, AppArchitecture.Universal)]
    [InlineData(MachOArchs.Intel, AppArchitecture.IntelOnly)]
    [InlineData(MachOArchs.Intel | MachOArchs.Other, AppArchitecture.IntelOnly)]
    [InlineData(MachOArchs.None, AppArchitecture.Unknown)]
    [InlineData(MachOArchs.Other, AppArchitecture.Unknown)]
    public void Architecture(MachOArchs archs, AppArchitecture expected) => Assert.Equal(expected, MachO.Architecture(archs));

    [Fact]
    public void ReadFile_MissingOrShort()
    {
        using var dir = new TempDir();
        Assert.Equal(MachOArchs.None, MachO.ReadFile(Path.Combine(dir.Path, "нет")));
        var path = Path.Combine(dir.Path, "exe");
        File.WriteAllBytes(path, Fat(false, X86_64, Arm64));
        Assert.Equal(MachOArchs.Arm64 | MachOArchs.Intel, MachO.ReadFile(path));
    }

    /// <summary>The parser agrees with `lipo -archs` on every app of this Mac.</summary>
    [MacFact]
    public async Task RealMac_AgreesWithLipo()
    {
        var runner = new ProcessCommandRunner();
        var checkedApps = 0;
        foreach (var app in Directory.EnumerateDirectories("/System/Applications", "*.app").Concat(Directory.EnumerateDirectories("/Applications", "*.app")))
        {
            var plist = await PlistFile.ReadAsync(Path.Combine(app, "Contents", "Info.plist"), runner, CancellationToken.None);
            if (plist.Value?.GetString("CFBundleExecutable") is not { } exe)
                continue;
            var path = Path.Combine(app, "Contents", "MacOS", exe);
            var lipo = await runner.RunAsync("/usr/bin/lipo", new[] { "-archs", path });
            if (!lipo.Ok)
                continue;
            var names = lipo.StdOut.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var archs = MachO.ReadFile(path);
            Assert.True(names.Any(n => n.StartsWith("arm64", StringComparison.Ordinal)) == archs.HasFlag(MachOArchs.Arm64), $"{path}: {lipo.StdOut}");
            Assert.True(names.Any(n => n is "x86_64" or "x86_64h" or "i386") == archs.HasFlag(MachOArchs.Intel), $"{path}: {lipo.StdOut}");
            checkedApps++;
        }
        Assert.True(checkedApps > 10, $"проверено программ: {checkedApps}");
    }
}
