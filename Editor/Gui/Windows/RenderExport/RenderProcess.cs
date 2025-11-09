// RenderProcess.cs
#nullable enable
using System.IO;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.RenderExport.MF;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class RenderProcess
{
    public static bool IsExporting { get; private set; }
    public static string LastHelpString => _lastHelpString;
    public static double Progress => FrameCount <= 1 ? 0.0 : (FrameIndex / (double)(FrameCount - 1));
    public static int FrameIndex { get; private set; }
    public static int FrameCount { get; private set; }

    public static int GetRealFrame() => FrameIndex - MfVideoWriter.SkipImages;

    public static void Start(string targetPath,
                             Int2 size,
                             RenderSettings.RenderMode mode,
                             int bitrate,
                             bool exportAudio,
                             ScreenshotWriter.FileFormats fileFormat,
                             RenderSettings.Settings timing,
                             int frameCount)
    {
        _renderMode = mode;
        _bitrate = bitrate;
        _exportAudio = exportAudio;
        _fileFormat = fileFormat;
        _timing = timing;
        FrameIndex = 0;
        FrameCount = Math.Max(frameCount, 0);

        _exportStartedTime = Playback.RunTimeInSecs;

        if (mode == RenderSettings.RenderMode.Video)
        {
            _videoWriter = new Mp4VideoWriter(targetPath, size, exportAudio)
                               {
                                   Bitrate = bitrate,
                                   Framerate = (int)timing.Fps
                               };
        }
        else
        {
            _targetFolder = targetPath;
        }

        ScreenshotWriter.ClearQueue();

        // set playback to the first frame
        RenderTiming.SetPlaybackTimeForFrame(ref _timing, FrameIndex, FrameCount, ref _runtime);
        IsExporting = true;
        _lastHelpString = "Rendering…";
    }

    public static bool ProcessFrame(ref Texture2D mainTexture, Int2 size)
    {
        if (_renderMode == RenderSettings.RenderMode.Video)
        {
            var audioFrame = AudioRendering.GetLastMixDownBuffer(1.0 / _timing.Fps);
            return SaveVideoFrameAndAdvance(ref mainTexture, ref audioFrame, RenderAudioInfo.SoundtrackChannels(), RenderAudioInfo.SoundtrackSampleRate());
        }

        AudioRendering.GetLastMixDownBuffer(Playback.LastFrameDuration);
        return SaveImageFrameAndAdvance(mainTexture);
    }

    public static void Cancel(string? reason = null)
    {
        var duration = Playback.RunTimeInSecs - _exportStartedTime;
        _lastHelpString = reason ?? $"Render cancelled after {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Cleanup();
    }

    public static void Finish(bool success, bool autoIncrementVideoFile)
    {
        var duration = Playback.RunTimeInSecs - _exportStartedTime;
        var successful = success ? "successfully" : "unsuccessfully";
        _lastHelpString = $"Render finished {successful} in {StringUtils.HumanReadableDurationFromSeconds(duration)}\n Ready to render.";

        if (success && _renderMode == RenderSettings.RenderMode.Video && autoIncrementVideoFile)
            RenderPaths.TryIncrementVideoFileNameInUserSettings();

        Cleanup();
    }

    private static void Cleanup()
    {
        IsExporting = false;

        if (_renderMode == RenderSettings.RenderMode.Video)
        {
            _videoWriter?.Dispose();
            _videoWriter = null;
        }

        RenderTiming.ReleasePlaybackTime(ref _timing, ref _runtime);
    }

    private static bool SaveVideoFrameAndAdvance(ref Texture2D mainTexture, ref byte[] audioFrame, int channels, int sampleRate)
    {
        if (Playback.OpNotReady)
        {
            Log.Debug("Waiting for operators to complete");
            return true;
        }

        try
        {
            _videoWriter?.ProcessFrames(ref mainTexture, ref audioFrame, channels, sampleRate);
            FrameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(ref _timing, FrameIndex, FrameCount, ref _runtime);
            return true;
        }
        catch (Exception e)
        {
            _lastHelpString = e.ToString();
            Cleanup();
            return false;
        }
    }

    private static string GetSequenceFilePath()
    {
        var prefix = RenderPaths.SanitizeFilename(UserSettings.Config.RenderSequenceFileName);
        return Path.Combine(_targetFolder, $"{prefix}_{FrameIndex:0000}.{_fileFormat.ToString().ToLower()}");
    }

    private static bool SaveImageFrameAndAdvance(Texture2D mainTexture)
    {
        try
        {
            var success = ScreenshotWriter.StartSavingToFile(mainTexture, GetSequenceFilePath(), _fileFormat);
            FrameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(ref _timing, FrameIndex, FrameCount, ref _runtime);
            return success;
        }
        catch (Exception e)
        {
            _lastHelpString = e.ToString();
            IsExporting = false;
            return false;
        }
    }

    // State
    private static RenderSettings.RenderMode _renderMode;
    private static int _bitrate = 25_000_000;
    private static bool _exportAudio = true;
    private static ScreenshotWriter.FileFormats _fileFormat;
    private static Mp4VideoWriter? _videoWriter;
    private static string _targetFolder = string.Empty;
    private static double _exportStartedTime;
    private static string _lastHelpString = string.Empty;

    private static RenderSettings.Settings _timing;
    private static RenderTiming.Runtime _runtime;
}