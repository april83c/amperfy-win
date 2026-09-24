using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Amperfy.App.Services;

/// CI helper: when started with "--screenshot-tour &lt;dir&gt;", the app executes the registered
/// steps (navigate somewhere, wait) and renders the window content of each step to a PNG file.
public sealed class ScreenshotTour
{
    public string OutputDirectory { get; }
    private readonly List<(string Name, Func<Task> Action)> _steps = [];

    public ScreenshotTour(string outputDirectory)
    {
        OutputDirectory = outputDirectory;
        Directory.CreateDirectory(outputDirectory);
    }

    public static ScreenshotTour? FromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var idx = Array.IndexOf(args, "--screenshot-tour");
        if (idx < 0 || idx + 1 >= args.Length) return null;
        return new ScreenshotTour(args[idx + 1]);
    }

    private readonly List<Func<IEnumerable<(string Name, Func<Task> Action)>>> _dynamicSteps = [];

    public void AddStep(string name, Func<Task> action) => _steps.Add((name, action));

    /// Steps built after the static steps ran (e.g. depending on the synced library).
    public void AddDynamicSteps(Func<IEnumerable<(string Name, Func<Task> Action)>> factory) => _dynamicSteps.Add(factory);

    public async Task RunAsync(Window window, Action? onFinished = null)
    {
        foreach (var (name, action) in _steps.Concat(_dynamicSteps.SelectMany(f => f())))
        {
            try
            {
                CrashLog.Write($"Tour step: {name}");
                await action();
                await Task.Delay(2500);
                await CaptureAsync(window, name);
            }
            catch (Exception ex)
            {
                CrashLog.Write($"Tour step {name} failed: {ex}");
            }
        }
        CrashLog.Write("Tour finished");
        onFinished?.Invoke();
    }

    public async Task CaptureAsync(Window window, string name)
    {
        if (window.Content is not UIElement root) return;
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var pixels = await rtb.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var dpi = (float)(root.XamlRoot?.RasterizationScale ?? 1.0) * 96f;
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, dpi, dpi, pixels.ToArray());
        await encoder.FlushAsync();
        stream.Seek(0);
        var bytes = new byte[stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
        }
        var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        await File.WriteAllBytesAsync(Path.Combine(OutputDirectory, $"{_captureIndex++:00}-{safe}.png"), bytes);
    }

    private int _captureIndex;
}
