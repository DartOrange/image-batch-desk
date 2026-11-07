using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageBatchDesk;

public sealed record ImageInfo(string Path, int Width, int Height, long Bytes, BitmapSource Thumbnail);
public sealed record BatchSettings(int MaxEdge, string Format, int Quality, string Suffix);
public sealed record PlannedImage(ImageInfo Source, string OutputPath, int Width, int Height);
public sealed record ItemResult(PlannedImage Plan, string Status, string Detail);

public static class Engine
{
    public const long MaxBytes = 80L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int MaxFiles = 500;

    public static void Validate(BatchSettings settings)
    {
        if (settings.MaxEdge is < 32 or > 8192) throw new ArgumentException("Maximum edge must be 32–8192 pixels.");
        if (settings.Format is not ("PNG" or "JPEG")) throw new ArgumentException("Choose PNG or JPEG.");
        if (settings.Quality is < 1 or > 100) throw new ArgumentException("JPEG quality must be 1–100.");
        if (settings.Suffix.Length is < 1 or > 32 || settings.Suffix.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Suffix: use 1–32 English letters, digits, hyphens or underscores.");
    }

    private static BitmapSource Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new IOException("File is no longer available.");
        if (file.Length > MaxBytes) throw new InvalidDataException("File exceeds the 80 MiB limit.");
        var ext = file.Extension.ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg")) throw new InvalidDataException("Only PNG and JPEG files are supported.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        RejectAnimatedPng(stream);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        if (decoder is not PngBitmapDecoder && decoder is not JpegBitmapDecoder) throw new InvalidDataException("File content is not PNG or JPEG.");
        if (decoder.Frames.Count != 1) throw new InvalidDataException("Multi-frame images are not supported.");
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels) throw new InvalidDataException("Image exceeds 40 million pixels.");
        // Force pixel decoding while the source is open; no file handle escapes this method.
        var stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
        var pixels = new byte[checked(stride * frame.PixelHeight)];
        frame.CopyPixels(pixels, stride, 0);
        BitmapSource source = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, frame.Format, frame.Palette, pixels, stride);
        var orientation = 1;
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/app1/ifd/{ushort=274}"))
                orientation = Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}"));
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        var transforms = new TransformGroup();
        // EXIF orientation is applied before sizing; saved output contains upright pixels.
        switch (orientation)
        {
            case 2: transforms.Children.Add(new ScaleTransform(-1, 1)); break;
            case 3: transforms.Children.Add(new RotateTransform(180)); break;
            case 4: transforms.Children.Add(new ScaleTransform(1, -1)); break;
            case 5: transforms.Children.Add(new ScaleTransform(-1, 1)); transforms.Children.Add(new RotateTransform(270)); break;
            case 6: transforms.Children.Add(new RotateTransform(90)); break;
            case 7: transforms.Children.Add(new ScaleTransform(-1, 1)); transforms.Children.Add(new RotateTransform(90)); break;
            case 8: transforms.Children.Add(new RotateTransform(270)); break;
        }
        if (transforms.Children.Count > 0) source = new TransformedBitmap(source, transforms);
        source.Freeze();
        return source;
    }

    private static void RejectAnimatedPng(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        if (stream.Read(header) == 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            while (stream.Position + 12 <= stream.Length)
            {
                stream.ReadExactly(header);
                var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
                if (header[4..].SequenceEqual("acTL"u8)) throw new InvalidDataException("Animated PNG files are not supported.");
                if (size > stream.Length - stream.Position - 4) throw new InvalidDataException("PNG chunk data is damaged.");
                stream.Seek(size + 4L, SeekOrigin.Current);
            }
        }
        stream.Position = 0;
    }

    public static (int Width, int Height) Fit(int width, int height, int edge)
    {
        var scale = Math.Min(1d, edge / (double)Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static BitmapSource Render(BitmapSource source, int width, int height, bool white)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            if (white) context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static ImageInfo Inspect(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var source = Load(path);
        var size = Fit(source.PixelWidth, source.PixelHeight, 360);
        return new(path, source.PixelWidth, source.PixelHeight, new FileInfo(path).Length, Render(source, size.Width, size.Height, false));
    }

    public static IReadOnlyList<PlannedImage> Plan(IReadOnlyList<ImageInfo> images, string destination, BatchSettings settings)
    {
        Validate(settings);
        if (images.Count is < 1 or > MaxFiles) throw new ArgumentException("Choose 1–500 images.");
        var folder = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(destination));
        if (!Directory.Exists(folder)) throw new ArgumentException("Choose an existing destination folder.");
        if (images.Any(i => string.Equals(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetDirectoryName(i.Path)!), folder, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a destination separate from all source folders.");
        var entries = Directory.EnumerateFileSystemEntries(folder).Take(10001).ToArray();
        if (entries.Length > 10000) throw new ArgumentException("Destination contains more than 10,000 entries. Choose a smaller or empty folder.");
        var used = new HashSet<string>(entries.Select(System.IO.Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var result = new List<PlannedImage>();
        foreach (var image in images)
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(image.Path);
            if (stem.Length > 100) stem = stem[..100];
            var baseName = stem + "-" + settings.Suffix;
            var ext = settings.Format == "PNG" ? ".png" : ".jpg";
            var name = baseName + ext;
            for (var i = 2; !used.Add(name); i++) name = $"{baseName}-{i:00}{ext}";
            var size = Fit(image.Width, image.Height, settings.MaxEdge);
            result.Add(new(image, System.IO.Path.Combine(folder, name), size.Width, size.Height));
        }
        return result;
    }

    public static void Run(IReadOnlyList<PlannedImage> plans, BatchSettings settings, CancellationToken token, Action<ItemResult> report)
    {
        Validate(settings);
        foreach (var plan in plans)
        {
            if (token.IsCancellationRequested) break;
            string? temporary = null;
            try
            {
                report(new(plan, "Processing", "Decoding and resizing…"));
                var source = Load(plan.Source.Path);
                if (source.PixelWidth != plan.Source.Width || source.PixelHeight != plan.Source.Height)
                    throw new IOException("Source dimensions changed. Remove it and add it again.");
                token.ThrowIfCancellationRequested();
                var bitmap = Render(source, plan.Width, plan.Height, settings.Format == "JPEG");
                token.ThrowIfCancellationRequested();
                BitmapEncoder encoder = settings.Format == "PNG" ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = settings.Quality };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(plan.OutputPath)!, ".image-desk-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(stream);
                    stream.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, plan.OutputPath, false);
                temporary = null;
                report(new(plan, "Saved", $"{plan.Width} × {plan.Height} · {new FileInfo(plan.OutputPath).Length / 1024d:0.#} KB"));
            }
            catch (OperationCanceledException) { report(new(plan, "Cancelled", "No output saved for this image.")); break; }
            catch (Exception ex) when (IsExpected(ex)) { report(new(plan, "Failed", Explain(ex))); }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { report(new(plan, "Failed", "Temporary file could not be removed: " + temporary)); }
                    catch (UnauthorizedAccessException) { report(new(plan, "Failed", "Temporary file could not be removed: " + temporary)); }
                }
            }
        }
    }

    public static bool IsExpected(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or InvalidOperationException or OverflowException or System.Security.SecurityException;
    public static string Explain(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access denied. Choose a writable folder or readable image.",
        FileFormatException => "Image data is damaged or unsupported.",
        System.Runtime.InteropServices.COMException => "Image could not be decoded or encoded. It may be damaged or unsupported.",
        _ => ex.Message
    };

    public static Task<T> OnWorker<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
