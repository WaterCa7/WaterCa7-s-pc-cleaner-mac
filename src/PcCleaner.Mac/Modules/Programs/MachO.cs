using System.Buffers.Binary;

namespace PcCleaner.Modules.Programs;

[Flags]
public enum MachOArchs
{
    None = 0,
    Arm64 = 1,

    /// <summary>x86_64 or i386.</summary>
    Intel = 2,
    Other = 4,
}

/// <summary>Which processors a program runs on natively.</summary>
public enum AppArchitecture
{
    Unknown,
    AppleSilicon,
    Universal,

    /// <summary>Runs on Apple Silicon only through Rosetta.</summary>
    IntelOnly,
}

/// <summary>
/// The processors in a Mach-O file, from its header alone: thin 32/64-bit files and fat (universal) files. Anything else
/// (scripts, Java class files, which also start with 0xCAFEBABE) is <see cref="MachOArchs.None"/>.
/// </summary>
public static class MachO
{
    /// <summary>Enough bytes for the header of any real universal file.</summary>
    public const int HeaderBytes = 4096;

    private const uint MagicThin32 = 0xFEEDFACE;
    private const uint MagicThin64 = 0xFEEDFACF;
    private const uint MagicFat = 0xCAFEBABE;
    private const uint MagicFat64 = 0xCAFEBABF;
    private const int MaxFatArchs = 30;

    private const int CpuArch64 = 0x01000000;
    private const int CpuTypeX86 = 7;
    private const int CpuTypeArm = 12;

    public static MachOArchs Read(ReadOnlySpan<byte> header)
    {
        if (header.Length < 8)
            return MachOArchs.None;

        var little = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (little is MagicThin32 or MagicThin64)
            return Arch(BinaryPrimitives.ReadInt32LittleEndian(header[4..]));
        var big = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (big is MagicThin32 or MagicThin64)
            return Arch(BinaryPrimitives.ReadInt32BigEndian(header[4..]));
        if (big is not (MagicFat or MagicFat64))
            return MachOArchs.None;

        var count = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        if (count is 0 or > MaxFatArchs)
            return MachOArchs.None;
        var entrySize = big == MagicFat64 ? 32 : 20;
        var result = MachOArchs.None;
        for (var i = 0; i < count; i++)
        {
            var at = 8 + i * entrySize;
            if (at + 4 > header.Length)
                break;
            result |= Arch(BinaryPrimitives.ReadInt32BigEndian(header[at..]));
        }
        return result;
    }

    /// <summary>Reads the first bytes of the file; a file that cannot be read is <see cref="MachOArchs.None"/>.</summary>
    public static MachOArchs ReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> buffer = stackalloc byte[HeaderBytes];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return Read(buffer[..read]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MachOArchs.None;
        }
    }

    public static AppArchitecture Architecture(MachOArchs archs) => archs switch
    {
        _ when archs.HasFlag(MachOArchs.Arm64) && archs.HasFlag(MachOArchs.Intel) => AppArchitecture.Universal,
        _ when archs.HasFlag(MachOArchs.Arm64) => AppArchitecture.AppleSilicon,
        _ when archs.HasFlag(MachOArchs.Intel) => AppArchitecture.IntelOnly,
        _ => AppArchitecture.Unknown,
    };

    private static MachOArchs Arch(int cpuType) => (cpuType & ~CpuArch64) switch
    {
        CpuTypeArm when (cpuType & CpuArch64) != 0 => MachOArchs.Arm64,
        CpuTypeX86 => MachOArchs.Intel,
        _ => MachOArchs.Other,
    };
}
