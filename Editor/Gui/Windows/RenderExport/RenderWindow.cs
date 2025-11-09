// RenderWindow.cs (updated to use the extracted classes)
#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Core.Utils;

namespace T3.Editor.Gui.Windows.RenderExport;

internal sealed class RenderWindow : Window
{
    public RenderWindow()
    {
        Config.Title = "Render To File";
    }

    // UI + orchestration
    protected override void DrawContent()
    {
        FormInputs.AddVerticalSpace(15);
        DrawTimeSetup();
        ImGui.Indent(5);
        DrawInnerContent();
    }

    private void DrawInnerContent()
    {
        var outputWindow = OutputWindow.GetPrimaryOutputWindow();
        if (outputWindow == null)
        {
            _lastHelpString = "No output view available";
            CustomComponents.HelpText(_lastHelpString);
            return;
        }

        var mainTexture = outputWindow.GetCurrentTexture();
        var outputType = outputWindow.ShownInstance?.Outputs.FirstOrDefault()?.ValueType;
        if (outputType != typeof(Texture2D))
        {
            _lastHelpString = outputType == null
                                  ? "The output view is empty"
                                  : "Select or pin a Symbol with Texture2D output in order to render to file";
            FormInputs.AddVerticalSpace(5);
            ImGui.Separator();
            FormInputs.AddVerticalSpace(5);
            ImGui.BeginDisabled();
            ImGui.Button("Start Render");
            CustomComponents.TooltipForLastItem("Only Symbols with a texture2D output can be rendered to file");
            ImGui.EndDisabled();
            CustomComponents.HelpText(_lastHelpString);
            return;
        }

        _lastHelpString = "Ready to render.";

        FormInputs.AddVerticalSpace();
        FormInputs.AddSegmentedButtonWithLabel(ref _renderMode, "Render Mode");

        Int2 size = default;
        if (mainTexture != null)
        {
            var desc = mainTexture.Description;
            size.Width = desc.Width;
            size.Height = desc.Height;
        }

        FormInputs.AddVerticalSpace();

        if (_renderMode == RenderSettings.RenderMode.Video)
            DrawVideoSettings(size);
        else
            DrawImageSequenceSettings();

        FormInputs.AddVerticalSpace(5);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(5);

        DrawRenderingControls(ref mainTexture!, size);

        CustomComponents.HelpText(RenderProcess.IsExporting ? RenderProcess.LastHelpString : _lastHelpString);
    }

    // Time setup UI drives RenderTiming.Settings
    private void DrawTimeSetup()
    {
        FormInputs.SetIndentToParameters();

        // Range
        FormInputs.AddSegmentedButtonWithLabel(ref _timeRange, "Render Range");
        RenderTiming.ApplyTimeRange(_timeRange, ref _timing);

        FormInputs.AddVerticalSpace();

        // Reference switch converts values
        var oldRef = _timing.Reference;
        if (FormInputs.AddSegmentedButtonWithLabel(ref _timing.Reference, "Defined as"))
        {
            _timing.StartInBars = (float)RenderTiming.ConvertReferenceTime(_timing.StartInBars, oldRef, _timing.Reference, _timing.Fps);
            _timing.EndInBars   = (float)RenderTiming.ConvertReferenceTime(_timing.EndInBars,   oldRef, _timing.Reference, _timing.Fps);
        }

        var changed = false;
        changed |= FormInputs.AddFloat($"Start in {_timing.Reference}", ref _timing.StartInBars);
        changed |= FormInputs.AddFloat($"End in {_timing.Reference}",   ref _timing.EndInBars);
        if (changed)
            _timeRange = RenderSettings.TimeRanges.Custom;

        FormInputs.AddVerticalSpace();

        // FPS (also rescales frame-based numbers)
        FormInputs.AddFloat("FPS", ref _timing.Fps, 0);
        if (_timing.Fps < 0) _timing.Fps = -_timing.Fps;
        if (_timing.Fps != 0 && Math.Abs(_lastValidFps - _timing.Fps) > float.Epsilon)
        {
            _timing.StartInBars = (float)RenderTiming.ConvertFps(_timing.StartInBars, _lastValidFps, _timing.Fps);
            _timing.EndInBars   = (float)RenderTiming.ConvertFps(_timing.EndInBars,   _lastValidFps, _timing.Fps);
            _lastValidFps = _timing.Fps;
        }

        _frameCount = RenderTiming.CountFrames(_timing);

        FormInputs.AddFloat("Resolution Factor", ref _resolutionFactor, 0.125f, 4, 0.1f, true, true,
                            "A factor applied to the output resolution of the rendered frames.");

        if (FormInputs.AddInt("Motion Blur Samples", ref _timing.OverrideMotionBlurSamples, -1, 50, 1,
                              "This requires a [RenderWithMotionBlur] operator. Please check its documentation."))
        {
            _timing.OverrideMotionBlurSamples = Math.Clamp(_timing.OverrideMotionBlurSamples, -1, 50);
        }
    }

