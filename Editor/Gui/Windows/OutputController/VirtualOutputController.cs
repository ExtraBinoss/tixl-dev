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
using T3.Editor.Gui.InputUi;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Core.DataTypes;
using T3.Core.Resource;

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
        
        // Set larger initial window size for better breathing room
        ImGui.SetNextWindowSize(new Vector2(900, 650), ImGuiCond.FirstUseEver);
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
    private const float DeckRowHeight = 125f;  // Balanced height for preview + text
    private const float MinDashboardWidth = 250f;
    private const float MaxDashboardWidth = 500f;
    private const float DefaultDashboardWidth = 380f;
    private const float SplitterWidth = 6f;
    private const float ContentPadding = 16f;  // Increased for more breathing room
    
    private readonly ControllerSlot[] _slots = new ControllerSlot[SlotCount];
    private int _selectedSlotIndex = -1;
    private readonly OutputControllerCanvas _variationCanvas;
    internal List<ExplorationVariation.VariationParameter> VariationParameters = new();
    public IOutputUi OutputUi { get; set; }
    
    private string _nodeFilter = "";
    private int _pendingNodePickerSlot = -1;
    private float _dashboardWidth = DefaultDashboardWidth;
    private bool _isDraggingSplitter = false;

    protected override void DrawContent()
    {
        var windowSize = ImGui.GetContentRegionAvail();
        
        // Global breathing room
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ContentPadding);
        ImGui.Indent(ContentPadding);
        
        // ============================================
        // TOP: DECK ROW (Horizontal slots like Resolume)
        // ============================================
        DrawDeckRow(windowSize.X - ContentPadding * 2);
        
        ImGui.Unindent(ContentPadding);
        ImGui.Dummy(new Vector2(0, ContentPadding)); // Spacer between sections
        ImGui.Indent(ContentPadding);
        
        // ============================================
        // BOTTOM: Dashboard + Resizable Splitter + Variation Canvas
        // ============================================
        var remainingHeight = ImGui.GetContentRegionAvail().Y;
        
        // Left: Dashboard Panel
        ImGui.BeginChild("Dashboard", new Vector2(_dashboardWidth, remainingHeight), true);
        {
            DrawDashboard();
        }
        ImGui.EndChild();
        
        // Splitter (resizable divider)
        ImGui.SameLine(0, 0);
        DrawHorizontalSplitter(remainingHeight);
        
        ImGui.SameLine(0, 0);
        
        // Right: Variation Canvas (Mixer Grid) - takes remaining width minus right padding
        var canvasWidth = windowSize.X - _dashboardWidth - SplitterWidth - ContentPadding * 2;
        ImGui.BeginChild("MixerCanvas", new Vector2(canvasWidth, remainingHeight), true, 
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar);
        {
            DrawMixerCanvas();
        }
        ImGui.EndChild();
        
        ImGui.Unindent(ContentPadding);
    }
    
    private void DrawHorizontalSplitter(float height)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        
        // Splitter button area
        ImGui.InvisibleButton("##HSplitter", new Vector2(SplitterWidth, height));
        
        var isHovered = ImGui.IsItemHovered();
        var isActive = ImGui.IsItemActive();
        
        // Handle dragging
        if (isActive && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _isDraggingSplitter = true;
            _dashboardWidth += ImGui.GetIO().MouseDelta.X;
            _dashboardWidth = Math.Clamp(_dashboardWidth, MinDashboardWidth, MaxDashboardWidth);
        }
        else if (_isDraggingSplitter && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _isDraggingSplitter = false;
        }
        
        // Visual feedback
        var splitterColor = isActive 
            ? UiColors.StatusAnimated 
            : (isHovered ? UiColors.ForegroundFull.Fade(0.5f) : UiColors.BackgroundGaps);
        
        // Draw splitter bar (thinner visual indicator in the center)
        var barWidth = 2f;
        var barX = cursorPos.X + (SplitterWidth - barWidth) / 2;
        drawList.AddRectFilled(
            new Vector2(barX, cursorPos.Y + 4), 
            new Vector2(barX + barWidth, cursorPos.Y + height - 4), 
            splitterColor, 
            1f);
        
        // Change cursor on hover
        if (isHovered || isActive)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }
    }

    private void DrawDeckRow(float totalWidth)
    {
        var style = ImGui.GetStyle();
        var internalPadding = ContentPadding;  // Internal left padding
        var adjustedWidth = totalWidth - internalPadding * 2;
        var slotWidth = (adjustedWidth - (SlotCount - 1) * style.ItemSpacing.X) / SlotCount;
        var slotHeight = DeckRowHeight;
        
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(internalPadding, internalPadding));
        ImGui.BeginChild("DeckRow", new Vector2(totalWidth, slotHeight + internalPadding * 2), true);
        {
            // Ensure content starts after the padding
            ImGui.SetCursorPos(new Vector2(internalPadding, internalPadding));
            
            for (int i = 0; i < SlotCount; i++)
            {
                if (i > 0) ImGui.SameLine();
                DrawSlot(i, slotWidth, slotHeight);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
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
            : (isSelected ? UiColors.StatusAutomated.Fade(0.4f) : UiColors.BackgroundFull.Fade(0.6f));
        
        drawList.AddRectFilled(slotMin, slotMax, bgColor, 8f);
        
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
            // Empty slot: Draw "+" icon (centered)
            var iconSize = ImGui.CalcTextSize("+");
            var iconPos = slotMin + new Vector2((width - iconSize.X) / 2, (height - iconSize.Y) / 2 - 12);
            drawList.AddText(iconPos, isHovered ? UiColors.ForegroundFull : UiColors.TextMuted, "+");
            
            // Hint text (at bottom)
            var hintText = "Add Operator";
            var hintSize = ImGui.CalcTextSize(hintText);
            var hintPos = slotMin + new Vector2((width - hintSize.X) / 2, height - hintSize.Y - 18);
            drawList.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, hintPos, UiColors.TextMuted.Fade(0.4f), hintText);
        }
        else
        {
            // Filled slot: Show operator name and param count
            DrawFilledSlotContent(slot, slotMin, width, height, isSelected);
        }
        
        // Selection border
        if (isSelected)
        {
            drawList.AddRect(slotMin, slotMax, UiColors.StatusAutomated, 8f, ImDrawFlags.None, 4f);
        }
        else if (isHovered)
        {
            drawList.AddRect(slotMin, slotMax, UiColors.StatusAutomated.Fade(0.3f), 8f, ImDrawFlags.None, 2f);
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
        var instance = slot.LinkedInstance;
        
        // Slot number indicator (top left corner)
        var slotNumber = $"{Array.IndexOf(_slots, slot) + 1}";
        drawList.AddText(slotMin + new Vector2(8, 8), UiColors.TextMuted, slotNumber);
        
        // Try to render live texture preview
        var hasPreview = false;
        var footerHeight = 24f;
        var topMargin = 8f;
        
        if (instance != null && instance.Outputs.Count > 0)
        {
            var firstOutput = instance.Outputs[0];
            if (firstOutput is Slot<Texture2D> textureSlot && textureSlot.Value != null && !textureSlot.Value.IsDisposed)
            {
                var texture = textureSlot.Value;
                var previewSrv = SrvManager.GetSrvForTexture(texture);
                
                if (previewSrv != null)
                {
                    hasPreview = true;
                    
                    var interiorMargin = 14f; 
                    var nameLabelHeight = 28f;
                    
                    var maxPreviewWidth = width - interiorMargin * 2;
                    var maxPreviewHeight = height - interiorMargin - nameLabelHeight;
                    
                    var textureAspect = (float)texture.Description.Width / texture.Description.Height;
                    var previewWidth = maxPreviewWidth;
                    var previewHeight = previewWidth / textureAspect;
                    
                    if (previewHeight > maxPreviewHeight)
                    {
                        previewHeight = maxPreviewHeight;
                        previewWidth = previewHeight * textureAspect;
                    }
                    
                    // Center the preview within the available area above the name
                    var previewX = slotMin.X + (width - previewWidth) / 2;
                    var previewY = slotMin.Y + interiorMargin + (maxPreviewHeight - previewHeight) / 2;
                    
                    var pMin = new Vector2(previewX, previewY);
                    var pMax = pMin + new Vector2(previewWidth, previewHeight);
                    
                    // Draw the texture preview
                    drawList.AddImage((IntPtr)previewSrv, pMin, pMax);
                    
                    // Selection highlight border on preview (subtle)
                    if (isSelected)
                    {
                        drawList.AddRect(pMin, pMax, UiColors.StatusAutomated.Fade(0.3f), 4f, ImDrawFlags.None, 1f);
                    }
                }
            }
        }
        
        // If no preview, show operator name centered
        if (!hasPreview)
        {
            ImGui.PushFont(Fonts.FontBold);
            var nameText = slot.Name.Length > 12 ? slot.Name[..10] + "…" : slot.Name;
            var nameSize = ImGui.CalcTextSize(nameText);
            var namePos = slotMin + new Vector2((width - nameSize.X) / 2, (height - nameSize.Y) / 2 - 10);
            drawList.AddText(Fonts.FontBold, Fonts.FontBold.FontSize, namePos, 
                isSelected ? UiColors.ForegroundFull : UiColors.Text, nameText);
            ImGui.PopFont();
        }
        
        // Operator name at bottom (always shown)
        var bottomName = slot.Name.Length > 14 ? slot.Name[..12] + "…" : slot.Name;
        var bottomSize = ImGui.CalcTextSize(bottomName);
        var bottomPos = slotMin + new Vector2((width - bottomSize.X) / 2, height - bottomSize.Y - 8);
        drawList.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, bottomPos, 
            isSelected ? UiColors.ForegroundFull : UiColors.TextMuted, bottomName);
    }

    private void DrawDashboard()
    {
        if (_selectedSlotIndex == -1 || _slots[_selectedSlotIndex] == null || _slots[_selectedSlotIndex].IsEmpty)
        {
            // Show operator list when no slot is selected
            DrawOperatorList();
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
    
    private void DrawOperatorList()
    {
        ImGui.PushFont(Fonts.FontLarge);
        ImGui.TextUnformatted("Operators in Graph");
        ImGui.PopFont();
        
        CustomComponents.SeparatorLine();
        CustomComponents.HelpText("Click an operator to assign it to the selected deck slot, or select a slot above first.");
        
        var compositionOp = ProjectView.Focused?.CompositionInstance;
        if (compositionOp == null)
        {
            CustomComponents.EmptyWindowMessage("No composition open");
            return;
        }
        
        ImGui.BeginChild("OperatorListScroll", new Vector2(-1, -1));
        {
            var drawList = ImGui.GetWindowDrawList();
            var itemHeight = 28f;
            
            foreach (var child in compositionOp.Children.Values)
            {
                var name = child.SymbolChild.ReadableName;
                var symbol = child.Symbol;
                
                // Get the operator's color from its primary output type
                var operatorColor = UiColors.TextMuted;
                if (symbol.OutputDefinitions.Count > 0)
                {
                    operatorColor = TypeUiRegistry.GetPropertiesForType(symbol.OutputDefinitions[0]?.ValueType).Color;
                }
                
                ImGui.PushID(child.SymbolChildId.GetHashCode());
                
                var cursorPos = ImGui.GetCursorScreenPos();
                var itemWidth = ImGui.GetContentRegionAvail().X;
                
                // Draw color indicator bar
                drawList.AddRectFilled(
                    cursorPos, 
                    cursorPos + new Vector2(4, itemHeight - 4), 
                    operatorColor, 
                    2f);
                
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
                
                if (ImGui.Selectable($"{name}##op", false, ImGuiSelectableFlags.None, new Vector2(itemWidth - 12, itemHeight - 4)))
                {
                    // If a slot is selected, assign to it; otherwise assign to first empty slot
                    var targetSlot = _selectedSlotIndex >= 0 ? _selectedSlotIndex : FindFirstEmptySlot();
                    if (targetSlot >= 0)
                    {
                        AssignInstanceToSlot(targetSlot, child);
                    }
                }
                
                // Show symbol type on hover
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Type: {symbol.Name}");
                }
                
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }
    
    private int FindFirstEmptySlot()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] == null || _slots[i].IsEmpty)
                return i;
        }
        return -1;
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
