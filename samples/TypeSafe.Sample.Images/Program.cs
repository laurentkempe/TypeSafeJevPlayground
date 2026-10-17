using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using TypeSafe;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: TypeSafe.Sample.Images <input-image> <output-image>");
    return 2;
}

var inputPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
IImageEncoder? outputEncoder = Path.GetExtension(outputPath).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => new JpegEncoder(),
    ".png" => new PngEncoder(),
    _ => null
};
if (outputEncoder is null)
{
    Console.Error.WriteLine("Output image must use a .png, .jpg, or .jpeg extension.");
    return 2;
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input image not found: {inputPath}");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var imageBytes = await File.ReadAllBytesAsync(inputPath, cancellation.Token);
    var photo = new TypeSafe.Image(imageBytes);
    using var client = new TypeSafeClient(new TypeSafeClientOptions
    {
        DefaultModel = "jev-preview",
        DefaultHeaders = new Dictionary<string, string>
        {
            ["TypeSafe-Danger-Zone-Images"] = "true"
        },
        Timeout = TimeSpan.FromSeconds(60)
    });

    var questions = new Dictionary<string, Question>
    {
        ["pig"] = Questions.Box(TextWithAssets.Create($"Locate each pig visible in {photo}."))
    };

    var result = await client.SystemOneAsync(new SystemOneRequest
    {
        State = photo,
        Model = "jev-preview",
        Questions = questions
    }, cancellationToken: cancellation.Token);

    Console.WriteLine($"Model: {result.Model}");
    foreach (var (name, answer) in result.Answers)
    {
        if (answer is not BoxAnswer boxAnswer)
            continue;

        var box = boxAnswer.Box;
        Console.WriteLine($"{name}: top={box.Top:0.####}, bottom={box.Bottom:0.####}, left={box.Left:0.####}, right={box.Right:0.####}, confidence={boxAnswer.Confidence?.ToString("P1") ?? "unknown"}");
    }

    using var renderedImage = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(inputPath, cancellation.Token);
    foreach (var answer in result.Answers.Values)
    {
        if (answer is not BoxAnswer boxAnswer)
            continue;

        DrawBox(renderedImage, boxAnswer.Box);
    }

    await using var output = File.Create(outputPath);
    await renderedImage.SaveAsync(output, outputEncoder, cancellation.Token);
    Console.WriteLine($"Image with boxes saved to: {outputPath}");
    return 0;
}

catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation cancelled.");
    return 130;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Image evaluation failed: {error.Message}");
    return 1;
}

static void DrawBox(Image<Rgba32> image, BoxCoordinates box)
{
    var left = Math.Clamp((int)Math.Round(box.Left * (image.Width - 1)), 0, image.Width - 1);
    var right = Math.Clamp((int)Math.Round(box.Right * (image.Width - 1)), 0, image.Width - 1);
    var top = Math.Clamp((int)Math.Round(box.Top * (image.Height - 1)), 0, image.Height - 1);
    var bottom = Math.Clamp((int)Math.Round(box.Bottom * (image.Height - 1)), 0, image.Height - 1);
    var red = new Rgba32(255, 0, 0, 255);

    for (var thickness = 0; thickness < 4; thickness++)
    {
        var x1 = Math.Min(left + thickness, right);
        var x2 = Math.Max(right - thickness, left);
        var y1 = Math.Min(top + thickness, bottom);
        var y2 = Math.Max(bottom - thickness, top);
        for (var x = x1; x <= x2; x++)
        {
            image[x, y1] = red;
            image[x, y2] = red;
        }
        for (var y = y1; y <= y2; y++)
        {
            image[x1, y] = red;
            image[x2, y] = red;
        }
    }
}