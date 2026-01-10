#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Core.Utils;

namespace T3.Editor.Gui.Windows.RenderExport;

internal sealed class RenderWindow : Window
{
    public RenderWindow()
    {
        Config.Title = "Render To File";
    }

    protected override void DrawContent()
    {
        FormInputs.AddVerticalSpace(15);
        DrawTimeSetup();
        ImGui.Indent(5);
        DrawInnerContent();
    }

    private void DrawInnerContent()
    {
        if (RenderProcess.State == RenderProcess.States.NoOutputWindow)
        {
            _lastHelpString = "No output view available";
            CustomComponents.HelpText(_lastHelpString);
            return;
        }

        if (RenderProcess.State == RenderProcess.States.NoValidOutputType)
        {
            _lastHelpString = RenderProcess.MainOutputType == null
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
        FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.RenderMode, "Render Mode");

        FormInputs.AddVerticalSpace();

        if (RenderSettings.RenderMode == RenderSettings.RenderModes.Video)
            DrawVideoSettings(RenderProcess.MainOutputOriginalSize);
        else
            DrawImageSequenceSettings();

        FormInputs.AddVerticalSpace(5);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(5);

        DrawRenderSummary();

        DrawRenderingControls();

        CustomComponents.HelpText(RenderProcess.IsExporting ? RenderProcess.LastHelpString : _lastHelpString);
    }

    private static void DrawTimeSetup()
    {
        FormInputs.SetIndentToParameters();

        // Range
        FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.TimeRange, "Render Range");
        RenderTiming.ApplyTimeRange(RenderSettings.TimeRange, RenderSettings);

        FormInputs.AddVerticalSpace();

