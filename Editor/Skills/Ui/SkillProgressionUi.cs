#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Skills.Data;
using T3.Editor.Skills.Training;

namespace T3.Editor.Skills.Ui;

internal static class SkillProgressionUi
{
    public enum ContentModes
    {
        PopUp,
        HubPanel,
    }

    public static void DrawContent(QuestTopic topic, QuestLevel activeLevel, ContentModes mode = ContentModes.PopUp, Action? startAction = null)
    {
        var index = topic.Levels.IndexOf(activeLevel);

        if (ImGui.IsWindowAppearing())
            FocusTopicOnMap(topic);

        if (index < 0)
        {
            CustomComponents.EmptyWindowMessage("Can't find level...");
            return;
        }

        var previousLevel = index > 0 ? topic.Levels[index - 1] : null;

        if (index == topic.Levels.Count
            || (index == topic.Levels.Count - 1
                && (topic.Levels[index].LevelState == SkillProgress.LevelResult.States.Completed
                    || topic.Levels[index].LevelState == SkillProgress.LevelResult.States.Skipped)))
        {
            DrawTopicCompletedContent(topic, index, mode);
        }
        else if (index < topic.Levels.Count)
        {
            DrawNextLevelContent(topic, previousLevel, activeLevel, index, mode, startAction);
        }
        else
        {
            CustomComponents.EmptyWindowMessage("Can't find level...");
        }
    }

    private static void FocusTopicOnMap(QuestTopic topic)
    {
        TopicSelection.Clear();
        TopicSelection.Add(topic);
        _mapCanvas.FocusTopics(TopicSelection);
    }

