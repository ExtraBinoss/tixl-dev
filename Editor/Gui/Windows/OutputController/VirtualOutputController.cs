#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using Vector2 = System.Numerics.Vector2;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Windows.Exploration;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.Windows.OutputController;

/// <summary>
/// A Resolume-style virtual output controller for managing operator variations.
/// </summary>
internal sealed class VirtualOutputController : Window
{
    public VirtualOutputController()
    {
        Config.Title = "Mixer";
        Config.Visible = true;
        _variationCanvas = new OutputControllerCanvas(this);
    }

    private class ControllerSlot
    {
        public bool IsEmpty = true;
        public string Name = "";
        public Guid SymbolChildId;
        public List<ExplorationVariation.VariationParameter> Parameters = new();
        public Instance? LinkedInstance;
    }

    private const int SlotCount = 8;
    private const float DeckRowHeight = 100f;
    private const float DashboardWidth = 350f;
    
    private readonly ControllerSlot[] _slots = new ControllerSlot[SlotCount];
    private int _selectedSlotIndex = -1;
    private readonly OutputControllerCanvas _variationCanvas;
    internal List<ExplorationVariation.VariationParameter> VariationParameters = new();
    public IOutputUi OutputUi { get; set; }
    
    private string _nodeFilter = "";
    private int _pendingNodePickerSlot = -1;

    protected override void DrawContent()
    {
        var windowSize = ImGui.GetContentRegionAvail();
        
        // ============================================
        // TOP: DECK ROW (Horizontal slots like Resolume)
        // ============================================
        DrawDeckRow(windowSize.X);
        
        ImGui.Spacing();
        
        // ============================================
        // BOTTOM: Dashboard + Variation Canvas
        // ============================================
        var remainingHeight = ImGui.GetContentRegionAvail().Y;
        
        // Left: Dashboard Panel
        ImGui.BeginChild("Dashboard", new Vector2(DashboardWidth, remainingHeight), true);
        {
            DrawDashboard();
        }
        ImGui.EndChild();
        
        ImGui.SameLine();
        
        // Right: Variation Canvas (Mixer Grid)
        ImGui.BeginChild("MixerCanvas", new Vector2(-1, remainingHeight), true, 
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar);
        {
            DrawMixerCanvas();
        }
        ImGui.EndChild();
    }

    private void DrawDeckRow(float totalWidth)
    {
        var style = ImGui.GetStyle();
        var slotWidth = (totalWidth - (SlotCount - 1) * style.ItemSpacing.X - style.WindowPadding.X * 2) / SlotCount;
        var slotHeight = DeckRowHeight;
        
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.BeginChild("DeckRow", new Vector2(-1, slotHeight + style.WindowPadding.Y * 2), true);
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (i > 0) ImGui.SameLine();
                DrawSlot(i, slotWidth, slotHeight);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawSlot(int index, float width, float height)
    {
        var slot = _slots[index] ??= new ControllerSlot();
        var isSelected = _selectedSlotIndex == index;
        var drawList = ImGui.GetWindowDrawList();
        
        ImGui.PushID(index);
        
        // Slot background
        var cursorPos = ImGui.GetCursorScreenPos();
        var slotMin = cursorPos;
        var slotMax = cursorPos + new Vector2(width, height);
        
        // Background color based on state
        var bgColor = slot.IsEmpty 
            ? UiColors.BackgroundFull.Fade(0.3f) 
            : (isSelected ? UiColors.BackgroundActive : UiColors.BackgroundFull.Fade(0.6f));
        
        drawList.AddRectFilled(slotMin, slotMax, bgColor, 6f);
        
        // Interactive area
        ImGui.InvisibleButton($"##slot_{index}", new Vector2(width, height));
        
        var isHovered = ImGui.IsItemHovered();
        var wasClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        
        if (wasClicked)
        {
            _selectedSlotIndex = index;
            if (slot.IsEmpty)
            {
                var selection = ProjectView.Focused?.NodeSelection;
                if (selection != null && selection.Selection.Count > 0)
                {
                    AssignSelectionToSlot(index);
                }
                else
                {
                    _pendingNodePickerSlot = index;
                    ImGui.OpenPopup("NodePickerPopup");
                    _nodeFilter = "";
                }
            }
            else
            {
                LoadSlot(index);
            }
        }
        
        // Draw slot content
        if (slot.IsEmpty)
        {
            // Empty slot: Draw "+" icon
            var iconSize = ImGui.CalcTextSize("+");
            var iconPos = slotMin + new Vector2((width - iconSize.X) / 2, (height - iconSize.Y) / 2);
            drawList.AddText(iconPos, isHovered ? UiColors.ForegroundFull : UiColors.TextMuted, "+");
            
            // Hint text
            var hintText = "Add Node";
            var hintSize = ImGui.CalcTextSize(hintText);
            var hintPos = slotMin + new Vector2((width - hintSize.X) / 2, height - hintSize.Y - 8);
            drawList.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, hintPos, UiColors.TextMuted.Fade(0.5f), hintText);
        }
        else
        {
            // Filled slot: Show operator name and param count
            DrawFilledSlotContent(slot, slotMin, width, height, isSelected);
        }
        
        // Selection border
        if (isSelected)
        {
            drawList.AddRect(slotMin, slotMax, UiColors.StatusAnimated, 6f, ImDrawFlags.None, 2f);
        }
        else if (isHovered)
        {
            drawList.AddRect(slotMin, slotMax, UiColors.ForegroundFull.Fade(0.3f), 6f);
        }
        
        // Context menu
        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Assign Selected Operator", slot.IsEmpty || !slot.IsEmpty))
            {
                AssignSelectionToSlot(index);
            }
            if (!slot.IsEmpty && ImGui.MenuItem("Clear Slot"))
            {
                ClearSlot(index);
            }
            ImGui.EndPopup();
        }
        
