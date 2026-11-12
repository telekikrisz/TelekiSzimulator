using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TelekiSzimulator.Services;

public sealed class GifAnimation
{
    private GifAnimation(IReadOnlyList<BitmapSource> frames, IReadOnlyList<int> delays)
    {
        Frames = frames;
        Delays = delays;
    }

    public IReadOnlyList<BitmapSource> Frames { get; }

    public IReadOnlyList<int> Delays { get; }

    public static GifAnimation Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidOperationException("A GIF nem tartalmaz képet.");

        var global = decoder.Metadata as BitmapMetadata;
        var width = Query(global, "/logscrdesc/Width");
        var height = Query(global, "/logscrdesc/Height");
        if (width <= 0 || height <= 0)
        {
            width = decoder.Frames.Max(frame => frame.PixelWidth);
            height = decoder.Frames.Max(frame => frame.PixelHeight);
        }

        var canvas = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        var frames = new List<BitmapSource>(decoder.Frames.Count);
        var delays = new List<int>(decoder.Frames.Count);
        byte[]? restore = null;
        var previousDisposal = 0;
        var previousRect = Int32Rect.Empty;

        foreach (var frame in decoder.Frames)
        {
            var metadata = frame.Metadata as BitmapMetadata;
            var left = Query(metadata, "/imgdesc/Left");
            var top = Query(metadata, "/imgdesc/Top");
            var disposal = Query(metadata, "/grctlext/Disposal");
            var delayCs = Query(metadata, "/grctlext/Delay");

            if (previousDisposal == 2)
                ClearRect(canvas, previousRect);
            else if (previousDisposal == 3 && restore != null)
                canvas.WritePixels(new Int32Rect(0, 0, width, height), restore, width * 4, 0);

            if (disposal == 3)
                restore = CopyPixels(canvas);

            DrawFrame(canvas, frame, left, top);
            var displayed = new WriteableBitmap(canvas);
            displayed.Freeze();
            frames.Add(displayed);
            delays.Add(delayCs <= 1 ? 100 : delayCs * 10);

            previousDisposal = disposal;
            previousRect = new Int32Rect(left, top, frame.PixelWidth, frame.PixelHeight);
        }

        return new GifAnimation(frames, delays);
    }

    private static void DrawFrame(WriteableBitmap canvas, BitmapSource frame, int left, int top)
    {
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var frameWidth = converted.PixelWidth;
        var frameHeight = converted.PixelHeight;
        var frameStride = frameWidth * 4;
        var framePixels = new byte[frameStride * frameHeight];
        converted.CopyPixels(framePixels, frameStride, 0);

        var canvasWidth = canvas.PixelWidth;
        var canvasHeight = canvas.PixelHeight;
        var canvasStride = canvasWidth * 4;
        var canvasPixels = CopyPixels(canvas);

        for (var y = 0; y < frameHeight; y++)
        {
            var destinationY = top + y;
            if (destinationY < 0 || destinationY >= canvasHeight)
                continue;

            for (var x = 0; x < frameWidth; x++)
            {
                var destinationX = left + x;
                if (destinationX < 0 || destinationX >= canvasWidth)
                    continue;

                var sourceIndex = (y * frameWidth + x) * 4;
                var alpha = framePixels[sourceIndex + 3];
                if (alpha == 0)
                    continue;

                var destinationIndex = (destinationY * canvasWidth + destinationX) * 4;
                if (alpha == 255)
                {
                    canvasPixels[destinationIndex] = framePixels[sourceIndex];
                    canvasPixels[destinationIndex + 1] = framePixels[sourceIndex + 1];
                    canvasPixels[destinationIndex + 2] = framePixels[sourceIndex + 2];
                    canvasPixels[destinationIndex + 3] = 255;
                    continue;
                }

                var inverse = 255 - alpha;
                canvasPixels[destinationIndex] = (byte)((framePixels[sourceIndex] * alpha + canvasPixels[destinationIndex] * inverse) / 255);
                canvasPixels[destinationIndex + 1] = (byte)((framePixels[sourceIndex + 1] * alpha + canvasPixels[destinationIndex + 1] * inverse) / 255);
                canvasPixels[destinationIndex + 2] = (byte)((framePixels[sourceIndex + 2] * alpha + canvasPixels[destinationIndex + 2] * inverse) / 255);
                canvasPixels[destinationIndex + 3] = (byte)(alpha + canvasPixels[destinationIndex + 3] * inverse / 255);
            }
        }

        canvas.WritePixels(new Int32Rect(0, 0, canvasWidth, canvasHeight), canvasPixels, canvasStride, 0);
    }

    private static void ClearRect(WriteableBitmap canvas, Int32Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var width = canvas.PixelWidth;
        var height = canvas.PixelHeight;
        var stride = width * 4;
        var pixels = CopyPixels(canvas);
        var x0 = Math.Max(0, rect.X);
        var y0 = Math.Max(0, rect.Y);
        var x1 = Math.Min(width, rect.X + rect.Width);
        var y1 = Math.Min(height, rect.Y + rect.Height);

        for (var y = y0; y < y1; y++)
        {
            var start = (y * width + x0) * 4;
            var length = (x1 - x0) * 4;
            Array.Clear(pixels, start, length);
        }

        canvas.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
    }

    private static byte[] CopyPixels(WriteableBitmap canvas)
    {
        var stride = canvas.PixelWidth * 4;
        var pixels = new byte[stride * canvas.PixelHeight];
        canvas.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static int Query(BitmapMetadata? metadata, string query)
    {
        if (metadata == null)
            return 0;

        try
        {
            return metadata.GetQuery(query) switch
            {
                byte value => value,
                ushort value => value,
                short value => value,
                int value => value,
                uint value => (int)value,
                _ => 0
            };
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return 0;
        }
    }
}
