using EduOS.Service.Services.Omr;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class OmrImageRecognitionTests
{
    private static Image<Rgba32> Form(int questions)
    {
        var height = OmrImageRecognition.SheetHeight(questions);
        var image = new Image<Rgba32>(OmrImageRecognition.SheetWidth, height,
            new Rgba32(255, 255, 255));
        foreach (var (x, y) in new[] { (50, 50), (950, 50), (50, height - 50), (950, height - 50) })
            Fill(image, x, y, 9);
        return image;
    }

    private static void Fill(Image<Rgba32> image, int cx, int cy, int radius)
    {
        for (var y = cy - radius; y <= cy + radius; y++)
        for (var x = cx - radius; x <= cx + radius; x++)
        {
            if (x >= 0 && y >= 0 && x < image.Width && y < image.Height &&
                (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                image[x, y] = new Rgba32(0, 0, 0);
        }
    }

    private static byte[] Png(Image<Rgba32> image)
    {
        using var memory = new MemoryStream();
        image.SaveAsPng(memory);
        return memory.ToArray();
    }

    [Fact]
    public void Aligned_single_choice_is_suggested_but_never_auto_approved()
    {
        using var image = Form(2);
        Fill(image, 320, OmrImageRecognition.FirstQuestionY, 9);
        var result = OmrImageRecognition.Read(Png(image), 2, 4);
        Assert.True(result.RequiresIndependentReview);
        Assert.Equal(OmrSuggestionState.Single, result.Questions[0].State);
        Assert.Equal('A', result.Questions[0].SuggestedOption);
        Assert.Equal(OmrSuggestionState.Blank, result.Questions[1].State);
        Assert.Equal(1, result.UncertainCount);
    }

    [Fact]
    public void Multiple_filled_bubbles_are_flagged_for_human_review()
    {
        using var image = Form(1);
        Fill(image, 320, OmrImageRecognition.FirstQuestionY, 9);
        Fill(image, 430, OmrImageRecognition.FirstQuestionY, 9);
        var result = OmrImageRecognition.Read(Png(image), 1, 4);
        Assert.Equal(OmrSuggestionState.Ambiguous, result.Questions[0].State);
        Assert.Null(result.Questions[0].SuggestedOption);
    }

    [Fact]
    public void Missing_registration_mark_is_rejected()
    {
        using var image = Form(1);
        for (var y = 37; y <= 63; y++)
        for (var x = 37; x <= 63; x++) image[x, y] = new Rgba32(255, 255, 255);
        Assert.Throws<InvalidDataException>(() => OmrImageRecognition.Read(Png(image), 1, 4));
    }

    [Fact]
    public void Poor_lighting_is_rejected_even_when_registration_marks_are_visible()
    {
        using var image = Form(1);
        Fill(image, 160, 165, 9);
        Assert.Throws<InvalidDataException>(() => OmrImageRecognition.Read(Png(image), 1, 4));
    }

    [Fact]
    public void Wrong_form_aspect_ratio_is_rejected()
    {
        using var image = Form(1);
        Assert.Throws<InvalidDataException>(() => OmrImageRecognition.Read(Png(image), 20, 4));
    }
}
