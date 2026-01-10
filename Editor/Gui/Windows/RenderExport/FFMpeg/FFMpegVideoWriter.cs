#nullable enable
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Pipes;
using FFMpegCore.Enums; // Fix: VideoCodec, Speed
using SharpDX.Direct3D11;
using T3.Core.DataTypes.Vector;
using T3.Core.Resource;

// Fix: explicit alias to avoid ambiguity between T3.Core.DataTypes.Texture2D and SharpDX.Direct3D11.Texture2D
using CoreTexture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.Windows.RenderExport.FFMpeg;

internal class FFMpegVideoWriter : IDisposable
{
    public string FilePath { get; }
    public int Bitrate { get; set; } = 25_000_000;
    public double Framerate { get; set; } = 60.0;
    public bool ExportAudio { get; set; }
    public FFMpegRenderSettings.SelectedCodec Codec { get; set; } = FFMpegRenderSettings.SelectedCodec.OpenH264;
    public int Crf { get; set; } = 23;
    public Speed Preset { get; set; } = Speed.Fast;
    
    private readonly int _audioSampleRate;
    private readonly int _audioChannels;

    public bool IsImageSequence { get; set; }
    public FFMpegRenderSettings.ImageFileFormats ImageFormat { get; set; }

    private readonly Int2 _videoPixelSize;
    private Task? _ffmpegTask;
    private bool _isWriting;
    private readonly Action? _onPipeBroken;
    
    // Two-pass audio: write audio to temp file, then mux after video is done
    private string? _tempAudioFile;
    private FileStream? _audioFileStream;
    private readonly Lock _audioLock = new();

    public FFMpegVideoWriter(string filePath, Int2 videoPixelSize, bool exportAudio, int audioSampleRate = 48000, int audioChannels = 2)
    {
        FilePath = filePath;
        _videoPixelSize = videoPixelSize;
        ExportAudio = exportAudio;
        _audioSampleRate = audioSampleRate;
        _audioChannels = audioChannels;
        _onPipeBroken = () => _isWriting = false;
    }

