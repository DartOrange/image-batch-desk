using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace ImageBatchDesk;

public sealed class ImageRow : INotifyPropertyChanged
{
    public required ImageInfo Info { get; init; }
    public string Name => Path.GetFileName(Info.Path);
    public string Dimensions => $"{Info.Width} × {Info.Height} · {Info.Bytes / 1024d:0.#} KB";
    private string output = "Choose destination", status = "Ready", detail = "Ready to process.";
    public string Output { get => output; set { output = value; Changed(nameof(Output)); } }
    public string Status { get => status; set { status = value; Changed(nameof(Status)); } }
    public string Detail { get => detail; set { detail = value; Changed(nameof(Detail)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
}

public partial class MainWindow : Window
{
    public ObservableCollection<ImageRow> Rows { get; } = [];
    private string? destination;
    private bool busy, importing, initialized;
    private CancellationTokenSource? cancellation;
    private IReadOnlyList<PlannedImage>? plans;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        initialized = true;
        UpdatePlan();
    }

    private BatchSettings ReadSettings()
    {
        if (!int.TryParse(EdgeBox.Text, out var edge)) throw new ArgumentException("Maximum edge must be a whole number (32–8192).");
        var format = ((ComboBoxItem)FormatBox.SelectedItem).Content.ToString()!;
        var quality = 88;
        if (format == "JPEG" && !int.TryParse(QualityBox.Text, out quality)) throw new ArgumentException("JPEG quality must be a whole number (1–100).");
        var settings = new BatchSettings(edge, format, format == "PNG" ? 88 : quality, SuffixBox.Text);
        Engine.Validate(settings);
        return settings;
    }

    private void UpdatePlan()
    {
        if (!initialized || busy || importing) return;
        plans = null;
        StartButton.IsEnabled = false;
        QueueCount.Text = $"{Rows.Count} image{(Rows.Count == 1 ? "" : "s")}";
        QualityBox.IsEnabled = FormatBox.SelectedIndex == 0;
        foreach (var row in Rows)
        {
            row.Output = destination == null ? "Choose destination" : "Check settings";
            row.Status = "Ready";
            row.Detail = "Ready to process.";
        }
        try
        {
            var settings = ReadSettings();
            if (Rows.Count == 0 || destination == null) return;
            plans = Engine.Plan(Rows.Select(r => r.Info).ToArray(), destination, settings);
            for (var i = 0; i < Rows.Count; i++) Rows[i].Output = Path.GetFileName(plans[i].OutputPath);
            StartButton.IsEnabled = true;
            StatusText.Text = "Your batch is ready.";
            StatusDetail.Text = $"{Rows.Count} images · maximum {settings.MaxEdge} px · {settings.Format} · no overwrites";
        }
        catch (Exception ex) when (Engine.IsExpected(ex)) { StatusText.Text = "Check your batch settings."; StatusDetail.Text = Engine.Explain(ex); }
    }

    private void SettingsChanged(object sender, RoutedEventArgs e) => UpdatePlan();

    private async void AddImages(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Add PNG or JPEG images", Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await AddPaths(dialog.FileNames);
    }

    private async void LoadSamples(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "samples");
        if (!Directory.Exists(folder)) { StatusText.Text = "Samples are not available."; StatusDetail.Text = "Use the packaged demo, or add your own PNG / JPEG files."; return; }
        await AddPaths(Directory.GetFiles(folder).Where(p => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(p).ToLowerInvariant())).Order().ToArray());
    }

