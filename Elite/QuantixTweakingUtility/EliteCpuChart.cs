using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuantixTweakingUtility;

internal sealed class EliteCpuChart : FrameworkElement
{
    private readonly Queue<double> _samples = new();
        internal enum Metric { Cpu, Ram, Gpu }
    private Metric _metric;
    private double? _latest;
    private bool _readingGpu;
    private bool _gpuUnavailable;
    private int _selectionVersion;
    internal event Action<Metric, double?>? SampleUpdated;
    private ulong _idle;
    private long _sampleTimestamp;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public void SelectMetric(Metric metric)
    {
        if (_metric == metric) return;
        _metric = metric;
        _selectionVersion++;
        _samples.Clear();
        _latest = null;
        _gpuUnavailable = false;
        _total = 0;
        SampleUpdated?.Invoke(_metric, null);
        Sample();
        InvalidateVisual();
    }

    private void AddSample(double value)
    {
        if (_samples.Count == 48) _samples.Dequeue();
        _samples.Enqueue(value);
        _latest = value;
        SampleUpdated?.Invoke(_metric, value);
    }
    private ulong _total;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    public EliteCpuChart()
    {
        Height = 175;
        Margin = new Thickness(0, 0, 8, 16);
        _timer.Tick += (_, _) => Sample();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Sample(); _timer.Start(); }
            else { _timer.Stop(); _total = 0; }
        };
        Unloaded += (_, _) => _timer.Stop();
        Sample();
    }

    private void Sample()
    {
        if (_metric == Metric.Ram)
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0)
                AddSample(1.0 - (double)status.AvailablePhysical / status.TotalPhysical);
            else ClearUnavailable();
            InvalidateVisual();
            return;
        }
        if (_metric == Metric.Gpu)
        {
            if (!_readingGpu)
                _ = SampleGpuAsync(_selectionVersion);
            return;
        }
        if (!GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            _total = 0;
            ClearUnavailable();
            InvalidateVisual();
            return;
        }
        ulong total = kernel + user;
        long timestamp = Stopwatch.GetTimestamp();
        if (_total != 0 && total > _total && timestamp - _sampleTimestamp >= Stopwatch.Frequency / 4)
        {
            AddSample(Math.Clamp(1.0 - (double)(idle - _idle) / (total - _total), 0, 1));
        }
        _idle = idle;
        _total = total;
        _sampleTimestamp = timestamp;
        InvalidateVisual();
    }

    private void ClearUnavailable()
    {
        _latest = null;
        _samples.Clear();
        SampleUpdated?.Invoke(_metric, null);
    }

    private async Task SampleGpuAsync(int version)
    {
        _readingGpu = true;
        try
        {
            double? usage = await Task.Run(ReadGpuUsage);
            if (_metric != Metric.Gpu || version != _selectionVersion || !IsVisible)
                return;
            _gpuUnavailable = !usage.HasValue;
            if (usage.HasValue)
                AddSample(usage.Value);
            else ClearUnavailable();
        }
        finally
        {
            _readingGpu = false;
            InvalidateVisual();
        }
    }

    private static double? ReadGpuUsage()
    {
        try
        {
            using var query = new ManagementObjectSearcher(
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var results = query.Get();
            var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject counter in results)
            {
                using (counter)
                {
                    if (counter["Name"] is not string name || counter["UtilizationPercentage"] is not ulong percent)
                        continue;
                    int engineStart = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                    if (engineStart < 0)
                        continue;
                    string engine = name[engineStart..];
                    engines[engine] = engines.GetValueOrDefault(engine) + percent;
                }
            }
            return engines.Count == 0 ? null : Math.Clamp(engines.Values.Max() / 100.0, 0, 1);
        }
        catch (ManagementException) { return null; }
        catch (COMException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var background = new SolidColorBrush(Color.FromRgb(30, 24, 43));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(54, 44, 72)), 1);
        var accent = new Pen(new SolidColorBrush(Color.FromRgb(163, 117, 255)), 2);
        drawing.DrawRoundedRectangle(background, null, new Rect(RenderSize), 9, 9);
        var label = new FormattedText((_metric == Metric.Gpu && _gpuUnavailable ? "GPU LOAD   ·   Telemetry unavailable" : $"{_metric.ToString().ToUpperInvariant()} LOAD"),
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"),
            12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(label, new Point(14, 46));
        if (_latest.HasValue)
        {
            var value = new FormattedText($"{_latest.Value * 100:F0}%", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 23, accent.Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(value, new Point(Math.Max(14, ActualWidth - value.Width - 14), 10));
        }
        double width = Math.Max(0, ActualWidth - 28);
        double top = 70;
        double bottom = Math.Max(top, ActualHeight - 16);
        for (int i = 0; i <= 4; i++)
        {
            double y = top + i * (bottom - top) / 4;
            drawing.DrawLine(grid, new Point(14, y), new Point(14 + width, y));
            double x = 14 + i * width / 4;
            drawing.DrawLine(grid, new Point(x, top), new Point(x, bottom));
        }
        Point? previous = null;
        int index = 0;
        foreach (double sample in _samples)
        {
            var point = new Point(14 + index++ * width / 47, bottom - sample * (bottom - top));
            if (previous.HasValue) drawing.DrawLine(accent, previous.Value, point);
            previous = point;
        }
        if (previous.HasValue) drawing.DrawEllipse(accent.Brush, null, previous.Value, 2.5, 2.5);
    }
}