    public void Start()
    {
        if (_isWriting) return;
        _isWriting = true;

        if (IsImageSequence) ExportAudio = false;

        // Ensure directory exists
        var dir = Path.GetDirectoryName(FilePath);
        try
        {
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create directory {dir}: {e.Message}");
            return;
        }

        // Setup temp audio file for two-pass approach
        if (ExportAudio)
        {
            try
            {
                _tempAudioFile = Path.Combine(Path.GetTempPath(), $"tixl_audio_{Guid.NewGuid():N}.raw");
                _audioFileStream = new FileStream(_tempAudioFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);
                Log.Debug($"Audio temp file: {_tempAudioFile}");
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to create temp audio file, disabling audio export: {e.Message}");
                ExportAudio = false;
            }
        }

        // Define source
        var videoSource = new RawVideoPipeSource(GetFramesGenerator())
        {
            FrameRate = Framerate
        };

        // Start FFMpeg task - video only first pass
        _ffmpegTask = Task.Run(async () =>
        {
            try
            {
                var args = FFMpegArguments
                   .FromPipeInput(videoSource);

                // For first pass, output to temp file if we have audio to mux later
                var firstPassOutput = ExportAudio 
                    ? Path.Combine(Path.GetTempPath(), $"tixl_video_{Guid.NewGuid():N}{Path.GetExtension(FilePath)}")
                    : FilePath;

                var processor = args.OutputToFile(firstPassOutput, true, options => {
                       // Common options
                       options.UsingMultithreading(true)
                              .WithSpeedPreset(Preset);
                        
                       // No audio in first pass - will mux later
                       options.WithCustomArgument("-an"); // Explicitly no audio
                       
                       // Codec specific options
                       if (IsImageSequence)
                       {
                           options.ForceFormat("image2");

                           switch (ImageFormat)
                           {
                               case FFMpegRenderSettings.ImageFileFormats.Png:
                                   options.WithVideoCodec("png")
                                          .WithCustomArgument("-compression_level 9"); // Max compression for PNG
                                   break;
                               case FFMpegRenderSettings.ImageFileFormats.Jpg:
                                   options.WithVideoCodec("mjpeg")
                                          .WithCustomArgument("-q:v 2"); // High quality JPEG
                                   break;
                               // Add others if needed
                           }
                       }
                       else 
                       {
                           Console.WriteLine($"FFMpegVideoWriter: {Codec}");
                           switch (Codec)
                           {
                               case FFMpegRenderSettings.SelectedCodec.OpenH264:
                                   options.WithVideoCodec("libopenh264")
                                          .WithVideoBitrate((int)(Bitrate / 1000))
                                          .ForcePixelFormat("yuv420p") // H.264 standard
                                          .WithCustomArgument("-vf scale=trunc(iw/2)*2:trunc(ih/2)*2");
                                   if (Crf >= 0) options.WithConstantRateFactor(Crf);
                                   options.ForceFormat("mp4");
                                   break;
                               case FFMpegRenderSettings.SelectedCodec.ProRes:
                                   options.WithVideoCodec("prores_ks")
                                          .WithVideoBitrate((int)(Bitrate / 1000))
                                          .ForcePixelFormat("yuv422p10le")
                                          .ForceFormat("mov");
                                   break;
                               case FFMpegRenderSettings.SelectedCodec.Hap:
                                   options.WithVideoCodec("hap")
                                          .ForceFormat("mov")
                                          .ForcePixelFormat("rgba")
                                          .WithCustomArgument("-vf scale=trunc(iw/4)*4:trunc(ih/4)*4");
                                   break;
                               case FFMpegRenderSettings.SelectedCodec.HapAlpha:
                                   options.WithVideoCodec("hap")
                                          .WithCustomArgument("-format hap_alpha")
                                          .ForceFormat("mov")
                                          .ForcePixelFormat("rgba")
                                          .WithCustomArgument("-vf scale=trunc(iw/4)*4:trunc(ih/4)*4");
                                   break;
                               case FFMpegRenderSettings.SelectedCodec.Vp9:
                                    options.WithVideoCodec("libvpx-vp9")
                                           .WithVideoBitrate((int)(Bitrate / 1000))
                                           .ForcePixelFormat("yuv420p")
                                           .WithCustomArgument("-vf scale=trunc(iw/2)*2:trunc(ih/2)*2");
                                    if (Crf >= 0) options.WithConstantRateFactor(Crf);
                                    options.ForceFormat("webm");
                                    break;
                           }
                       }
                   }
                   );

                await processor.ProcessAsynchronously();
                
                // Second pass: Mux audio if we have it
                if (ExportAudio && _tempAudioFile != null && File.Exists(_tempAudioFile))
                {
                    await MuxAudioAsync(firstPassOutput, _tempAudioFile, FilePath);
                    
                    // Cleanup temp video file
                    try { File.Delete(firstPassOutput); } catch { }
                }
            }
            catch (Exception e)
            {
                Log.Error($"FFMpeg error: {e.Message}");
            }
        });
    }
    
    private async Task MuxAudioAsync(string videoFile, string audioFile, string outputFile)
    {
        try
        {
            Log.Debug($"Muxing audio into video: {outputFile}");
            
            // Determine audio codec based on video codec
            var audioCodec = Codec switch
            {
                FFMpegRenderSettings.SelectedCodec.Vp9 => "libopus",
                _ => "aac"
            };
            
            var args = FFMpegArguments
                .FromFileInput(videoFile)
                .AddFileInput(audioFile, true, options =>
                {
                    options.ForceFormat("f32le")
                           .WithCustomArgument($"-ar {_audioSampleRate}")
                           .WithCustomArgument($"-ac {_audioChannels}");
                })
                .OutputToFile(outputFile, true, options =>
                {
                    options.CopyChannel() // Copy video stream as-is
                           .WithAudioCodec(audioCodec)
                           .WithCustomArgument("-shortest"); // Match shortest stream
                });
                
            await args.ProcessAsynchronously();
            Log.Debug("Audio mux completed successfully");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to mux audio: {e.Message}");
            // If muxing fails, at least copy the video-only file
            try 
            { 
                File.Copy(videoFile, outputFile, true);
                Log.Warning("Audio mux failed, exported video without audio.");
            } 
            catch { }
        }
        finally
        {
            // Cleanup temp audio file
            try { if (audioFile != null) File.Delete(audioFile); } catch { }
        }
    }

