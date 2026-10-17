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
