namespace MarkItDown.Converters.Pdf;

internal abstract record PdfContentBlock(double Y, double Top, double Bottom);

internal sealed record PdfTextBlock(
    double Y, double Top, double Bottom,
    string Text,
    double FontSize) : PdfContentBlock(Y, Top, Bottom);

internal sealed record PdfImageBlock(
    double Y, double Top, double Bottom,
    int PageNumber,
    int ImageIndex,
    string FileName) : PdfContentBlock(Y, Top, Bottom);