    private readonly BlockingCollection<byte[]> _frameBuffer = new(boundedCapacity: 60);

    private IEnumerable<IVideoFrame> GetFramesGenerator()
    {
        foreach (var frame in _frameBuffer.GetConsumingEnumerable())
        {
            yield return new FFMpegRawFrame(frame, _videoPixelSize.Width, _videoPixelSize.Height, _onPipeBroken);
        }
    }
    
    public void ProcessFrames(CoreTexture2D gpuTexture, ref byte[] audioFrame)
    {
        // Capture audio frame for the callback
        var currentAudioFrame = audioFrame;
        ScreenshotWriter.InitiateConvertAndReadBack2(gpuTexture, (item) => SaveSampleAfterReadback(item, currentAudioFrame));
    }

    private void SaveSampleAfterReadback(TextureBgraReadAccess.ReadRequestItem readRequestItem, byte[]? audioFrame)
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
            
            for (var y = 0; y < height; y++)
            {
                inputStream.Position = (long)y * dataBox.RowPitch;
                inputStream.ReadExactly(frameData, y * rowStride, rowStride);
            }

            if (_isWriting)
            {
                if (!_frameBuffer.IsAddingCompleted)
                {
                    _frameBuffer.Add(frameData);
                }
            }
            else
            {
                Log.Debug("FFMpegVideoWriter: Skipping frame add after dispose.");
            }

            // Write audio to temp file (two-pass approach)
            if (_isWriting && ExportAudio && _audioFileStream != null && audioFrame != null)
            {
                lock (_audioLock)
                {
                    try
                    {
                        _audioFileStream.Write(audioFrame, 0, audioFrame.Length);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Failed to write audio frame: {e.Message}");
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Collection completed or disposed, ignore
        }
        catch (Exception e)
        {
            Log.Error($"Frame read error: {e.Message}");
        }
        finally
        {
            ResourceManager.Device.ImmediateContext.UnmapSubresource(cpuAccessTexture, 0);
            inputStream?.Dispose();
        }
    }

    public void Dispose()
    {
        _isWriting = false;
        _frameBuffer.CompleteAdding();
        
        // Close audio file stream first
        lock (_audioLock)
        {
            _audioFileStream?.Flush();
            _audioFileStream?.Dispose();
            _audioFileStream = null;
        }
        
        try 
        {
            // Wait longer for muxing to complete
            _ffmpegTask?.Wait(ExportAudio ? 30000 : 5000);
        }
        catch (Exception e)
        {
            Log.Debug($"FFMpeg join error: {e.Message}");
        }
        _frameBuffer.Dispose();
    }
}

internal class FFMpegRawFrame(byte[] data, int width, int height, Action? onPipeBroken) : IVideoFrame
{
    private readonly byte[] _data = data;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public string Format => "bgra";
    private readonly Action? _onPipeBroken = onPipeBroken;

    public void Serialize(Stream pipe)
    {
        try
        {
            pipe.Write(_data, 0, _data.Length);
        }
        catch (IOException ioEx)
        {
            Log.Debug($"FFMpegVideoWriter: Pipe broken, stopping write: {ioEx.Message}");
            _onPipeBroken?.Invoke();
        }
        catch (Exception ex) { Log.Error($"FFMpegVideoWriter: Stream error: {ex.Message}"); }
    }

    public Task SerializeAsync(Stream pipe)
    {
        return SerializeAsync(pipe, CancellationToken.None);
    }
    
    public async Task SerializeAsync(Stream pipe, CancellationToken token)
    {
        try
        {
             await pipe.WriteAsync(_data, token);
        }
        catch (IOException ioEx)
        {
            Log.Debug($"FFMpegVideoWriter: Pipe broken (Async), stopping write: {ioEx.Message}");
            _onPipeBroken?.Invoke();
        }
        catch (Exception ex) { Log.Error($"FFMpegVideoWriter: Stream error (Async): {ex.Message}"); }
    }
}
