#nullable enable
namespace T3.Editor.Gui.Windows.RenderExport;

using FFMpegCore.Enums;

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
    }

    public bool ShowAdvancedSettings;
    public SelectedCodec Codec = SelectedCodec.H264;
    public int CrfQuality = 23;
    public Speed Preset = Speed.Fast;

    internal enum SelectedCodec
    {
        H264,
        H265,
        ProRes,
        Hap,
        HapAlpha,
        Vp9,
        Vp9Alpha,
    }
    
    public static string GetFileExtension(SelectedCodec codec)
    {
        return codec switch
        {
            SelectedCodec.H264 => ".mp4",
            SelectedCodec.H265 => ".mp4",
            SelectedCodec.ProRes => ".mov",
            SelectedCodec.Hap => ".mov",
            SelectedCodec.HapAlpha => ".mov",
            SelectedCodec.Vp9 => ".webm",
            SelectedCodec.Vp9Alpha => ".webm",
            _ => ".mp4"
        };
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