        // Reference switch converts values
        var oldRef = RenderSettings.Reference;
        if (FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.Reference, "Defined as"))
        {
            RenderSettings.StartInBars =
                (float)RenderTiming.ConvertReferenceTime(RenderSettings.StartInBars, oldRef, RenderSettings.Reference, RenderSettings.Fps);
            RenderSettings.EndInBars = (float)RenderTiming.ConvertReferenceTime(RenderSettings.EndInBars, oldRef, RenderSettings.Reference, RenderSettings.Fps);
        }

        var changed = false;
        changed |= FormInputs.AddFloat($"Start in {RenderSettings.Reference}", ref RenderSettings.StartInBars);
        changed |= FormInputs.AddFloat($"End in {RenderSettings.Reference}", ref RenderSettings.EndInBars);
        if (changed)
            RenderSettings.TimeRange = RenderSettings.TimeRanges.Custom;

        FormInputs.AddVerticalSpace();

        // FPS (also rescales frame-based numbers)
        FormInputs.AddFloat("FPS", ref RenderSettings.Fps, 0);
        if (RenderSettings.Fps < 0) RenderSettings.Fps = -RenderSettings.Fps;
        if (RenderSettings.Fps != 0 && Math.Abs(_lastValidFps - RenderSettings.Fps) > float.Epsilon)
        {
            RenderSettings.StartInBars = (float)RenderTiming.ConvertFps(RenderSettings.StartInBars, _lastValidFps, RenderSettings.Fps);
            RenderSettings.EndInBars = (float)RenderTiming.ConvertFps(RenderSettings.EndInBars, _lastValidFps, RenderSettings.Fps);
            _lastValidFps = RenderSettings.Fps;
        }

        RenderSettings.FrameCount = RenderTiming.ComputeFrameCount(RenderSettings);

        // Resolution as Percentage with Popover
        DrawResolutionPopover();

        // Motion Blur Samples
        if (FormInputs.AddInt("Motion Blur Samples", ref RenderSettings.OverrideMotionBlurSamples, -1, 50, 1,
                              "Number of motion blur samples. Set to -1 to disable. Requires [RenderWithMotionBlur] operator."))
        {
            RenderSettings.OverrideMotionBlurSamples = Math.Clamp(RenderSettings.OverrideMotionBlurSamples, -1, 50);
        }

        // Show hint when motion blur is disabled
        if (RenderSettings.OverrideMotionBlurSamples == -1)
        {
            FormInputs.AddHint("Motion blur is disabled. Add [RenderWithMotionBlur] to your graph and set samples > 0.");
        }
    }

    private static void DrawResolutionPopover()
    {
        var currentPct = (int)(RenderSettings.ResolutionFactor * 100);

        // Use DrawInputLabel for proper alignment with other form inputs
        FormInputs.DrawInputLabel("Resolution");

        CustomComponents.DrawPopover("ResolutionPopover", $"{currentPct}%", () =>
        {
            var shouldClose = false;

            if (ImGui.Selectable("25%", currentPct == 25))
            {
                RenderSettings.ResolutionFactor = 0.25f;
                shouldClose = true;
            }
            if (ImGui.Selectable("50%", currentPct == 50))
            {
                RenderSettings.ResolutionFactor = 0.5f;
                shouldClose = true;
            }
            if (ImGui.Selectable("100%", currentPct == 100))
            {
                RenderSettings.ResolutionFactor = 1.0f;
                shouldClose = true;
            }
            if (ImGui.Selectable("200%", currentPct == 200))
            {
                RenderSettings.ResolutionFactor = 2.0f;
                shouldClose = true;
            }

            CustomComponents.SeparatorLine();

            // Custom input
            ImGui.TextUnformatted("Custom:");
            ImGui.SameLine();
            var customPct = RenderSettings.ResolutionFactor * 100f;
            ImGui.SetNextItemWidth(60 * T3Ui.UiScaleFactor);
            if (ImGui.InputFloat("##CustomRes", ref customPct, 0, 0, "%.0f"))
            {
                customPct = Math.Clamp(customPct, 1f, 400f);
                RenderSettings.ResolutionFactor = customPct / 100f;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("%");

            return shouldClose;
        });
        CustomComponents.TooltipForLastItem("A factor applied to the output resolution of the rendered frames.");
    }

    private void DrawVideoSettings(Int2 size)
    {
        // Bitrate in Mbps
        var bitrateMbps = RenderSettings.Bitrate / 1_000_000f;
        if (FormInputs.AddFloat("Bitrate (Mbps)", ref bitrateMbps, 0.1f, 500f, 0.5f, true, true,
                                "Video bitrate in megabits per second."))
        {
            RenderSettings.Bitrate = (int)(bitrateMbps * 1_000_000f);
        }

        var startSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.StartInBars, RenderSettings.Reference, RenderSettings.Fps);
        var endSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.EndInBars, RenderSettings.Reference, RenderSettings.Fps);
        var duration = Math.Max(0, endSec - startSec);

        double bpp = size.Width <= 0 || size.Height <= 0 || RenderSettings.Fps <= 0
                         ? 0
                         : RenderSettings.Bitrate / (double)(size.Width * size.Height) / RenderSettings.Fps;

        var q = GetQualityLevelFromRate((float)bpp);
        FormInputs.AddHint($"{q.Title} quality ({RenderSettings.Bitrate * duration / 1024 / 1024 / 8:0} MB for {duration / 60:0}:{duration % 60:00}s at {size.Width}×{size.Height})");
        CustomComponents.TooltipForLastItem(q.Description);

        // Split path into Output Folder and File Name
        var currentPath = UserSettings.Config.RenderVideoFilePath ?? "./Render/render-v01.mp4";
        var directory = Path.GetDirectoryName(currentPath) ?? "./Render";
        var filename = Path.GetFileName(currentPath) ?? "render-v01.mp4";

        FormInputs.AddFilePicker("Output Folder",
                                 ref directory!,
                                 ".\\Render",
                                 null,
                                 "Folder where the video will be saved.",
                                 FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput("File Name", ref filename))
        {
            // Sanitize invalid characters
            filename = (filename ?? string.Empty).Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                filename = filename.Replace(c, '_');
            }
            if (string.IsNullOrEmpty(filename))
                filename = "render-v01.mp4";
        }
        CustomComponents.TooltipForLastItem("Video filename. Using 'v01' enables auto-increment.");

        // Ensure .mp4 extension
        if (!filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            filename += ".mp4";

        // Recombine path
        UserSettings.Config.RenderVideoFilePath = Path.Combine(directory, filename);

        if (RenderPaths.IsFilenameIncrementable())
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, RenderSettings.AutoIncrementVersionNumber ? 0.7f : 0.3f);
            FormInputs.AddCheckBox("Increment version after export", ref RenderSettings.AutoIncrementVersionNumber);
            ImGui.PopStyleVar();
        }

        FormInputs.AddCheckBox("Export Audio (experimental)", ref RenderSettings.ExportAudio);
    }

    // Image sequence options
    private static void DrawImageSequenceSettings()
    {
        FormInputs.AddEnumDropdown(ref RenderSettings.FileFormat, "File Format");

        if (FormInputs.AddStringInput("File Name", ref UserSettings.Config.RenderSequenceFileName))
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
                                 ref UserSettings.Config.RenderSequenceFilePath!,
                                 ".\\ImageSequence ",
                                 null,
                                 "Specify the folder where the image sequence will be saved.",
                                 FileOperations.FilePickerTypes.Folder);
    }

    private void DrawRenderSummary()
    {
        var size = RenderProcess.MainOutputOriginalSize;
        var scaledWidth = (int)(size.Width * RenderSettings.ResolutionFactor);
        var scaledHeight = (int)(size.Height * RenderSettings.ResolutionFactor);

        var startSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.StartInBars, RenderSettings.Reference, RenderSettings.Fps);
        var endSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.EndInBars, RenderSettings.Reference, RenderSettings.Fps);
        var duration = Math.Max(0, endSec - startSec);

        string outputPath;
        string format;
        if (RenderSettings.RenderMode == RenderSettings.RenderModes.Video)
        {
            outputPath = UserSettings.Config.RenderVideoFilePath ?? "./Render/render-v01.mp4";
            format = "MP4 Video";
        }
        else
        {
            var folder = UserSettings.Config.RenderSequenceFilePath ?? ".\\ImageSequence";
            var baseName = UserSettings.Config.RenderSequenceFileName ?? "output";
            outputPath = Path.Combine(folder, baseName);
            format = $"{RenderSettings.FileFormat} Sequence";
        }

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);

        ImGui.TextWrapped($"Output: {outputPath}");
        ImGui.TextWrapped($"Format: {format} • {scaledWidth}×{scaledHeight} @ {RenderSettings.Fps:0}fps");
        ImGui.TextWrapped($"Duration: {duration / 60:0}:{duration % 60:00.0}s ({RenderSettings.FrameCount} frames)");

        ImGui.PopStyleColor();
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(5);
    }

    private static void DrawRenderingControls()
    {
        if (!RenderProcess.IsExporting && !RenderProcess.IsToollRenderingSomething)
        {
            if (ImGui.Button("Start Render"))
            {
                RenderProcess.TryStart(RenderSettings);
            }
        }
        else if (RenderProcess.IsExporting)
        {
            var progress = (float)RenderProcess.Progress;
            var elapsed = T3.Core.Animation.Playback.RunTimeInSecs - RenderProcess.ExportStartedTimeLocal;

            // Calculate time remaining
            string timeRemainingStr = "Calculating...";
            if (progress > 0.01)
            {
                var estimatedTotal = elapsed / progress;
                var remaining = estimatedTotal - elapsed;
                timeRemainingStr = StringUtils.HumanReadableDurationFromSeconds(remaining) + " remaining";
            }

            ImGui.ProgressBar(progress, new Vector2(-1, 16 * T3Ui.UiScaleFactor), $"{progress * 100:0}%");

            ImGui.PushFont(Fonts.FontSmall);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(timeRemainingStr);
            ImGui.PopStyleColor();
            ImGui.PopFont();

            if (ImGui.Button("Cancel"))
            {
                RenderProcess.Cancel($"Render cancelled after {StringUtils.HumanReadableDurationFromSeconds(elapsed)}");
            }
        }
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

    internal override List<Window> GetInstances() => [];

    private static string _lastHelpString = string.Empty;
    private static float _lastValidFps = RenderSettings.Fps;
    private static RenderSettings RenderSettings => RenderSettings.Current;

    private readonly RenderSettings.QualityLevel[] _qualityLevels =
        {
            new(0.01, "Poor", "Very low quality. Consider lower resolution."),
            new(0.02, "Low", "Probable strong artifacts"),
            new(0.05, "Medium", "Will exhibit artifacts in noisy regions"),
            new(0.08, "Okay", "Compromise between filesize and quality"),
            new(0.12, "Good", "Good quality. Probably sufficient for YouTube."),
            new(0.5, "Very good", "Excellent quality, but large."),
            new(1, "Reference", "Indistinguishable. Very large files."),
        };
}