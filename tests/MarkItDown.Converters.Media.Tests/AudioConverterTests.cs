using MarkItDown.Core;
using MarkItDown.Converters.Media;

namespace MarkItDown.Converters.Media.Tests;

public sealed class AudioConverterTests
{
    private readonly AudioConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsMp3Extension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.mp3" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_AcceptsWavExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.wav" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public void CanConvert_AcceptsM4aExtension()
    {
        var request = new DocumentConversionRequest { FilePath = "test.m4a" };
        Assert.True(_converter.CanConvert(request));
    }

    [Fact]
    public async Task ConvertAsync_ExtractsMetadata()
    {
        var path = CreateTestMp3();
        try
        {
            var result = await _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = path });

            Assert.Equal("Audio", result.Kind);
            Assert.Contains("Duration:", result.Markdown);
            Assert.Contains("MediaTypes:", result.Markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestMp3()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp3");
        // MPEG Audio Layer 3, 128kbps, 44100Hz, Joint Stereo
        // Frame sync: 0xFF 0xFB, bitrate index 128kbps: 0x90, padding/privacy: 0x00
        var header = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
        var frameSize = 417; // 128kbps, 44100Hz frame size
        var frame = new byte[frameSize];
        header.CopyTo(frame, 0);

        // Write multiple frames to give TagLib enough data to parse
        var data = new byte[frameSize * 10];
        for (var i = 0; i < 10; i++)
        {
            Array.Copy(frame, 0, data, i * frameSize, frameSize);
        }

        File.WriteAllBytes(path, data);
        return path;
    }
}
