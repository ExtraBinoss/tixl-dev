#nullable enable
using System;
using System.IO;
using System.Linq;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport.FFMpeg;
using T3.Editor.UiModel.ProjectHandling;
using FFMpegCore;
using FFMpegCore.Helpers;
using FFMpegCore.Extensions.Downloader;
using System.Threading.Tasks;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class FFMpegRenderProcess
{
    public static string LastHelpString { get; private set; } = string.Empty;
    public static double Progress => _frameCount <= 1 ? 0.0 : (_frameIndex / (double)(_frameCount - 1));
    
    public static Type? MainOutputType { get; private set; }
    public static Int2 MainOutputOriginalSize;
    public static Int2 MainOutputRenderedSize;
    public static Texture2D? MainOutputTexture;
    
    public static States State;

    public static bool IsExporting { get; private set; }
    public static bool IsToollRenderingSomething { get; private set; }
    
    public static double ExportStartedTimeLocal;
    
    public enum States
    {
        NoOutputWindow,
        NoValidOutputType,
        NoValidOutputTexture,
        WaitingForExport,
        Exporting,
    }

    public static void Update()
    {
        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            State = States.NoOutputWindow;
            return;
        }

        MainOutputTexture = outputWindow.GetCurrentTexture();
        if (MainOutputTexture == null)
        {
            State = States.NoValidOutputTexture;
            return;
        }

        MainOutputType = outputWindow.ShownInstance?.Outputs.FirstOrDefault()?.ValueType;
        if (MainOutputType != typeof(Texture2D))
        {
            State = States.NoValidOutputType;
            return;
        }

        // HandleRenderShortCuts(); // Disable shortcuts for FFMpeg window for now to avoid conflicts

        if (!IsExporting)
        {
            var desc = MainOutputTexture.Description;
            MainOutputOriginalSize.Width = desc.Width;
            MainOutputOriginalSize.Height = desc.Height;

            MainOutputRenderedSize = new Int2(((int)(desc.Width * FFMpegRenderSettings.Current.ResolutionFactor)).Clamp(1,16384),
                                              ((int)(desc.Height * FFMpegRenderSettings.Current.ResolutionFactor)).Clamp(1,16384));
            
            State = States.WaitingForExport;
            return;
        }

        State = States.Exporting;

        // Process frame
        var audioFrame = AudioRendering.GetLastMixDownBuffer(1.0 / _renderSettings.Fps);
        
        bool success = true;
        try 
        {
            _videoWriter?.ProcessFrames(MainOutputTexture, ref audioFrame, RenderAudioInfo.SoundtrackChannels(), RenderAudioInfo.SoundtrackSampleRate());
            _frameIndex++;
             // We need to advance time!
            RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
        }
        catch(Exception e)
        {
            LastHelpString = e.Message;
            success = false;
        }

        // Update stats
        var currentFrame =  _frameIndex; // FFMpeg writer doesn't need "skip images" buffering logic as strictly? Maybe it does for readback delay.
        // Assuming synchronous-ish readback initiation effectively or relying on queue.
        // If we use async readback, _framIndex might be ahead of written frames.
        
        var completed = currentFrame >= _frameCount || !success;

        if (!completed) 
            return;

        Finished();
    }
    
    private static void Finished()
    {
        var duration = Playback.RunTimeInSecs - _exportStartedTime;
        LastHelpString = $"FFMpeg Render finished in {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Log.Debug(LastHelpString);

        if (_renderSettings.Bitrate > 0 && true) // Check auto-increment?
        {
             // Maybe implement increment if settings have it
        }

        Cleanup();
        IsToollRenderingSomething = false;
    }

    public static void TryStart(FFMpegRenderSettings renderSettings)
    {
        if (IsExporting)
        {
            Log.Warning("Export is already in progress");
            return;
        }
        
        // Setup filename
        var targetFilePath = RenderPaths.ResolveProjectRelativePath(UserSettings.Config.RenderVideoFilePath);
        
        // Basic validation...
        
        // renderSettings.FrameCount = 100; // Placeholder removed
        // Need to convert renderSettings to RenderSettings type for RenderTiming?
        // Or duplicate RenderTiming logic.
        // Quick hack: Create a temp RenderSettings object to pass to utils?
        var tempSettings = new RenderSettings()
        {
            Reference = (RenderSettings.TimeReference)renderSettings.Reference,
            StartInBars = renderSettings.StartInBars,
            EndInBars = renderSettings.EndInBars,
            Fps = renderSettings.Fps,
            FrameCount = renderSettings.FrameCount,
            TimeRange = (RenderSettings.TimeRanges)renderSettings.TimeRange
        };
        renderSettings.FrameCount = RenderTiming.ComputeFrameCount(tempSettings);
        
        IsToollRenderingSomething = true;
        ExportStartedTimeLocal = Core.Animation.Playback.RunTimeInSecs;

        _renderSettings = tempSettings; // Use the casted one for internal usage with RenderTiming
        
        _frameIndex = 0;
        _frameCount = Math.Max(_renderSettings.FrameCount, 0);
        _exportStartedTime = Playback.RunTimeInSecs;

        _videoWriter = new FFMpegVideoWriter(targetFilePath, MainOutputOriginalSize, renderSettings.ExportAudio)
                           {
                               Bitrate = renderSettings.Bitrate,
                               Framerate = renderSettings.Fps
                           };
                           
        _videoWriter.Start();

        RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
        IsExporting = true;
        LastHelpString = "Rendering with FFMpeg…";
    }

    public static void Cancel(string? reason = null)
    {
        LastHelpString = reason ?? "Cancelled";
        Cleanup();
        IsToollRenderingSomething = false;
    }

    private static void Cleanup()
    {
        IsExporting = false;
        _videoWriter?.Dispose();
        _videoWriter = null;
        RenderTiming.ReleasePlaybackTime(ref _renderSettings, ref _runtime);
    }
    
    // FFMpeg install logic
    public static bool IsFFMpegAvailable()
    {
        // Simple check
        // Or check if GlobalFFOptions.Current.BinaryFolder is set and valid
        try 
        {
            // GlobalFFOptions.Configure(); // default
            // If ffmpeg is in path, this should be enough?
            // Actually FFMpegCore wrapper doesn't expose "IsAvailable".
            // We can check if ffmpeg.exe exists in user binaries folder.
            var folder = GetFFBinariesFolder();
            // Just return false if folder empty for now to show download button (which now warns)
            return File.Exists(Path.Combine(folder, "ffmpeg.exe"));
        }
        catch
        {
            return false;
        }
    }
    
    public static async void InstallFFMpeg()
    {
        var folder = GetFFBinariesFolder();
        try 
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            Log.Info($"Downloading FFMpeg to {folder}...");
            await FFMpegDownloader.DownloadBinaries(FFMpegCore.Extensions.Downloader.Enums.FFMpegVersions.LatestAvailable, options: new FFOptions { BinaryFolder = folder });
            Log.Info("FFMpeg download complete.");
        }
        catch(Exception e)
        {
            Log.Error($"Failed to download FFMpeg: {e.Message}");
        }
        
        // Configure options
        GlobalFFOptions.Configure(new FFOptions { BinaryFolder = folder });
    }
    
    private static string GetFFBinariesFolder()
    {
        // Save in user profile or app data
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tixl", "ffmpeg");
    }
    
    // Initialize
    static FFMpegRenderProcess()
    {
        // Check availability on init
        var folder = GetFFBinariesFolder();
        if (File.Exists(Path.Combine(folder, "ffmpeg.exe")))
        {
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = folder });
        }
    }

    // State
    private static FFMpegVideoWriter? _videoWriter;
    private static double _exportStartedTime;
    private static int _frameIndex;
    private static int _frameCount;

    private static RenderSettings _renderSettings = null!; // Using standard settings for timing helpers
    private static RenderTiming.Runtime _runtime;
}
