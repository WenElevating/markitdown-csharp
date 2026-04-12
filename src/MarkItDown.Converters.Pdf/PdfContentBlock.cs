namespace MarkItDown.Converters.Pdf;

internal abstract record PdfContentBlock(
    double Y, double Top, double Bottom,
    double Left, double Right,
    bool IsHeaderFooter = false);

internal sealed record PdfTextBlock(
    double Y, double Top, double Bottom,
    double Left, double Right,
    string Text,
    double FontSize,
    bool IsHeaderFooter = false,
    bool IsBold = false) : PdfContentBlock(Y, Top, Bottom, Left, Right, IsHeaderFooter);

internal sealed record PdfImageBlock(
    double Y, double Top, double Bottom,
    double Left, double Right,
    int PageNumber,
    int ImageIndex,
    string FileName,
    bool IsHeaderFooter = false) : PdfContentBlock(Y, Top, Bottom, Left, Right, IsHeaderFooter);