    // Video options
    private void DrawVideoSettings(Int2 size)
    {
        FormInputs.AddInt("Bitrate", ref _bitrate, 0, 500000000, 1000);

        var startSec = RenderTiming.ReferenceTimeToSeconds(_timing.StartInBars, _timing.Reference, _timing.Fps);
        var endSec   = RenderTiming.ReferenceTimeToSeconds(_timing.EndInBars,   _timing.Reference, _timing.Fps);
        var duration = Math.Max(0, endSec - startSec);

        double bpp = size.Width <= 0 || size.Height <= 0 || _timing.Fps <= 0
                         ? 0
                         : _bitrate / (double)(size.Width * size.Height) / _timing.Fps;

        var q = GetQualityLevelFromRate((float)bpp);
        FormInputs.AddHint($"{q.Title} quality ({_bitrate * duration / 1024 / 1024 / 8:0} MB for {duration / 60:0}:{duration % 60:00}s at {size.Width}×{size.Height})");
        CustomComponents.TooltipForLastItem(q.Description);

        FormInputs.AddFilePicker("File name",
                                 ref UserSettings.Config.RenderVideoFilePath,
                                 ".\\Render\\Title-v01.mp4 ",
                                 null,
                                 "Using v01 in the file name will enable auto incrementation and don't forget the .mp4 extension, I'm serious.",
                                 FileOperations.FilePickerTypes.Folder);

        if (RenderPaths.IsFilenameIncrementable())
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _autoIncrementVersionNumber ? 0.7f : 0.3f);
            FormInputs.AddCheckBox("Increment version after export", ref _autoIncrementVersionNumber);
            ImGui.PopStyleVar();
        }

        FormInputs.AddCheckBox("Export Audio (experimental)", ref _exportAudio);
    }

    // Image sequence options
    private static void DrawImageSequenceSettings()
    {
        FormInputs.AddEnumDropdown(ref _fileFormat, "File Format");

        if (FormInputs.AddStringInput("File name", ref UserSettings.Config.RenderSequenceFileName))
        {
            UserSettings.Config.RenderSequenceFileName = (UserSettings.Config.RenderSequenceFileName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(UserSettings.Config.RenderSequenceFileName))
                UserSettings.Config.RenderSequenceFileName = "output";
        }

        if (ImGui.IsItemHovered())
        {
            CustomComponents.TooltipForLastItem("Base filename for the image sequence (e.g., 'frame' for 'frame_0000.png').\n" +
                                                "Invalid characters (?, |, \", /, \\, :) will be replaced with underscores.\n" +
                                                "If empty, defaults to 'output'.");
        }

        FormInputs.AddFilePicker("Output Folder",
                                 ref UserSettings.Config.RenderSequenceFilePath,
                                 ".\\ImageSequence ",
                                 null,
                                 "Specify the folder where the image sequence will be saved.",
                                 FileOperations.FilePickerTypes.Folder);
    }

    private void DrawRenderingControls(ref Texture2D mainTexture, Int2 size)
    {
        if (!RenderProcess.IsExporting && !IsToollRenderingSomething)
        {
            if (ImGui.Button("Start Render"))
            {
                var targetPath = GetTargetPath();
                if (RenderPaths.ValidateOrCreateTargetFolder(targetPath))
                {
                    SetRenderingStarted();
                    RenderProcess.Start(targetPath,
                                        size,
                                        _renderMode,
                                        _bitrate,
                                        _exportAudio,
                                        _fileFormat,
                                        _timing,
                                        _frameCount);
                }
            }
        }
        else if (RenderProcess.IsExporting)
        {
            bool success = RenderProcess.ProcessFrame(ref mainTexture, size);

            ImGui.ProgressBar((float)RenderProcess.Progress, new Vector2(-1, 16 * T3Ui.UiScaleFactor));

            var effectiveFrameCount = _renderMode == RenderSettings.RenderMode.Video ? RenderProcess.FrameCount : RenderProcess.FrameCount + 2;
            var currentFrame = _renderMode == RenderSettings.RenderMode.Video ? RenderProcess.GetRealFrame() : RenderProcess.FrameIndex + 1;

            var completed = currentFrame >= effectiveFrameCount || !success;
            if (completed)
            {
                RenderProcess.Finish(success, _autoIncrementVersionNumber && _renderMode == RenderSettings.RenderMode.Video);
                RenderingFinished();
            }
            else if (ImGui.Button("Cancel"))
            {
                var elapsed = T3.Core.Animation.Playback.RunTimeInSecs - _exportStartedTimeLocal;
                RenderProcess.Cancel($"Render cancelled after {StringUtils.HumanReadableDurationFromSeconds(elapsed)}");
                RenderingFinished();
            }
        }
    }

    private string GetTargetPath()
    {
        return _renderMode == RenderSettings.RenderMode.Video
                   ? RenderPaths.ResolveProjectRelativePath(UserSettings.Config.RenderVideoFilePath)
                   : RenderPaths.ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath);
    }

    // Minimal “rendering started/finished” bookkeeping kept local to the UI
    private static void SetRenderingStarted()
    {
        IsToollRenderingSomething = true;
        _exportStartedTimeLocal = T3.Core.Animation.Playback.RunTimeInSecs;
    }

    private static void RenderingFinished()
    {
        IsToollRenderingSomething = false;
    }

    // Helpers
    private RenderSettings.QualityLevel GetQualityLevelFromRate(float bitsPerPixelSecond)
    {
        RenderSettings.QualityLevel q = default;
        for (var i = _qualityLevels.Length - 1; i >= 0; i--)
        {
            q = _qualityLevels[i];
            if (q.MinBitsPerPixelSecond < bitsPerPixelSecond)
                break;
        }
        return q;
    }

    internal override List<Window> GetInstances() => new();

    // State (UI)
    public static bool IsToollRenderingSomething { get; private set; }

    private static RenderSettings.RenderMode _renderMode = RenderSettings.RenderMode.Video;
    private static int _bitrate = 25_000_000;
    private static bool _autoIncrementVersionNumber = true;
    private static bool _exportAudio = true;
    private static ScreenshotWriter.FileFormats _fileFormat;
    private static string _lastHelpString = string.Empty;

    // Time state for UI + process
    private static RenderSettings.Settings _timing = new()
                                                       {
                                                           Reference = RenderSettings.TimeReference.Bars,
                                                           StartInBars = 0f,
                                                           EndInBars = 4f,
                                                           Fps = 60f,
                                                           OverrideMotionBlurSamples = -1,
                                                       };

    private static RenderSettings.TimeRanges _timeRange = RenderSettings.TimeRanges.Custom;
    private static float _resolutionFactor = 1f;     // currently UI-only hint
    private static float _lastValidFps = _timing.Fps;

    private static int _frameCount;

    // local UI runtime info
    private static double _exportStartedTimeLocal;

    private readonly RenderSettings.QualityLevel[] _qualityLevels =
        {
            new (0.01, "Poor", "Very low quality. Consider lower resolution."),
            new (0.02, "Low", "Probable strong artifacts"),
            new (0.05, "Medium", "Will exhibit artifacts in noisy regions"),
            new (0.08, "Okay", "Compromise between filesize and quality"),
            new (0.12, "Good", "Good quality. Probably sufficient for YouTube."),
            new (0.5, "Very good", "Excellent quality, but large."),
            new (1, "Reference", "Indistinguishable. Very large files."),
        };
}