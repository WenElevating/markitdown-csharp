using MarkItDown.Core;
using MarkItDown.Converters.Office;

namespace MarkItDown.Converters.Office.Tests;

public sealed class MsgConverterTests
{
    private readonly MsgConverter _converter = new();

    [Fact]
    public void CanConvert_AcceptsMsgExtension()
    {
        Assert.True(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "email.msg" }));
    }

    [Fact]
    public void CanConvert_RejectsNonMsgExtension()
    {
        Assert.False(_converter.CanConvert(
            new DocumentConversionRequest { FilePath = "email.txt" }));
    }

    [Fact]
    public async Task ConvertAsync_ThrowsForNonExistentFile()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _converter.ConvertAsync(
                new DocumentConversionRequest { FilePath = "nonexistent.msg" }));
    }
}
