using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// High-speed desktop screen capture engine for SUO Link.
/// Captures primary monitor at configurable FPS (30/60) and quality,
/// streaming frames to connected remote mobile devices.
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    private static ScreenCaptureService? _instance;
    public static ScreenCaptureService Instance => _instance ??= new ScreenCaptureService();

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private byte[]? _latestJpegFrame;
    private long _frameIndex;
    private int _targetFps = 30;
    private int _targetHeight = 720; // 720p default for optimal low-latency mobile streaming
    private long _jpegQuality = 65L;

    public int TargetFps
    {
        get => _targetFps;
        set => _targetFps = Math.Clamp(value, 15, 60);
    }

    public int TargetHeight
    {
        get => _targetHeight;
        set => _targetHeight = Math.Clamp(value, 480, 1080);
    }

    public long JpegQuality
    {
        get => _jpegQuality;
        set => _jpegQuality = Math.Clamp(value, 30, 95);
    }

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public long FrameIndex => Interlocked.Read(ref _frameIndex);

    public event Action<byte[]>? OnNewFrame;

    private static readonly ImageCodecInfo JpegEncoder = GetEncoder(ImageFormat.Jpeg);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    public byte[]? GetLatestFrame()
    {
        lock (_lock)
        {
            return _latestJpegFrame;
        }
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        using var encoderParams = new EncoderParameters(1);

        while (!token.IsCancellationRequested)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                int screenW = GetSystemMetrics(SM_CXSCREEN);
                int screenH = GetSystemMetrics(SM_CYSCREEN);
                if (screenW <= 0 || screenH <= 0)
                {
                    screenW = 1920;
                    screenH = 1080;
                }

                // Calculate target scaled dimensions maintaining aspect ratio
                int outH = _targetHeight;
                int outW = (int)((float)screenW / screenH * outH);
                if (outW % 2 != 0) outW++; // keep even

                using var screenBmp = new Bitmap(screenW, screenH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(screenBmp))
                {
                    g.CopyFromScreen(0, 0, 0, 0, new Size(screenW, screenH), CopyPixelOperation.SourceCopy);
                }

                using var outputBmp = new Bitmap(outW, outH, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(outputBmp))
                {
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                    g.DrawImage(screenBmp, 0, 0, outW, outH);
                }

                using var ms = new MemoryStream();
                using (var param = new EncoderParameter(Encoder.Quality, _jpegQuality))
                {
                    encoderParams.Param[0] = param;
                    outputBmp.Save(ms, JpegEncoder, encoderParams);
                }

                byte[] jpegBytes = ms.ToArray();

                lock (_lock)
                {
                    _latestJpegFrame = jpegBytes;
                }

                Interlocked.Increment(ref _frameIndex);
                OnNewFrame?.Invoke(jpegBytes);
            }
            catch (Exception)
            {
                // Capture error or desktop locked - sleep briefly and retry
                await Task.Delay(100, token).ConfigureAwait(false);
            }

            // Frame rate pacing
            int frameTimeMs = 1000 / _targetFps;
            var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            int delay = Math.Max(1, frameTimeMs - elapsed);

            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return codecs[0];
    }

    public void Dispose()
    {
        Stop();
    }
}
