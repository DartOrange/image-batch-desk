using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageBatchDesk;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
    private static void Reject(Action action, string name) { try { action(); } catch (Exception ex) when (Engine.IsExpected(ex)) { Check(true, name); return; } throw new Exception("Accepted: " + name); }
    private static byte[] Hash(string path) => SHA256.HashData(File.ReadAllBytes(path));
    private static BitmapSource Decode(string path)
    {
        using var stream = File.OpenRead(path);
        var source = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        source.Freeze(); return source;
    }
    private static void Create(string path, bool jpeg = false, int orientation = 1)
    {
        var pixels = new byte[8 * 4 * 4];
        for (var y = 0; y < 4; y++) for (var x = 0; x < 8; x++)
        {
            var i = (y * 8 + x) * 4; pixels[i] = (byte)(x < 4 ? 0 : 255); pixels[i + 1] = (byte)(y < 2 ? 255 : 0); pixels[i + 2] = (byte)(x < 4 ? 255 : 0); pixels[i + 3] = (byte)(jpeg ? 255 : 100);
        }
        var image = BitmapSource.Create(8, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 32);
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 100 } : new PngBitmapEncoder();
        if (jpeg)
        {
            var metadata = new BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation); metadata.Comment = "Metadata must not escape";
            encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
        }
        else encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "image-desk-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
            var output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            var other = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
            var path = Path.Combine(input, "sample.png"); Create(path);
            var original = Hash(path); var info = Engine.Inspect(path);
            var settings = new BatchSettings(32, "PNG", 88, "small");
            Check(Engine.Fit(4000, 2000, 1600) == (1600, 800) && Engine.Fit(1, 40000, 32) == (1, 32), "aspect ratio, rounding and one-pixel floor");
            Check(Engine.Fit(8, 4, 1600) == (8, 4), "small images never enlarge");
            Reject(() => Engine.Validate(settings with { MaxEdge = 0 }), "invalid size rejected");
            Reject(() => Engine.Validate(settings with { Suffix = "../oops" }), "unsafe suffix rejected");
            Reject(() => Engine.Plan([info], input, settings), "source folder rejected");
            Reject(() => Engine.Plan([info], Path.Combine(root, "absent"), settings), "missing destination rejected");
            Reject(() => Engine.Plan(Enumerable.Repeat(info, 501).ToArray(), output, settings), "batch file count limit enforced");
            var duplicate = Path.Combine(other, "sample.png"); Create(duplicate);
            File.WriteAllText(Path.Combine(output, "sample-small.png"), "keep");
            var plans = Engine.Plan([info, Engine.Inspect(duplicate)], output, settings);
            Check(Path.GetFileName(plans[0].OutputPath) == "sample-small-02.png" && Path.GetFileName(plans[1].OutputPath) == "sample-small-03.png", "existing and duplicate output names numbered deterministically");
            var results = new List<ItemResult>(); Engine.Run(plans, settings, CancellationToken.None, results.Add);
            Check(results.Count(r => r.Status == "Saved") == 2 && Hash(path).SequenceEqual(original), "real conversion preserves original bytes");
            var image = new FormatConvertedBitmap(Decode(plans[0].OutputPath), PixelFormats.Bgra32, null, 0);
            var px = new byte[8 * 4 * 4]; image.CopyPixels(px, 32, 0);
            Check(image.PixelWidth == 8 && image.PixelHeight == 4 && px[3] is >= 98 and <= 102, "PNG size and transparency retained");
            Check(File.ReadAllText(Path.Combine(output, "sample-small.png")) == "keep", "existing file unchanged");
            var collision = Engine.Plan([info], output, settings);
            File.WriteAllText(collision[0].OutputPath, "race"); results.Clear(); Engine.Run(collision, settings, CancellationToken.None, results.Add);
            Check(results.Any(r => r.Status == "Failed") && File.ReadAllText(collision[0].OutputPath) == "race", "concurrent output collision fails without overwrite");
            Check(Directory.GetFiles(output, "*.tmp").Length == 0, "temporary files removed after collision");
            using var cts = new CancellationTokenSource(); var cancelPlans = Engine.Plan([info, info], output, settings); results.Clear();
            Engine.Run(cancelPlans, settings, cts.Token, r => { results.Add(r); if (r.Status == "Processing") cts.Cancel(); });
            Check(results.Any(r => r.Status == "Cancelled") && !cancelPlans.Any(p => File.Exists(p.OutputPath)), "cancel during current item prevents current and later output");
            using var after = new CancellationTokenSource(); var afterPlans = Engine.Plan([info, info], output, settings); results.Clear();
            Engine.Run(afterPlans, settings, after.Token, r => { results.Add(r); if (r.Status == "Saved") after.Cancel(); });
            Check(File.Exists(afterPlans[0].OutputPath) && !File.Exists(afterPlans[1].OutputPath), "cancel preserves completed file and skips next");
            var jpegPlan = Engine.Plan([info], output, settings with { Format = "JPEG" }); results.Clear(); Engine.Run(jpegPlan, settings with { Format = "JPEG" }, CancellationToken.None, results.Add);
            var jpg = new FormatConvertedBitmap(Decode(jpegPlan[0].OutputPath), PixelFormats.Bgra32, null, 0); jpg.CopyPixels(px, 32, 0);
            Check(px[0] > 100 && px[3] == 255, "JPEG transparency flattened against white");
            for (var orientation = 1; orientation <= 8; orientation++)
            {
                var orientedPath = Path.Combine(input, $"orientation-{orientation}.jpg"); Create(orientedPath, true, orientation);
                var oriented = Engine.Inspect(orientedPath);
                Check((oriented.Width, oriented.Height) == (orientation >= 5 ? (4, 8) : (8, 4)), $"EXIF {orientation} upright dimensions");
                var orientedPixels = new FormatConvertedBitmap(oriented.Thumbnail, PixelFormats.Bgra32, null, 0);
                var corner = new byte[4]; orientedPixels.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), corner, 4, 0);
                var expected = orientation switch { 1 or 5 => new[] { 0, 255, 255 }, 2 or 8 => new[] { 255, 255, 0 }, 3 or 7 => new[] { 255, 0, 0 }, _ => new[] { 0, 0, 255 } };
                Check(Enumerable.Range(0, 3).All(i => Math.Abs(corner[i] - expected[i]) < 65), $"EXIF {orientation} correct top-left pixel");
                var p = Engine.Plan([oriented], output, settings with { Format = "JPEG" }); Engine.Run(p, settings with { Format = "JPEG" }, CancellationToken.None, _ => { });
                var saved = Decode(p[0].OutputPath);
                Check(saved.Metadata is not BitmapMetadata meta || !meta.ContainsQuery("/app1/ifd/{ushort=274}"), $"EXIF {orientation} stripped from saved file");
            }
            var bad = Path.Combine(input, "broken.png"); File.WriteAllText(bad, "not an image"); Reject(() => Engine.Inspect(bad), "corrupt image rejected");
            var pngBytes = File.ReadAllBytes(path); var animated = Path.Combine(input, "animated.png");
            File.WriteAllBytes(animated, pngBytes[..33].Concat(new byte[] { 0, 0, 0, 8, 97, 99, 84, 76, 0, 0, 0, 2, 0, 0, 0, 0, 243, 141, 147, 112 }).Concat(pngBytes[33..]).ToArray());
            Reject(() => Engine.Inspect(animated), "APNG animation control rejected");
            var huge = Path.Combine(input, "too-many-pixels.png");
            var hugeBitmap = BitmapSource.Create(6400, 6400, 96, 96, PixelFormats.Gray8, null, new byte[6400 * 6400], 6400);
            var hugeEncoder = new PngBitmapEncoder(); hugeEncoder.Frames.Add(BitmapFrame.Create(hugeBitmap)); using (var s = File.Create(huge)) hugeEncoder.Save(s);
            try { Engine.Inspect(huge); throw new Exception("Pixel limit not enforced"); } catch (InvalidDataException ex) { Check(ex.Message.Contains("40 million"), "decoded pixel limit enforced before pixel copy"); }
            var large = Path.Combine(input, "too-large.jpg"); using (var f = File.Create(large)) f.SetLength(Engine.MaxBytes + 1); Reject(() => Engine.Inspect(large), "oversized file rejected before decode");
            var removed = Engine.Plan([info], output, settings); File.Delete(path); results.Clear(); Engine.Run(removed, settings, CancellationToken.None, results.Add);
            Check(results.Any(r => r.Status == "Failed") && !File.Exists(removed[0].OutputPath), "source deleted after planning fails cleanly");
            Console.WriteLine($"{passed} checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
