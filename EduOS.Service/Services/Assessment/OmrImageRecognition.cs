using SkiaSharp;
using System.Text;

namespace EduOS.Service.Services.Omr;

/// <summary>
/// Registration of a printed, front-facing EduOS OMR page. This deliberately
/// rejects skewed, cropped, rotated or dark photos rather than invent answers.
/// The reviewer always sees the original scan and confirms every answer.
/// </summary>
public static class OmrImageRecognition
{
    public const int MaxPrintableQuestions = 50;
    public const int SheetWidth = 1000;
    public const int RowHeight = 36;
    public const int FirstQuestionY = 220;

    public static int SheetHeight(int questions) => FirstQuestionY + questions * RowHeight + 90;

    public static OmrRecognitionResult Read(byte[] imageBytes, int questions, int options)
    {
        if (imageBytes is not { Length: > 0 and <= 5_242_880 } ||
            questions is < 1 or > MaxPrintableQuestions || options is < 4 or > 5)
            throw new ArgumentException("Invalid OMR photo or template.");
        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data);
        if (codec == null || codec.Info.Width is < 600 or > 5000 ||
            codec.Info.Height is < 300 or > 8000 ||
            (long)codec.Info.Width * codec.Info.Height > 16_000_000)
            throw new InvalidDataException("OMR image dimensions are outside the safe range.");
        using var image = SKBitmap.Decode(codec);
        if (image == null) throw new InvalidDataException("OMR image could not be decoded.");
        var expectedHeight = SheetHeight(questions);
        if (Math.Abs((decimal)image.Width / image.Height - (decimal)SheetWidth / expectedHeight) > 0.045m)
            throw new InvalidDataException("OMR photo is cropped, rotated or not aligned with the printable template.");

        // Four printed registration squares reject page shifts and most perspective
        // distortion. No uncertain orientation is ever scored automatically.
        var fiducials = new[] { (50, 50), (950, 50), (50, expectedHeight - 50),
            (950, expectedHeight - 50) };
        foreach (var (x, y) in fiducials)
        {
            var measured = Measure(image, expectedHeight, x, y, 5);
            if (measured.SampledPixels == 0 || measured.DarkPixels < measured.SampledPixels * 7 / 10)
                throw new InvalidDataException("OMR registration marks missing; retake a straight, well-lit photo.");
        }
        // Empty paper calibration cells must stay light. A dark/underexposed scan
        // otherwise produces misleading single/ambiguous bubble confidence.
        foreach (var (x, y) in new[] { (160, 165), (850, 165), (160, 190), (850, 190) })
        {
            var light = Measure(image, expectedHeight, x, y, 7);
            if (light.SampledPixels == 0 || light.DarkPixels * 10 > light.SampledPixels)
                throw new InvalidDataException("OMR image has poor lighting or dark paper; recapture it.");
        }
        var bubbles = new List<OmrBubbleSample>(questions * options);
        for (var q = 0; q < questions; q++)
        {
            for (var o = 0; o < options; o++)
            {
                var pixels = Measure(image, expectedHeight, 320 + 110 * o,
                    FirstQuestionY + RowHeight * q, 6);
                bubbles.Add(new OmrBubbleSample
                {
                    QuestionNo = q + 1, OptionCode = (char)('A' + o),
                    DarkPixels = pixels.DarkPixels, SampledPixels = pixels.SampledPixels
                });
            }
        }
        return OmrBubbleRecognitionPolicy.Evaluate(questions, options, bubbles);
    }

    private static (int DarkPixels, int SampledPixels) Measure(SKBitmap image,
        int sheetHeight, int logicalX, int logicalY, int radius)
    {
        var cx = logicalX * image.Width / SheetWidth;
        var cy = logicalY * image.Height / sheetHeight;
        var rx = Math.Max(2, radius * image.Width / SheetWidth);
        var ry = Math.Max(2, radius * image.Height / sheetHeight);
        int dark = 0, count = 0;
        for (var y = Math.Max(0, cy - ry); y <= Math.Min(image.Height - 1, cy + ry); y++)
        for (var x = Math.Max(0, cx - rx); x <= Math.Min(image.Width - 1, cx + rx); x++)
        {
            var dx = (double)(x - cx) / rx;
            var dy = (double)(y - cy) / ry;
            if (dx * dx + dy * dy > 1) continue;
            var pixel = image.GetPixel(x, y);
            // The scan must be opaque; transparency is not white paper.
            if (pixel.Alpha < 240) throw new InvalidDataException("Transparent OMR image is not permitted.");
            if (pixel.Red + pixel.Green + pixel.Blue < 465) dark++;
            count++;
        }
        return (dark, count);
    }

    public static string PrintableSvg(Guid templateId, int questions, int options)
    {
        if (templateId == Guid.Empty || questions is < 1 or > MaxPrintableQuestions ||
            options is < 4 or > 5) throw new ArgumentException("Invalid OMR template.");
        var h = SheetHeight(questions);
        var svg = new StringBuilder(questions * options * 110 + 1300);
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 {h}\" width=\"1000\" height=\"{h}\">");
        svg.Append($"<rect x=\"0\" y=\"0\" width=\"1000\" height=\"{h}\" fill=\"white\"/>");
        foreach (var (x, y) in new[] { (50, 50), (950, 50), (50, h - 50), (950, h - 50) })
            svg.Append($"<rect x=\"{x - 12}\" y=\"{y - 12}\" width=\"24\" height=\"24\" fill=\"black\"/>");
        svg.Append("<g font-family=\"Arial,sans-serif\" fill=\"#111\" font-size=\"22\">");
        svg.Append("<text x=\"130\" y=\"75\">EduOS - OMR Answer Sheet</text>");
        svg.Append($"<text x=\"130\" y=\"108\" font-size=\"15\">Form {templateId:D}</text>");
        svg.Append("<text x=\"130\" y=\"145\" font-size=\"17\">Student roll: ____________________</text>");
        for (var i = 0; i < questions; i++)
        {
            var y = FirstQuestionY + RowHeight * i;
            svg.Append($"<text x=\"180\" y=\"{y + 7}\" font-size=\"19\">{i + 1}.</text>");
            for (var o = 0; o < options; o++)
            {
                var x = 320 + 110 * o;
                svg.Append($"<circle cx=\"{x}\" cy=\"{y}\" r=\"13\" fill=\"white\" stroke=\"black\" stroke-width=\"2\"/>");
                svg.Append($"<text x=\"{x + 20}\" y=\"{y + 6}\" font-size=\"17\">{(char)('A' + o)}</text>");
            }
        }
        svg.Append("</g></svg>");
        return svg.ToString();
    }
}
