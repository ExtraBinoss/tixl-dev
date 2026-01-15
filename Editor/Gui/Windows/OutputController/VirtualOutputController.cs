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

namespace T3.Editor.Gui.Windows.OutputController;

internal sealed class VirtualOutputController : Window
{
    public VirtualOutputController()
    {
        Config.Title = "Virtual Output Controller";
        Config.Visible = true;
        _variationCanvas = new OutputControllerCanvas(this);
    }

    private class ControllerSlot
    {
        public bool IsEmpty = true;
        public string Name = "Empty";
        public Guid SymbolChildId;
        public List<ExplorationVariation.VariationParameter> Parameters = new();
    }

    private readonly ControllerSlot[] _slots = new ControllerSlot[12];
    private int _selectedSlotIndex = -1;
    private readonly OutputControllerCanvas _variationCanvas;
    internal List<ExplorationVariation.VariationParameter> VariationParameters = new();
    public IOutputUi OutputUi { get; set; }
    
    // For handling drag of scatter strength - copied from ExplorationWindow
    private static float _strengthBeforeDrag = 0;

    protected override void DrawContent()
    {
        // Draw Grid of Slots (Decks) - Horizontal Layout
        ImGui.BeginChild("Slots", new Vector2(-1, 80), true); // Fixed height for slots row
        {
            var contentRegion = ImGui.GetContentRegionAvail();
            var columns = 12; // Horizontal row
            var itemWidth = (contentRegion.X - (columns - 1) * ImGui.GetStyle().ItemSpacing.X) / columns;
            var itemHeight = contentRegion.Y;

            for (int i = 0; i < 12; i++)
            {
                if (i > 0) ImGui.SameLine();
                
                var slot = _slots[i] ??= new ControllerSlot();
                var isSelected = _selectedSlotIndex == i;

                ImGui.PushID(i);
                
                // Button labeling
                var label = slot.IsEmpty ? "+" : slot.Name;
                if (!slot.IsEmpty)
                {
                    // Shorten name if too long for the button
                    // label = ...
                }

                if (ImGui.Button(label, new Vector2(itemWidth, itemHeight)))
                {
                    _selectedSlotIndex = i;
                    if (slot.IsEmpty)
                    {
                         // If we have selection, assign.
                         var selection = ProjectView.Focused?.NodeSelection;
                         if (selection != null && selection.Selection.Count > 0)
                         {
                             AssignSelectionToSlot(i);
                         }
                         else
                         {
                             // Open Node Picker
                             ImGui.OpenPopup("NodePicker");
                             _nodeFilter = "";
                         }
                    }
                    else
                    {
                        LoadSlot(i);
                    }
                }
                
                // Node Picker Popup
                if (ImGui.BeginPopup("NodePicker"))
                {
                    DrawNodePicker(i);
                    ImGui.EndPopup();
                }
                
                if (ImGui.IsItemHovered() && !slot.IsEmpty && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                     // Double click could maybe center/fit the canvas?
                }
                
                // Context menu
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.MenuItem("Assign Selected Operator"))
                    {
                        AssignSelectionToSlot(i);
                    }
                    if (ImGui.MenuItem("Clear Slot"))
                    {
                        ClearSlot(i);
                    }
                    ImGui.EndPopup();
                }

                // Selection indicator
                if (isSelected)
                {
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    ImGui.GetWindowDrawList().AddRect(min, max, UiColors.StatusAnimated, 2f, ImDrawFlags.None, 2f);
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        // Bottom Section: Parameters + Canvas
        // Left: Parameters
        ImGui.BeginChild("Parameters", new Vector2(300, -1), true); // Dashboard width
        {
            if (_selectedSlotIndex != -1 && !_slots[_selectedSlotIndex].IsEmpty)
            {
                ImGui.TextUnformatted($"Dashboard: {_slots[_selectedSlotIndex].Name}");
                ImGui.Separator();
                ImGui.Spacing();
                DrawParameters();
            }
            else
            {
                ImGui.TextUnformatted("Select a slot.");
            }
        }
        ImGui.EndChild();
        
        ImGui.SameLine();
        
        // Right: Variation Canvas
        ImGui.BeginChild("Canvas", new Vector2(-1, -1), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar);
        {
            if (_selectedSlotIndex != -1 && !_slots[_selectedSlotIndex].IsEmpty)
            {
                _variationCanvas.Draw(ProjectView.Focused?.Structure);
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
        
        var slot = _slots[index];
        slot.IsEmpty = false;
        slot.Name = symbolChildUi.SymbolChild.ReadableName;
        slot.SymbolChildId = symbolChildUi.Id;
        slot.Parameters.Clear();
        
        // Add all vector/float inputs as parameters by default? 
        // Or maybe just keep it empty effectively letting user add parameters like in ExplorationWindow?
        // For now, let's copy the logic from ExplorationWindow to populate parameters based on selection
        // But since we are "assigning" to a deck, we probably want to snapshot which parameters were selected at that time.
        
        // Actually, better approach: The slot stores the list of parameters.
        // When we load the slot, we populate VariationParameters list which the canvas uses.
        
        // Let's iterate inputs and add them if they are in the selection? 
        // Or if we require specific inputs to be selected...
        // For the "Click and start using" feel, maybe we should auto-add some/all?
        
        // Replicating ExplorationWindow selection logic:
        var selectedSymbolUiChilds = nodeSelection.GetSelectedChildUis();
        // Since we picked the first one for the name, let's assume we focus on that one instance mostly
        // But Exploration supports multi-selection.
        
        // For simplicity in this first version, we stick to the first selected operator.
        
        var instance = ProjectView.Focused?.Structure.GetInstanceFromIdPath(new [] { symbolChildUi.Id }); // Simplified path lookup, likely incorrect for deep nesting
        // Correct way is getting instance from NodeSelection if possible
        var selectedInstance = nodeSelection.GetSelectedInstances().FirstOrDefault();
        
        if (selectedInstance == null) return;
        
        foreach (var input in symbolChildUi.SymbolChild.Inputs.Values)
        {
             var inputUi = symbolChildUi.SymbolChild.Symbol.GetSymbolUi().InputUis[input.Id];
             
             // Check valid types
             if (input.DefaultValue.ValueType == typeof(float) ||
                 input.DefaultValue.ValueType == typeof(Vector2) ||
                 input.DefaultValue.ValueType == typeof(Vector4) ||
                 input.DefaultValue.ValueType == typeof(System.Numerics.Vector3))
             {
                 var inputSlot = selectedInstance.Inputs.Single(i => i.Id == input.Id);
                 
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
                     case Vector4InputUi float4InputUi:
                        scale = 0.02f;
                        break;
                }
                 
                 slot.Parameters.Add(new ExplorationVariation.VariationParameter()
                 {
                    TargetChild = symbolChildUi,
                    Input = input,
                    InstanceIdPath = selectedInstance.InstancePath, // This might be brittle if hierarchy changes
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
        
        _selectedSlotIndex = index;
        LoadSlot(index);
    }
    
    private void DrawNodePicker(int slotIndex)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("Search", ref _nodeFilter, 255);
        
        var structure = ProjectView.Focused?.Structure;
        if (structure == null) return;
        
        var compositionId = ProjectView.Focused.NodeSelection.GetSelectionSymbolChildId(); // Current composition? 
        // Actually we want children of the current composition.
        // ProjectView.Focused.CompositionId is likely what we want if available.
        // Let's use `structure.Instances` or similar if accessible, or traverse from a root.
        
        // Better way: Get the Composition Instance shown in the graph.
        var compositionOp = ProjectView.Focused?.CompositionInstance;
        if (compositionOp == null) 
        {
             ImGui.TextUnformatted("No composition found");
             return;
        }

        ImGui.BeginChild("NodeList", new Vector2(300, 200));
        
        // We need to iterate the children of the current composition
        foreach(var child in compositionOp.Children.Values)
        {
            // Filter
            if (!string.IsNullOrEmpty(_nodeFilter) && !child.Symbol.Name.Contains(_nodeFilter, StringComparison.InvariantCultureIgnoreCase) && !child.SymbolChild.ReadableName.Contains(_nodeFilter, StringComparison.InvariantCultureIgnoreCase))
                continue;

            if (ImGui.Selectable(child.SymbolChild.ReadableName))
            {
                AssignInstanceToSlot(slotIndex, child);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.EndChild();
    }
    
    private void AssignInstanceToSlot(int index, Instance instance)
    {
         var slot = _slots[index];
         slot.IsEmpty = false;
         slot.Name = instance.SymbolChild.ReadableName;
         slot.SymbolChildId = instance.SymbolChildId;
         slot.Parameters.Clear();
         
         // Logic to Populate Parameters from Instance
         PopulateParameters(slot, instance);
         
         _selectedSlotIndex = index;
         LoadSlot(index);
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
                  
                  // Extract UI properties
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
                     case Vector4InputUi float4InputUi:
                        scale = 0.02f;
                        break;
                 }
                 
                 slot.Parameters.Add(new ExplorationVariation.VariationParameter()
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
                slot.Name = "Empty";
                slot.Parameters.Clear();
                slot.SymbolChildId = Guid.Empty;
                
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

    private string _nodeFilter = "";
    
    private void DrawParameters()
    {
         ImGui.DragFloat("Scatter", ref _variationCanvas.Scatter, 0.1f, 0, 100);
         ImGui.Spacing();
         
         // Simplified parameter list
         if (VariationParameters.Count == 0)
         {
             ImGui.TextUnformatted("No parameters.");
             return;
         }

        var nodeSelection = ProjectView.Focused?.NodeSelection;

         // Group by Symbol Child like in ExplorationWindow
         // Since we copied the params, let's just iterate them.
         // Note: Logic to remove params if not selected is removed here because we "saved" the params to the deck.
         // However, we still need to allow toggling/adjusting scatter strength.
         
         foreach (var param in VariationParameters)
         {
             ImGui.PushID(param.Input.Id.GetHashCode());
             
             // Strength control
             var keep = ImGui.GetCursorPos();
             var formattedStrength = $"×{param.ScatterStrength:F1}";
             var size = ImGui.CalcTextSize(formattedStrength);
             ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X- size.X - 5);
             ImGui.TextUnformatted(formattedStrength);
             ImGui.SetCursorPos(keep);
             ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X- 50);
             ImGui.InvisibleButton("ScatterStrengthFactor", new Vector2(50, ImGui.GetTextLineHeight()));
             
             if (ImGui.IsItemActivated())
             {
                 _strengthBeforeDrag = param.ScatterStrength;
             }
             if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
             {
                 param.ScatterStrength = (_strengthBeforeDrag + ImGui.GetMouseDragDelta().X * 0.02f).Clamp(0, 100f);
             }
             if (ImGui.IsItemDeactivated())
             {
                 _variationCanvas.ClearVariations();
             }

             ImGui.SameLine();
             
             // Checkbox to toggle inclusion? 
             // For now just list name
             // ImGui.TextUnformatted($"{param.TargetChild.SymbolChild.ReadableName}.{param.Input.Name}");
             
             // Draw actual parameter control and scatter control side-by-side
             ImGui.PushItemWidth(120);
             if (param.Type == typeof(float))
             {
                 if (param.InputSlot.Input.Value is InputValue<float> floatVal)
                 {
                     var val = floatVal.Value;
                     if (ImGui.DragFloat($"##val_{param.Input.Id}", ref val, param.ParameterScale * 0.1f))
                     {
                         // Update logic would go here
                     }
                 }
             }
             else if (param.Type == typeof(Vector2))
             {
                 if (param.InputSlot.Input.Value is InputValue<Vector2> vec2Val)
                 {
                     var val = vec2Val.Value;
                     ImGui.DragFloat2($"##val_{param.Input.Id}", ref val, param.ParameterScale * 0.1f);
                 }
             }
              else if (param.Type == typeof(System.Numerics.Vector3))
             {
                 if (param.InputSlot.Input.Value is InputValue<System.Numerics.Vector3> vec3Val)
                 {
                     var val = vec3Val.Value;
                     ImGui.DragFloat3($"##val_{param.Input.Id}", ref val, param.ParameterScale * 0.1f);
                 }
             }
              else if (param.Type == typeof(Vector4))
             {
                 if (param.InputSlot.Input.Value is InputValue<Vector4> vec4Val)
                 {
                     var val = vec4Val.Value;
                     ImGui.ColorEdit4($"##val_{param.Input.Id}", ref val, ImGuiColorEditFlags.NoInputs);
                 }
             }
             ImGui.PopItemWidth();
             
             ImGui.SameLine();
             ImGui.TextUnformatted($"{param.Input.Name}");
             
             ImGui.PopID();
         }
    }

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }
}