    internal async Task AddPaths(string[] paths)
    {
        if (busy || importing) return;
        if (paths.Length + Rows.Count > Engine.MaxFiles) { StatusText.Text = "Batch is too large."; StatusDetail.Text = "Choose at most 500 files. No files were added."; return; }
        importing = true;
        SetLocked(true);
        Progress.IsIndeterminate = true;
        StatusText.Text = "Reading your images…";
        var known = Rows.Select(r => r.Info.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var added = 0;
        foreach (var path in paths)
        {
            if (known.Contains(path)) continue;
            try
            {
                var info = await Engine.OnWorker(() => Engine.Inspect(path));
                Rows.Add(new() { Info = info });
                known.Add(info.Path);
                added++;
            }
            catch (Exception ex) when (Engine.IsExpected(ex)) { failures.Add($"{Path.GetFileName(path)}: {Engine.Explain(ex)}"); }
        }
        importing = false;
        Progress.IsIndeterminate = false;
        SetLocked(false);
        if (Queue.SelectedItem == null && Rows.Count > 0) Queue.SelectedIndex = 0;
        UpdatePlan();
        StatusText.Text = $"Added {added} image{(added == 1 ? "" : "s")}{(failures.Count > 0 ? $" · {failures.Count} rejected" : "")}.";
        StatusDetail.Text = failures.Count > 0 ? string.Join(" | ", failures.Take(3)) + (failures.Count > 3 ? " | Additional files also rejected." : "") : "Select an image to preview it. Choose a separate destination folder.";
        StatusDetail.ToolTip = string.Join(Environment.NewLine, failures);
    }

    private void SetLocked(bool locked)
    {
        SettingsPanel.IsEnabled = AddButton.IsEnabled = SamplesButton.IsEnabled = RemoveButton.IsEnabled = ClearButton.IsEnabled = !locked;
        StartButton.IsEnabled = !locked && plans != null;
    }

    private void ChooseDestination(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a separate output folder" };
        if (dialog.ShowDialog(this) != true) return;
        SetDestination(dialog.FolderName);
    }

    internal void SetDestination(string folder)
    {
        destination = folder;
        DestinationLabel.Text = destination.Length <= 50 ? destination : Path.GetPathRoot(destination) + "…" + Path.DirectorySeparatorChar + new DirectoryInfo(destination).Parent?.Name + Path.DirectorySeparatorChar + Path.GetFileName(destination);
        DestinationLabel.ToolTip = destination;
        UpdatePlan();
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = Queue.SelectedItem as ImageRow;
        Preview.Source = row?.Info.Thumbnail;
        EmptyPreview.Visibility = row == null ? Visibility.Visible : Visibility.Collapsed;
        PreviewDetails.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
        PreviewName.Text = row?.Name ?? "";
        PreviewSize.Text = row?.Dimensions ?? "";
        if (row != null && row.Status is "Failed" or "Saved" or "Cancelled") { StatusText.Text = row.Status + " · " + row.Name; StatusDetail.Text = row.Detail; }
    }

    private void RemoveSelected(object sender, RoutedEventArgs e)
    {
        foreach (var row in Queue.SelectedItems.Cast<ImageRow>().ToArray()) Rows.Remove(row);
        if (Rows.Count > 0) Queue.SelectedIndex = 0;
        UpdatePlan();
    }
    private void ClearQueue(object sender, RoutedEventArgs e) { Rows.Clear(); Progress.Value = 0; OpenButton.Visibility = Visibility.Collapsed; StatusText.Text = "Ready for a fresh batch."; StatusDetail.Text = "Add PNG or JPEG images."; UpdatePlan(); }

    private async void StartBatch(object sender, RoutedEventArgs e) => await ProcessBatch();

    internal async Task ProcessBatch()
    {
        UpdatePlan(); // Recheck existing output names immediately before execution.
        if (plans == null || !StartButton.IsEnabled) return;
        var runPlans = plans;
        var settings = ReadSettings();
        cancellation = new();
        busy = true;
        SetLocked(true);
        CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        OpenButton.Visibility = Visibility.Collapsed;
        Progress.Maximum = Rows.Count;
        Progress.Value = 0;
        foreach (var row in Rows) { row.Status = "Waiting"; row.Detail = "Waiting in this batch."; }
        StatusText.Text = "Processing your batch…";
        StatusDetail.Text = "Cancel stops after the current codec operation. Completed files are kept.";
        var completed = 0;
        try
        {
            await Engine.OnWorker(() =>
            {
                Engine.Run(runPlans, settings, cancellation.Token, result => Dispatcher.Invoke(() =>
                {
                    var row = Rows.First(r => r.Info.Path == result.Plan.Source.Path);
                    row.Status = result.Status;
                    row.Detail = result.Detail;
                    if (result.Status is "Saved" or "Failed") Progress.Value = ++completed;
                }));
                return true;
            });
        }
        catch (Exception ex) when (Engine.IsExpected(ex)) { StatusDetail.Text = Engine.Explain(ex); }
        finally
        {
            var cancelled = cancellation.IsCancellationRequested;
            foreach (var row in Rows.Where(r => r.Status == "Waiting")) { row.Status = cancelled ? "Cancelled" : "Failed"; row.Detail = cancelled ? "Not started. No output saved." : "Batch stopped before this image."; }
            busy = false;
            SetLocked(false);
            CancelButton.Visibility = Visibility.Collapsed;
            OpenButton.Visibility = Visibility.Visible;
            StatusText.Text = cancelled ? "Batch cancelled. Finished copies are safe." : "Batch finished.";
            StatusDetail.Text = $"{Rows.Count(r => r.Status == "Saved")} saved · {Rows.Count(r => r.Status == "Failed")} failed · {Rows.Count(r => r.Status == "Cancelled")} cancelled. Select a row for details. Run again to create new copies.";
            var firstFailure = Rows.FirstOrDefault(r => r.Status == "Failed");
            if (firstFailure != null) StatusDetail.Text = $"{Rows.Count(r => r.Status == "Saved")} saved · {Rows.Count(r => r.Status == "Failed")} failed. {firstFailure.Name}: {firstFailure.Detail}";
            cancellation.Dispose();
            cancellation = null;
            StartButton.IsEnabled = true;
        }
    }

    private void CancelBatch(object sender, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; StatusText.Text = "Cancelling…"; }
    private void OpenOutput(object sender, RoutedEventArgs e)
    {
        try { if (destination != null) Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or IOException) { StatusText.Text = "Could not open the folder."; StatusDetail.Text = ex.Message; }
    }
    private void FilesDragged(object sender, DragEventArgs e) { e.Effects = !busy && !importing && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void FilesDropped(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await AddPaths(paths); }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (busy || importing) { e.Cancel = true; cancellation?.Cancel(); StatusText.Text = "Wait for the current operation, then close again."; }
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control && !busy && !importing) { AddImages(this, e); e.Handled = true; }
        if (e.Key == Key.Escape && busy) { CancelBatch(this, e); e.Handled = true; }
    }
}