        ImGui.PopID();
        
        // Node Picker Popup (shared)
        if (_pendingNodePickerSlot == index && ImGui.BeginPopup("NodePickerPopup"))
        {
            DrawNodePicker(index);
            ImGui.EndPopup();
        }
    }

    private void DrawFilledSlotContent(ControllerSlot slot, Vector2 slotMin, float width, float height, bool isSelected)
    {
        var drawList = ImGui.GetWindowDrawList();
        
        // Slot number indicator
        var slotNumber = $"{Array.IndexOf(_slots, slot) + 1}";
        drawList.AddText(slotMin + new Vector2(8, 6), UiColors.TextMuted, slotNumber);
        
        // Operator name (centered, bold)
        ImGui.PushFont(Fonts.FontBold);
        var nameText = slot.Name.Length > 12 ? slot.Name[..10] + "…" : slot.Name;
        var nameSize = ImGui.CalcTextSize(nameText);
        var namePos = slotMin + new Vector2((width - nameSize.X) / 2, 25);
        drawList.AddText(Fonts.FontBold, Fonts.FontBold.FontSize, namePos, 
            isSelected ? UiColors.ForegroundFull : UiColors.Text, nameText);
        ImGui.PopFont();
        
        // Parameter count
        var paramCount = $"{slot.Parameters.Count} params";
        var paramSize = ImGui.CalcTextSize(paramCount);
        var paramPos = slotMin + new Vector2((width - paramSize.X) / 2, height - 20);
        drawList.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, paramPos, UiColors.TextMuted, paramCount);
    }

    private void DrawDashboard()
    {
        if (_selectedSlotIndex == -1 || _slots[_selectedSlotIndex] == null || _slots[_selectedSlotIndex].IsEmpty)
        {
            // Empty state
            var center = ImGui.GetContentRegionAvail() / 2;
            ImGui.SetCursorPos(center - new Vector2(0, 40));
            CustomComponents.EmptyWindowMessage(
                "Select a deck slot above\nto control its parameters.\n\nClick + to add an operator,\nor select one in the graph first.");
            return;
        }

        var slot = _slots[_selectedSlotIndex];
        var instance = slot.LinkedInstance;
        
        if (instance == null)
        {
            CustomComponents.EmptyWindowMessage("Instance not found.\nTry reassigning the operator.");
            return;
        }
        
        // Header
        ImGui.PushFont(Fonts.FontLarge);
        ImGui.TextUnformatted(slot.Name);
        ImGui.PopFont();
        
        ImGui.SameLine();
        CustomComponents.RightAlign(60);
        if (ImGui.SmallButton("Clear"))
        {
            ClearSlot(_selectedSlotIndex);
            return;
        }
        
        CustomComponents.SeparatorLine();
        
        // Scatter control (global for this slot)
        CustomComponents.SmallGroupHeader("VARIATION SCATTER");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("##Scatter", ref _variationCanvas.Scatter, 0, 100, "%.1f");
        CustomComponents.HelpText("Controls how much parameter values vary across the grid.");
        
        ImGui.Spacing();
        CustomComponents.SeparatorLine();
        
        // Parameters - Use the same component as ParameterWindow
        CustomComponents.SmallGroupHeader("PARAMETERS");
        
        ImGui.BeginChild("ParameterList", new Vector2(-1, -1));
        {
            var symbolUi = instance.GetSymbolUi();
            var symbolChildUi = instance.GetChildUi();
            var compositionSymbolUi = instance.Parent?.GetSymbolUi();
            
            if (symbolUi != null && symbolChildUi != null && compositionSymbolUi != null)
            {
                ParameterWindow.DrawParameters(instance, symbolUi, symbolChildUi, compositionSymbolUi, false);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextWrapped("Could not load parameters.");
                ImGui.PopStyleColor();
            }
        }
        ImGui.EndChild();
    }

    private void DrawMixerCanvas()
    {
        if (_selectedSlotIndex == -1 || _slots[_selectedSlotIndex] == null || _slots[_selectedSlotIndex].IsEmpty)
        {
            CustomComponents.EmptyWindowMessage(
                "Variation Mixer\n\nSelect a deck with an assigned operator\nto explore parameter variations.\n\nHover over cells to preview,\nclick to apply permanently.");
            return;
        }

        _variationCanvas.Draw(ProjectView.Focused?.Structure);
    }

    private void DrawNodePicker(int slotIndex)
    {
        ImGui.TextUnformatted("Select Operator");
        ImGui.Separator();
        
        ImGui.SetNextItemWidth(280);
        CustomComponents.DrawInputFieldWithPlaceholder("Search nodes...", ref _nodeFilter, 280);
        
        ImGui.Spacing();
        
        var compositionOp = ProjectView.Focused?.CompositionInstance;
        if (compositionOp == null) 
        {
            ImGui.TextColored(UiColors.StatusError.Rgba, "No composition open");
            return;
        }

        ImGui.BeginChild("NodeList", new Vector2(280, 300));
        {
            foreach (var child in compositionOp.Children.Values)
            {
                var name = child.SymbolChild.ReadableName;
                var symbolName = child.Symbol.Name;
                
                // Filter
                if (!string.IsNullOrEmpty(_nodeFilter))
                {
                    if (!name.Contains(_nodeFilter, StringComparison.InvariantCultureIgnoreCase) &&
                        !symbolName.Contains(_nodeFilter, StringComparison.InvariantCultureIgnoreCase))
                        continue;
                }

                ImGui.PushID(child.SymbolChildId.GetHashCode());
                
                if (ImGui.Selectable(name))
                {
                    AssignInstanceToSlot(slotIndex, child);
                    _pendingNodePickerSlot = -1;
                    ImGui.CloseCurrentPopup();
                }
                
                // Show symbol type as hint
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Type: {symbolName}");
                }
                
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    private void AssignSelectionToSlot(int index)
    {
        var nodeSelection = ProjectView.Focused?.NodeSelection;
        if (nodeSelection == null || nodeSelection.Selection.Count == 0)
            return;
            
        var symbolChildUi = nodeSelection.GetSelectedChildUis().FirstOrDefault();
        if (symbolChildUi == null) return;
        
        var selectedInstance = nodeSelection.GetSelectedInstances().FirstOrDefault();
        if (selectedInstance == null) return;
        
        var slot = _slots[index];
        slot.IsEmpty = false;
        slot.Name = symbolChildUi.SymbolChild.ReadableName;
        slot.SymbolChildId = symbolChildUi.Id;
        slot.LinkedInstance = selectedInstance;
        slot.Parameters.Clear();
        
        PopulateParametersFromInstance(slot, symbolChildUi, selectedInstance);
        
        _selectedSlotIndex = index;
        LoadSlot(index);
    }
    
    private void AssignInstanceToSlot(int index, Instance instance)
    {
        var slot = _slots[index];
        slot.IsEmpty = false;
        slot.Name = instance.SymbolChild.ReadableName;
        slot.SymbolChildId = instance.SymbolChildId;
        slot.LinkedInstance = instance;
        slot.Parameters.Clear();
        
        PopulateParameters(slot, instance);
        
        _selectedSlotIndex = index;
        LoadSlot(index);
    }

    private void PopulateParametersFromInstance(ControllerSlot slot, SymbolUi.Child symbolChildUi, Instance instance)
    {
        foreach (var input in symbolChildUi.SymbolChild.Inputs.Values)
        {
            var inputUi = symbolChildUi.SymbolChild.Symbol.GetSymbolUi().InputUis[input.Id];
            
            if (input.DefaultValue.ValueType == typeof(float) ||
                input.DefaultValue.ValueType == typeof(Vector2) ||
                input.DefaultValue.ValueType == typeof(Vector4) ||
                input.DefaultValue.ValueType == typeof(System.Numerics.Vector3))
            {
                var inputSlot = instance.Inputs.Single(i => i.Id == input.Id);
                
                float scale = 1f;
                float min = float.NegativeInfinity;
                float max = float.PositiveInfinity;
                bool clampMin = false;
                bool clampMax = false;
                
                switch (inputUi)
                {
                    case FloatInputUi floatInputUi:
                        scale = floatInputUi.Scale;
                        min = floatInputUi.Min;
                        max = floatInputUi.Max;
                        clampMin = floatInputUi.ClampMin;
                        clampMax = floatInputUi.ClampMax;
                        break;
                    case Vector2InputUi float2InputUi:
                        scale = float2InputUi.Scale;
                        min = float2InputUi.Min;
                        max = float2InputUi.Max;
                        clampMin = float2InputUi.ClampMin;
                        clampMax = float2InputUi.ClampMax;
                        break;
                    case Vector3InputUi float3InputUi:
                        scale = float3InputUi.Scale;
                        min = float3InputUi.Min;
                        max = float3InputUi.Max;
                        clampMin = float3InputUi.ClampMin;
                        clampMax = float3InputUi.ClampMax;
                        break;
                    case Vector4InputUi:
                        scale = 0.02f;
                        break;
                }
                
                slot.Parameters.Add(new ExplorationVariation.VariationParameter
                {
                    TargetChild = symbolChildUi,
                    Input = input,
                    InstanceIdPath = instance.InstancePath,
                    Type = input.DefaultValue.ValueType,
                    InputSlot = inputSlot,
                    ParameterScale = scale,
                    ParameterMin = min,
                    ParameterMax = max,
                    ParameterClampMin = clampMin,
                    ParameterClampMax = clampMax
                });
            }
        }
    }
    
    private void PopulateParameters(ControllerSlot slot, Instance instance)
    {
        foreach (var input in instance.SymbolChild.Inputs.Values)
        {
            var inputUi = instance.Symbol.GetSymbolUi().InputUis[input.Id];
            
            if (input.DefaultValue.ValueType == typeof(float) ||
                input.DefaultValue.ValueType == typeof(Vector2) ||
                input.DefaultValue.ValueType == typeof(Vector4) ||
                input.DefaultValue.ValueType == typeof(System.Numerics.Vector3))
            {
                var inputSlot = instance.Inputs.Single(i => i.Id == input.Id);
                
                float scale = 1f;
                float min = float.NegativeInfinity;
                float max = float.PositiveInfinity;
                bool clampMin = false;
                bool clampMax = false;
                
                switch (inputUi)
                {
                    case FloatInputUi floatInputUi:
                        scale = floatInputUi.Scale;
                        min = floatInputUi.Min;
                        max = floatInputUi.Max;
                        clampMin = floatInputUi.ClampMin;
                        clampMax = floatInputUi.ClampMax;
                        break;
                    case Vector2InputUi float2InputUi:
                        scale = float2InputUi.Scale;
                        min = float2InputUi.Min;
                        max = float2InputUi.Max;
                        clampMin = float2InputUi.ClampMin;
                        clampMax = float2InputUi.ClampMax;
                        break;
                    case Vector3InputUi float3InputUi:
                        scale = float3InputUi.Scale;
                        min = float3InputUi.Min;
                        max = float3InputUi.Max;
                        clampMin = float3InputUi.ClampMin;
                        clampMax = float3InputUi.ClampMax;
                        break;
                    case Vector4InputUi:
                        scale = 0.02f;
                        break;
                }
                
                slot.Parameters.Add(new ExplorationVariation.VariationParameter
                {
                    TargetChild = instance.Parent.Symbol.GetSymbolUi().ChildUis[instance.SymbolChildId],
                    Input = input,
                    InstanceIdPath = instance.InstancePath,
                    Type = input.DefaultValue.ValueType,
                    InputSlot = inputSlot,
                    ParameterScale = scale,
                    ParameterMin = min,
                    ParameterMax = max,
                    ParameterClampMin = clampMin,
                    ParameterClampMax = clampMax
                });
            }
        }
    }
    
    private void ClearSlot(int index)
    {
        var slot = _slots[index];
        slot.IsEmpty = true;
        slot.Name = "";
        slot.Parameters.Clear();
        slot.SymbolChildId = Guid.Empty;
        slot.LinkedInstance = null;
        
        if (_selectedSlotIndex == index)
        {
            VariationParameters.Clear();
            _variationCanvas.ClearVariations();
        }
    }
    
    private void LoadSlot(int index)
    {
        VariationParameters.Clear();
        VariationParameters.AddRange(_slots[index].Parameters);
        _variationCanvas.ClearVariations();
    }

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }
}
