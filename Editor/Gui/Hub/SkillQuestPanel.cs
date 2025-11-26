#nullable enable

using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Skills;
using T3.Editor.Skills.Data;
using T3.Editor.Skills.Ui;
using SkillTraining = T3.Editor.Skills.Training.SkillTraining;

namespace T3.Editor.Gui.Hub;

internal static class SkillQuestPanel
{
    internal static void Draw(GraphWindow window, bool projectViewJustClosed)
    {
        if (!UserSettings.Config.ShowSkillQuestInHub)
            return;
        
        if (!SkillTraining.TryGetActiveTopicAndLevel(out var activeTopic, out var activeLevel))
        {
            ImGui.TextUnformatted("no skill quest data");
            return;
        }

        if (projectViewJustClosed)
        {
            _selectedTopic.Clear();
            if (activeTopic.ProgressionState == QuestTopic.ProgressStates.Completed)
            {
                foreach (var topic in SkillMapData.Data.Topics)
                {
                    if (topic.ProgressionState == QuestTopic.ProgressStates.Completed
                        || topic.ProgressionState == QuestTopic.ProgressStates.Unlocked
                        || topic.ProgressionState == QuestTopic.ProgressStates.Passed)
                        _selectedTopic.Add(topic);
                }
            }
            else
            {
                _selectedTopic.Add(activeTopic);
            }
            _mapCanvas.FocusToActiveTopics(_selectedTopic);
            
            // Only selected active
            _selectedTopic.Clear();
            _selectedTopic.Add(activeTopic);
        }

        ContentPanel.Begin("Skill Quest", 
                           "An interactive journey from playful TiXL basics to advanced real-time graphics design.", 
                           DrawIcons, Height);
        {
            FormInputs.AddVerticalSpace(5);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Rgba);
            ImGui.BeginChild("Map", new Vector2(200, 0), false);
            _mapCanvas.DrawContent(HandleTopicInteraction2, out _, _selectedTopic);
            ImGui.EndChild();
            ImGui.PopStyleColor();

            ImGui.SameLine(0, 0);

            ImGui.BeginGroup();
            {
                ImGui.BeginChild("Content", new Vector2(-10, -30), false);
                {
                    ImGui.Indent(10);
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.TextUnformatted(activeTopic.Title);
                    ImGui.PopStyleColor();
                    ImGui.PopFont();

                    ImGui.Text(activeLevel.Title);
                    ImGui.Unindent();
                }
                ImGui.EndChild();

                ImGui.BeginChild("actions",new Vector2(-10, 0));
                {
                    ImGui.Button("Skip");
                    ImGui.SameLine(0, 10);
                    if (ImGui.Button("Start"))
                    {
                        SkillTraining.StartPlayModeFromHub(window);
                    }
                }
                ImGui.EndChild();
            }
            ImGui.EndGroup();
        }

        ContentPanel.End();
    }

    
    
    private static void HandleTopicInteraction2(QuestTopic topic, bool isSelected)
    {
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            SkillProgress.Data.ActiveTopicId = topic.Id;
            SkillProgress.SaveUserData();
            _selectedTopic.Clear();
            _selectedTopic.Add(topic);
            SkillTraining.UpdateTopicStatesAndProgression();
        }
    }
    
    private static void DrawIcons()
    {
        ImGui.Dummy(new Vector2());
        // ImGui.Button("New Project");
        // ImGui.SameLine(0, 10);
        //
        // Icon.AddFolder.DrawAtCursor();
    }

    internal static float Height => 220 * T3Ui.UiScaleFactor;

    private static readonly HashSet<QuestTopic> _selectedTopic = [];
    private static readonly SkillMapCanvas _mapCanvas = new();
}