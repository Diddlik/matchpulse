using System.Reflection;
using OpenCvSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Box = Vortice.Mathematics.Box;
using Rect = OpenCvSharp.Rect;

namespace MatchPulse;

/// <summary>
/// Frame Source that captures one monitor with DXGI Desktop Duplication. Only the Cue regions are copied
/// from the GPU; the rest of each full-size frame stays black, so the Match Detector sees the same frames
/// as from the replay. The caller disposes each frame's image.
/// </summary>
public static class LiveFrameSource
{
    // Every Cue declared in Cue.cs, so a new Cue is captured without changes here.
    static readonly Cue[] Cues = typeof(Cue).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => f.GetValue(null)).OfType<Cue>().ToArray();

    public static IEnumerable<Frame> Read(IntPtr monitor, double checksPerSecond, CancellationToken stop)
    {
        if (checksPerSecond is < 5 or > 10)
            throw new ArgumentOutOfRangeException(nameof(checksPerSecond), checksPerSecond, "Must be between 5 and 10.");
        var interval = TimeSpan.FromSeconds(1 / checksPerSecond);

        while (!stop.IsCancellationRequested)
        {
            using var capture = Capture.Open(monitor);
            while (!stop.IsCancellationRequested)
            {
                var next = DateTimeOffset.Now + interval;
                var image = capture.Next();
                if (image is null) break; // access lost, e.g. a display mode change: open again
                yield return new Frame(DateTimeOffset.Now, image);
                var wait = next - DateTimeOffset.Now;
                if (wait > TimeSpan.Zero) stop.WaitHandle.WaitOne(wait);
            }
        }
    }

    sealed class Capture : IDisposable
    {
        readonly ID3D11Device _device;
        readonly ID3D11DeviceContext _context;
        readonly IDXGIOutputDuplication _duplication;
        readonly ID3D11Texture2D _staging;
        readonly int _width, _height;
        readonly Rect[] _regions;

        Capture(ID3D11Device device, ID3D11DeviceContext context, IDXGIOutputDuplication duplication)
        {
            _device = device;
            _context = context;
            _duplication = duplication;
            var mode = duplication.Description.ModeDescription;
            (_width, _height) = ((int)mode.Width, (int)mode.Height);
            _regions = Cues.Select(c => c.Region(new Size(_width, _height))).ToArray();
            _staging = device.CreateTexture2D(new Texture2DDescription
            {
                Width = mode.Width, Height = mode.Height, MipLevels = 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
            });
        }

        public static Capture Open(IntPtr monitor)
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                using (output)
                {
                    if (output.Description.Monitor != monitor) continue;
                    D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, [FeatureLevel.Level_11_0, FeatureLevel.Level_10_0], out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    return new Capture(device, context, output1.DuplicateOutput(device));
                }
            }
            throw new InvalidOperationException($"No DXGI output for monitor {monitor}.");
        }

        /// <summary>The current screen with only the Cue regions filled, or null when access was lost.</summary>
        public Mat? Next()
        {
            var result = _duplication.AcquireNextFrame(100, out _, out var resource);
            if (result == Vortice.DXGI.ResultCode.AccessLost) return null;
            if (result.Success)
            {
                // A new desktop image: copy the Cue regions into the staging texture. On a timeout the screen
                // has not changed and the staging texture still holds the last regions.
                using (resource)
                using (var texture = resource.QueryInterface<ID3D11Texture2D>())
                    foreach (var r in _regions)
                        _context.CopySubresourceRegion(_staging, 0, (uint)r.X, (uint)r.Y, 0, texture, 0,
                            new Box(r.X, r.Y, 0, r.Right, r.Bottom, 1));
                _duplication.ReleaseFrame();
            }
            else if (result != Vortice.DXGI.ResultCode.WaitTimeout) result.CheckError();

            var image = new Mat(_height, _width, MatType.CV_8UC3, Scalar.Black);
            var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                using var bgra = Mat.FromPixelData(_height, _width, MatType.CV_8UC4, mapped.DataPointer, (long)mapped.RowPitch);
                foreach (var r in _regions)
                    using (var target = image[r])
                        Cv2.CvtColor(bgra[r], target, ColorConversionCodes.BGRA2BGR);
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }
            return image;
        }

        public void Dispose()
        {
            _staging.Dispose();
            _duplication.Dispose();
            _context.Dispose();
            _device.Dispose();
        }
    }
}
