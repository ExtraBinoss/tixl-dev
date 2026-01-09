#nullable enable
namespace T3.Editor.Gui.Windows.RenderExport;

internal sealed class FFMpegRenderSettings
{
    public static readonly FFMpegRenderSettings Current = new()
                                                        {
                                                            Reference = TimeReference.Bars,
                                                            StartInBars = 0f,
                                                            EndInBars = 4f,
                                                            Fps = 60f,
                                                            OverrideMotionBlurSamples = -1,
                                                        };
    
    public TimeReference Reference;
    public float StartInBars;
    public float EndInBars;
    public float Fps;
    public int OverrideMotionBlurSamples;   // forwarded for operators that might read it

    
    public  RenderModes RenderMode = RenderModes.Video;
    public  int Bitrate = 25_000_000;
    public  bool ExportAudio = true;
    // public  ScreenshotWriter.FileFormats FileFormat; // Image sequence not primary focus for FFMpeg yet? Or maybe it is?
    // Let's keep it to support image sequences via FFMpeg if we want, but for now maybe just Video is enough? 
    // The user wants side by side, so maybe keep features parity. FFMpeg can do image sequences too.
    // But for now let's keep it simple and focus on Video as per request for "ffmpeg thing". 
    // Actually, let's keep the structure similar.
    
    public  TimeRanges TimeRange = TimeRanges.Custom;
    public  float ResolutionFactor = 1f;

    public int FrameCount;
    
    internal enum RenderModes
    {
        Video,
        // ImageSequence // FFMpeg is mostly for video, but could do images. Let's comment out for now to simplify or keep it?
        // Let's keep it simple: Video only for now as FFMpeg major gain is video codecs.
    }

    internal enum TimeReference
    {
        Bars,
        Seconds,
        Frames
    }

    internal enum TimeRanges
    {
        Custom,
        Loop,
        Soundtrack,
    }

    internal readonly struct QualityLevel
    {
        internal QualityLevel(double bits, string title, string description)
        {
            MinBitsPerPixelSecond = bits;
            Title = title;
            Description = description;
        }

        internal readonly double MinBitsPerPixelSecond;
        internal readonly string Title;
        internal readonly string Description;
    }
}
