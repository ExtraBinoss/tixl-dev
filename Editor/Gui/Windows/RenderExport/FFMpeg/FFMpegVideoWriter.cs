#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Pipes;
using FFMpegCore.Enums; // Fix: VideoCodec, Speed
using SharpDX.Direct3D11;
using T3.Core.DataTypes; 
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Windows.RenderExport.MF; 
using SharpDX;
using SharpDX.WIC; // Fix: PixelFormat
using T3.Core.Resource;
using T3.Core.Utils;

// Fix: explicit alias to avoid ambiguity between T3.Core.DataTypes.Texture2D and SharpDX.Direct3D11.Texture2D
using CoreTexture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.Windows.RenderExport.FFMpeg;

internal class FFMpegVideoWriter : IDisposable
{
    public string FilePath { get; }
    public int Bitrate { get; set; } = 25_000_000;
    public double Framerate { get; set; } = 60.0;
    public bool ExportAudio { get; set; }

    private readonly Int2 _videoPixelSize;
    // private readonly ConcurrentQueue<byte[]> _videoFrames = new(); // Replaced by BlockingCollection
    private Task? _ffmpegTask;
    private bool _isWriting;

    public FFMpegVideoWriter(string filePath, Int2 videoPixelSize, bool exportAudio)
    {
        FilePath = filePath;
        _videoPixelSize = videoPixelSize;
        ExportAudio = exportAudio;
    }

    public void Start()
    {
        if (_isWriting) return;
        _isWriting = true;

        // Ensure directory exists
        var dir = Path.GetDirectoryName(FilePath);
        try 
        {
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch(Exception e)
        {
            Log.Error($"Failed to create directory {dir}: {e.Message}");
            return;
        }

        // Define source
        // Fix: Removed Width/Height/Format assignment as they are read-only or inferred from frames.
        // RawVideoPipeSource(IEnumerable<IVideoFrame>) reads properties from the frames.
        var videoSource = new RawVideoPipeSource(GetFramesGenerator())
        {
            FrameRate = Framerate
        };

        // Start FFMpeg task
        _ffmpegTask = Task.Run(async () =>
        {
            try
            {
                var args = FFMpegArguments
                   .FromPipeInput(videoSource)
                   .OutputToFile(FilePath, true, options => options
                       .WithVideoCodec(VideoCodec.LibX264)
                       .WithVideoBitrate((int)(Bitrate / 1000))
                       .ForcePixelFormat("yuv420p")
                       .UsingMultithreading(true)
                       .ForceFormat("mp4")
                       .WithSpeedPreset(Speed.Fast) 
                   );

                await args.ProcessAsynchronously();
            }
            catch (Exception e)
            {
                Log.Error($"FFMpeg error: {e.Message}");
            }
        });
    }

    private readonly BlockingCollection<byte[]> _frameBuffer = new(boundedCapacity: 60); 

    private IEnumerable<IVideoFrame> GetFramesGenerator()
    {
        foreach (var frame in _frameBuffer.GetConsumingEnumerable())
        {
            yield return new FFMpegRawFrame(frame, _videoPixelSize.Width, _videoPixelSize.Height);
        }
    }
    
    // Fix: Use CoreTexture2D alias
    public void ProcessFrames(CoreTexture2D gpuTexture, ref byte[] audioFrame, int channels, int sampleRate)
    {
        ScreenshotWriter.InitiateConvertAndReadBack2(gpuTexture, SaveSampleAfterReadback);
    }

    private void SaveSampleAfterReadback(TextureBgraReadAccess.ReadRequestItem readRequestItem)
    {
        var cpuAccessTexture = readRequestItem.CpuAccessTexture;
        if (cpuAccessTexture == null || cpuAccessTexture.IsDisposed) return;

        var width = cpuAccessTexture.Description.Width;
        var height = cpuAccessTexture.Description.Height;
        var rowStride = SharpDX.WIC.PixelFormat.GetStride(SharpDX.WIC.PixelFormat.Format32bppRGBA, width);
        
        var dataBox = ResourceManager.Device.ImmediateContext.MapSubresource(cpuAccessTexture, 0, 0, MapMode.Read, MapFlags.None, out var inputStream);
        
        try
        {
            var frameSize = width * height * 4;
            var frameData = new byte[frameSize];
            
            // FFMpegCore/ffmpeg usually expects top-down. 
            // MapSubresource likely gives layout as is (often top-down for D3D textures unless render target logic flips it).
            // MfVideoWriter does logical flipping based on usage. Mp4VideoWriter(H264) wants FlipY for some reason (maybe MP4 container expectation vs Texture coord system).
            // Let's copy MP4 behavior: Read Bottom-Up.
            
            for (var y = 0; y < height; y++)
            {
                inputStream.Position = (long)y * dataBox.RowPitch;
                inputStream.ReadExactly(frameData, y * rowStride, rowStride);
            }

            if (_isWriting)
            {
                _frameBuffer.Add(frameData);
            }
            else
            {
                Log.Debug("FFMpegVideoWriter: Skipping frame add after dispose.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Frame read error: {e.Message}");
        }
        finally
        {
            ResourceManager.Device.ImmediateContext.UnmapSubresource(cpuAccessTexture, 0);
            inputStream?.Dispose();
            // Note: cpuAccessTexture is NOT disposed here as it might be pooled by ScreenshotWriter logic. 
            // Ideally follows MfVideoWriter pattern.
        }
    }

    public void Dispose()
    {
        _isWriting = false;
        _frameBuffer.CompleteAdding();
        try 
        {
            _ffmpegTask?.Wait(2000); // Wait max 2s to flush
        }
        catch (Exception e)
        {
            Log.Debug($"FFMpeg join error: {e.Message}");
        }
        _frameBuffer.Dispose();
    }
}

internal class FFMpegRawFrame : IVideoFrame
{
    private readonly byte[] _data;
    public int Width { get; }
    public int Height { get; }
    public string Format => "bgra";

    public FFMpegRawFrame(byte[] data, int width, int height)
    {
        _data = data;
        Width = width;
        Height = height;
    }

    public void Serialize(Stream pipe)
    {
        pipe.Write(_data, 0, _data.Length);
    }

    public Task SerializeAsync(Stream pipe)
    {
        return pipe.WriteAsync(_data, 0, _data.Length);
    }
    
    // Fix: Implement interface properly for newer FFMpegCore
    public Task SerializeAsync(Stream pipe, CancellationToken token)
    {
         return pipe.WriteAsync(_data, 0, _data.Length, token);
    }
}
