using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ImageBatchDesk;

// In-process integration diagnostics. Does not inject operating-system input.
internal static class Diagnostics
{
    public static async Task<int> Run(string directory)
    {
        var checks = new List<string>();
        MainWindow? window = null;
        var created = false;
        try
        {
            directory = Path.GetFullPath(directory);
            if (Directory.Exists(directory)) throw new ArgumentException("Self-test destination must not already exist.");
            Directory.CreateDirectory(directory);
            created = true;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Failed: " + name); checks.Add(name); }
            var output = Directory.CreateDirectory(Path.Combine(directory, "output")).FullName;
            var samples = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "samples")).Where(p => p.EndsWith(".jpg") || p.EndsWith(".png")).Order().ToArray();
            var hashes = samples.ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
            window = new MainWindow();
            window.Show();
            await Capture(window, directory, "01-empty");
            await window.AddPaths(samples);
            Check(window.Rows.Count == 4, "Bundled samples imported");
            window.SetDestination(output);
            window.EdgeBox.Text = "1200";
            await Capture(window, directory, "02-ready");
            Check(window.StartButton.IsEnabled && window.Rows.All(r => r.Output.EndsWith(".jpg")), "Output plan visible");
            await window.ProcessBatch();
            Check(window.Rows.All(r => r.Status == "Saved"), "All four images converted by window workflow");
            Check(Directory.GetFiles(output).Length == 4, "Four final outputs");
            foreach (var file in Directory.GetFiles(output))
            {
                var info = await Engine.OnWorker(() => Engine.Inspect(file));
                Check(Math.Max(info.Width, info.Height) <= 1200, "Output dimensions: " + Path.GetFileName(file));
            }
            Check(samples.All(p => hashes[p].SequenceEqual(SHA256.HashData(File.ReadAllBytes(p)))), "All original sample bytes unchanged");
            await Capture(window, directory, "03-complete");
            await window.ProcessBatch();
            Check(Directory.GetFiles(output).Length == 8, "Second run creates numbered copies");
            window.EdgeBox.Text = "wrong";
            Check(!window.StartButton.IsEnabled && window.StatusDetail.Text.Contains("whole number"), "Invalid settings block execution with explanation");
            await Capture(window, directory, "04-invalid-settings");
            window.EdgeBox.Text = "1200";
            var corrupt = Path.Combine(directory, "broken.png"); File.WriteAllText(corrupt, "invalid PNG fixture");
            await window.AddPaths([corrupt]);
            Check(window.Rows.Count == 4 && window.StatusText.Text.Contains("rejected"), "Corrupt image rejection preserves queue");
            await Capture(window, directory, "05-rejected");
            window.SetDestination(Path.GetDirectoryName(samples[0])!);
            Check(!window.StartButton.IsEnabled, "Original folder blocked");
            await Capture(window, directory, "06-protected-folder");
            window.SetDestination(output);
            window.FormatBox.SelectedIndex = 1;
            window.QualityBox.Text = "";
            Check(window.StartButton.IsEnabled, "PNG ignores inactive JPEG quality");
            await window.ProcessBatch();
            Check(window.Rows.All(r => r.Status == "Saved"), "PNG workflow completes");
            window.Width = 1020; window.Height = 740;
            await Capture(window, directory, "07-minimum-window");
            // A timer executes on the live dispatcher while codecs run on the worker thread.
            window.Width = 1260; window.Height = 900;
            var longPaths = Enumerable.Range(0, 60).Select(i =>
            {
                var name = Path.Combine(directory, $"cancel-{i:00}.png");
                File.Copy(samples.First(p => p.EndsWith("sunmark.png")), name); return name;
            }).ToArray();
            window.ClearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.AddPaths(longPaths);
            var cancelledOutput = Directory.CreateDirectory(Path.Combine(directory, "cancel-output")).FullName;
            window.SetDestination(cancelledOutput);
            window.EdgeBox.Text = "1800";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => { timer.Stop(); window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            timer.Start();
            await window.ProcessBatch();
            timer.Stop();
            Check(window.Rows.Any(r => r.Status == "Cancelled"), "Dispatcher cancellation interrupts batch");
            Check(window.Rows.All(r => r.Status is "Saved" or "Cancelled"), "Every cancelled-batch row has a terminal state");
            Check(!Directory.GetFiles(cancelledOutput, "*.tmp").Any(), "No cancellation temporary files remain");
            Check(Directory.GetFiles(cancelledOutput).Length == window.Rows.Count(r => r.Status == "Saved"), "Completed copies match cancellation summary");
            await Capture(window, directory, "08-cancelled");
            window.ClearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.AddPaths(samples);
            window.SetDestination(output);
            var deletedFolder = Directory.CreateDirectory(Path.Combine(directory, "removed-destination")).FullName;
            window.SetDestination(deletedFolder);
            Directory.Delete(deletedFolder);
            await window.ProcessBatch();
            Check(!window.StartButton.IsEnabled && window.StatusDetail.Text.Contains("existing destination"), "Destination removed after planning is recoverable");
            window.SetDestination(output);
            Check(window.StartButton.IsEnabled, "Valid destination restores execution");
            await Capture(window, directory, "09-recovered");
            window.ClearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var changed = Path.Combine(directory, "changed-source.png");
            File.Copy(samples.First(p => p.EndsWith("sunmark.png")), changed);
            await window.AddPaths([changed, samples[0]]);
            File.WriteAllText(changed, "Source became corrupt after import.");
            await window.ProcessBatch();
            Check(window.Rows[0].Status == "Failed" && window.Rows[1].Status == "Saved", "A changed corrupt source fails without blocking the next image");
            window.Queue.SelectedIndex = 0;
            await Capture(window, directory, "10-item-failed");
            window.EdgeBox.Text = "1400";
            Check(window.Rows.All(r => r.Status == "Ready"), "Replanning clears prior result statuses");
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks, runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), executable = Environment.ProcessPath, inputMethod = "in-process WPF controls; no OS input or native file-dialog automation" }, new JsonSerializerOptions { WriteIndented = true }));
            window.Close(); return 0;
        }
        catch (Exception ex)
        {
            if (created) File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
            window?.Close(); return 1;
        }
    }

    private static async Task Capture(MainWindow window, string folder, string name)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right), (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(stream);
    }
}