    private static void DrawNextLevelContent(QuestTopic topic, QuestLevel? previousLevel, QuestLevel nextLevel, int index, ContentModes mode,
                                             Action? startAction)
    {
        var uiScale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();

        var leftWidth = 240 * uiScale;
        var area = ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        area.Expand(-10);
        dl.AddRectFilled(area.Min + new Vector2(leftWidth, 0), area.Max, UiColors.WindowBackground, 7 * uiScale);

        ImGui.BeginChild("Map", new Vector2(leftWidth, 0), false, ImGuiWindowFlags.NoBackground);
        {
            _mapCanvas.DrawContent(null, out _, TopicSelection);
            if (mode == ContentModes.HubPanel && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                SkillMapPopup.Show();
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();

        ImGui.BeginChild("Right", new Vector2(0, 0), false, ImGuiWindowFlags.NoBackground);
        {
            ImGui.BeginChild("TopContent", new Vector2(0, -HeightActionsArea), false,
                             ImGuiWindowFlags.NoBackground);
            {
                FormInputs.AddVerticalSpace(20);
                ImGui.Indent(20 * T3Ui.UiScaleFactor);
                DrawProgressHeader(topic, index, dl);

                if (mode == ContentModes.PopUp)
                {
                    if (previousLevel != null)
                    {
                        CustomComponents.StylizedText("COMPLETED", Fonts.FontSmall, UiColors.Text.Fade(0.3f));
                        CustomComponents.StylizedText(previousLevel.Title, Fonts.FontNormal, UiColors.Text.Fade(0.3f));
                        FormInputs.AddVerticalSpace();
                    }
                }

                CustomComponents.StylizedText("NEXT UP", Fonts.FontSmall, UiColors.Text.Fade(0.3f));

                ImGui.PushFont(Fonts.FontLarge);
                ImGui.TextWrapped(nextLevel.Title);
                ImGui.PopFont();
            }
            ImGui.EndChild();
            
            bool isFirst = index == 0;
            ImGui.BeginChild("Actions2", new Vector2(0, HeightActionsArea), false, ImGuiWindowFlags.NoBackground);
            {
                var indent = 10;
                ImGui.Indent(indent);
                var style = ImGui.GetStyle();
                var btnH = ImGui.GetFrameHeight();
                var wSkip = ImGui.CalcTextSize("Skip").X + style.FramePadding.X * 2;
                var labelCTA = isFirst ? "Start" : "Continue";

                var wCont = ImGui.CalcTextSize(labelCTA).X + style.FramePadding.X * 2;
                var totalW = wSkip + wCont + style.ItemSpacing.X * 2 + indent;

                if (mode == ContentModes.PopUp)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
                    if (ImGui.Button("Back to Hub", Vector2.Zero))
                    {
                        SkillTraining.SaveNewResult(SkillProgress.LevelResult.States.Skipped);
                        SkillTraining.ExitPlayMode();
                    }

                    ImGui.SameLine();
                }

                ImGui.SameLine(ImGui.GetWindowWidth() - totalW - 20);

                if (ImGui.Button("Skip", new Vector2(wSkip, btnH)))
                {
                    SkillTraining.SaveNewResult(SkillProgress.LevelResult.States.Skipped);
                    SkillTraining.UpdateTopicStatesAndProgression();
                }

                ImGui.SameLine(0, 10);
                ImGui.PopStyleColor();

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.45f, 0.95f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.55f, 1.00f, 1f));
                if (ImGui.Button(labelCTA, new Vector2(wCont, btnH)))
                {
                    if (mode == ContentModes.PopUp)
                    {
                        startAction?.Invoke();
                    }
                    else
                    {
                        startAction?.Invoke();
                    }
                }

                ImGui.PopStyleColor(2);
            }
            ImGui.EndChild();
        }
        ImGui.EndChild();
    }

    private static void DrawTopicCompletedContent(QuestTopic topic, int index, ContentModes mode)
    {
        var uiScale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();

        var leftWidth = 240 * uiScale;
        ImGui.BeginChild("Map", new Vector2(leftWidth, 0), false, ImGuiWindowFlags.NoBackground);
        {
            _mapCanvas.DrawContent(null, out _, TopicSelection);
            if (mode == ContentModes.HubPanel && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                SkillMapPopup.Show();
            }            
        }
        ImGui.EndChild();
        ImGui.SameLine();

        var area = ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        area.Expand(-10);
        dl.AddRectFilled(area.Min + new Vector2(leftWidth, 0), area.Max, UiColors.WindowBackground, 7 * uiScale);

        ImGui.SameLine(0, 4);

        var paddingForActions = mode == ContentModes.PopUp ? HeightActionsArea : 0;
        
        ImGui.BeginChild("Right", new Vector2(0, -paddingForActions), false, ImGuiWindowFlags.NoBackground);
        {
            FormInputs.AddVerticalSpace(20);
            ImGui.Indent(20 * T3Ui.UiScaleFactor);

            DrawProgressHeader(topic, index, dl);
            CustomComponents.StylizedText("CONTINUE WITH THESE UNLOCKED TOPICS", Fonts.FontSmall, UiColors.Text.Fade(0.3f));

            FormInputs.AddVerticalSpace(5);
            foreach (var t in SkillMapData.Data.Topics)
            {
                if (t.ProgressionState == QuestTopic.ProgressStates.Unlocked)
                {
                    DrawUnlockedTopicButton(t);
                }
            }

            if (mode == ContentModes.PopUp)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
                if (ImGui.Button("Back to Hub", Vector2.Zero))
                {
                    SkillTraining.SaveNewResult(SkillProgress.LevelResult.States.Skipped);
                    SkillTraining.ExitPlayMode();
                }
                
                ImGui.SameLine(0,10);
                
                if (ImGui.Button("Replay last", Vector2.Zero))
                {
                    SkillTraining.SaveNewResult(SkillProgress.LevelResult.States.Skipped);
                    SkillTraining.ExitPlayMode();
                }

                ImGui.PopStyleColor();
            }
        }
        ImGui.EndChild();
    }

    private static void DrawUnlockedTopicButton(QuestTopic topic)
    {
        SkillMapCanvas.GetTopicColorAndStateFade(topic, out var color, out _);
        ImGui.PushStyleColor(ImGuiCol.Button, color.Fade(0.4f).Rgba);
        var clicked = ImGui.Button(topic.Title, new Vector2(-20, 0));
        ImGui.PopStyleColor();
        if (clicked)
        {
            SkillTraining.StartTopic(topic);
        }
    }

    private static void DrawProgressHeader(QuestTopic topic, int index, ImDrawListPtr dl)
    {
        var label = topic.Title;
        if (topic.ProgressionState == QuestTopic.ProgressStates.Completed)
        {
            label += " - Completed";
        }
        else if (topic.ProgressionState == QuestTopic.ProgressStates.Passed)
        {
            label += " - Passed";
        }

        CustomComponents.StylizedText(label, Fonts.FontNormal, UiColors.Text.Fade(0.3f));
        var countLabel = $"{index }/{topic.Levels.Count}";
        var labelSize = ImGui.CalcTextSize(countLabel);
        ImGui.SameLine(ImGui.GetColumnWidth() - labelSize.X, 0);
        CustomComponents.StylizedText(countLabel, Fonts.FontNormal, UiColors.Text.Fade(0.3f));

        FormInputs.AddVerticalSpace(5);
        var padding = 3;
        var p = ImGui.GetCursorScreenPos();
        var width = (ImGui.GetContentRegionAvail().X - ImGui.GetCursorPosX() + padding) / topic.Levels.Count;
        var blockSize = new Vector2(width - padding, 4);
        for (int levelIndex = 0; levelIndex < topic.Levels.Count; levelIndex++)
        {
            var pp = p + new Vector2(levelIndex * width, 0);
            var tickColor = topic.Levels[levelIndex].LevelState switch
                                {
                                    SkillProgress.LevelResult.States.Undefined => UiColors.ForegroundFull.Fade(0.05f),
                                    SkillProgress.LevelResult.States.Started   => UiColors.ForegroundFull.Fade(0.2f),
                                    SkillProgress.LevelResult.States.Skipped   => UiColors.ForegroundFull.Fade(0.1f),
                                    SkillProgress.LevelResult.States.Completed => UiColors.ForegroundFull.Fade(0.3f),
                                    _                                          => throw new ArgumentOutOfRangeException()
                                };
            if (levelIndex == index)
                tickColor = UiColors.BackgroundActive;

            dl.AddRectFilled(pp, pp + blockSize, tickColor, 2);
        }

        FormInputs.AddVerticalSpace(20);
    }

    private static float HeightActionsArea => 40 * T3Ui.UiScaleFactor;

    public static readonly HashSet<QuestTopic> TopicSelection = [];
    private static readonly SkillMapCanvas _mapCanvas = new();
}