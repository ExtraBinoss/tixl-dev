#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Msdfgen;
using MsdfAtlasGen;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Msdfgen.Extensions;

namespace T3.Editor.Gui.Windows.Utilities
{
    public static class MsdfGeneration
    {
        public static void Draw()
        {
            FormInputs.AddSectionHeader("MSDF Generation");
            CustomComponents.HelpText("Generate MSDF fonts from .ttf files using MSDF-Sharp.\nChecks for 'Resources/fonts' and outputs there.");
            FormInputs.AddVerticalSpace();

            FormInputs.AddFilePicker("Font File", ref _fontFilePath, null, null, "Select .ttf file", FileOperations.FilePickerTypes.File);

            FormInputs.AddCheckBox("Use Recommended Settings", ref _useRecommended);

            if (!_useRecommended)
            {
                FormInputs.SetIndent(120);
                FormInputs.AddFloat("Size", ref _fontSize, 1, 500, 1);
                FormInputs.AddInt("Width", ref _width, 128, 4096, 128);
                FormInputs.AddInt("Height", ref _height, 128, 4096, 128);
                FormInputs.AddFloat("Miter Limit", ref _miterLimit, 0, 10, 0.1f);
                FormInputs.AddInt("Spacing", ref _spacing, 0, 32, 1);
                FormInputs.AddFloat("Range", ref _rangeValue, 0.1f, 10, 0.1f);
                FormInputs.AddFloat("Angle Threshold", ref _angleThreshold, 0, 6, 0.1f);
                FormInputs.ApplyIndent();
            }

            FormInputs.AddVerticalSpace();

            FormInputs.AddVerticalSpace();

            var internalPackage = GetPackageContainingPath(_fontFilePath);
            SymbolPackage? usagePackage = null;

            if (internalPackage != null)
            {
                // Lock to the package containing the file
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"Target Project: {internalPackage.DisplayName}");
                usagePackage = internalPackage;
            }
            else
            {
                // Allow selection for external files
                var editablePackages = SymbolPackage.AllPackages.Where(p => !p.IsReadOnly).OrderBy(p => p.DisplayName).ToList();
                
                if (_selectedPackage == null || !editablePackages.Contains(_selectedPackage))
                {
                   _selectedPackage = editablePackages.FirstOrDefault();
                }

                if (_selectedPackage != null)
                {
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (FormInputs.AddDropdown(ref _selectedPackage, editablePackages, "Target Project", p => p.DisplayName))
                    {
                        // Selection handled by helper
                    }
                    usagePackage = _selectedPackage;
                }
                else
                {
                    ImGui.TextColored(UiColors.StatusError, "Target Project: No editable projects found to save to.");
                }
            }

            bool hasFile = !string.IsNullOrEmpty(_fontFilePath) && File.Exists(_fontFilePath);
            if (CustomComponents.DisablableButton("Generate MSDF", hasFile && usagePackage != null))
            {
                Generate(usagePackage!);
            }
            
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                var color = _isStatusError ? UiColors.StatusError : UiColors.StatusAutomated;
                ImGui.TextColored(color, _statusMessage);
            }
        }

        private static void Generate(SymbolPackage package)
        {
            _statusMessage = "";
            _isStatusError = false;
            try
            {
                if (string.IsNullOrEmpty(_fontFilePath) || !File.Exists(_fontFilePath))
                {
                    _statusMessage = "Font file not found";
                    _isStatusError = true;
                    Log.Warning("Font file not found: " + _fontFilePath);
                    return;
                }

                string fontPath = _fontFilePath;
                // Use defaults if recommended is checked
                // Defaults: -size 90 -dimensions 1024 1024 -spacing 2 -miterlimit 3.0 -range 2.0 -angle 3.0
                double fontSize = _useRecommended ? 90.0 : (double)_fontSize;
                int width = _useRecommended ? 1024 : _width;
                int height = _useRecommended ? 1024 : _height;
                double miterLimit = _useRecommended ? 3.0 : (double)_miterLimit;
                int spacing = _useRecommended ? 2 : _spacing;
                double rangeValue = _useRecommended ? 2.0 : (double)_rangeValue;
                double angleThreshold = _useRecommended ? 3.0 : (double)_angleThreshold;

                var range = new Msdfgen.Range(rangeValue);

                string outputDir = Path.Combine(package.ResourcesFolder, "fonts");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                string fontName = Path.GetFileNameWithoutExtension(fontPath);
                string imageOut = Path.Combine(outputDir, $"{fontName}_msdf.png");
                string fntOut = Path.Combine(outputDir, $"{fontName}_msdf.fnt");

                Log.Debug($"Initializing FreeType for {fontPath}...");
                using var ft = FreetypeHandle.Initialize();
                if (ft == null)
                {
                    Log.Error("Failed to initialize FreeType.");
                    return;
                }

                using var fontHandle = FontHandle.LoadFont(ft, fontPath);
                if (fontHandle == null)
                {
                    Log.Error("Failed to load font.");
                    return;
                }

                var fontGeometry = new FontGeometry();
                var charset = Charset.ASCII;
                fontGeometry.LoadCharset(fontHandle, fontSize, charset);
                fontGeometry.SetName(fontName);

                foreach (var glyph in fontGeometry.GetGlyphs().Glyphs)
                {
                    glyph.EdgeColoring(Msdfgen.EdgeColoring.EdgeColoringSimple, angleThreshold, 0);
                }

                var glyphs = fontGeometry.GetGlyphs().Glyphs.ToArray();
                var packer = new TightAtlasPacker();
                packer.SetDimensions(width, height);
                packer.SetMiterLimit(miterLimit);
                packer.SetSpacing(spacing);
                packer.SetPixelRange(range);

                int packResult = packer.Pack(glyphs);
                if (packResult < 0)
                {
                    Log.Error("Packing failed!");
                    return;
                }

                packer.GetDimensions(out int finalW, out int finalH);
                Log.Debug($"Packed Dimensions: {finalW}x{finalH}");

                // Generator Config
                // -coloringstrategy simple (already done in loop above)
                // -errorcorrection indiscriminate
                var generatorConfig = new MSDFGeneratorConfig(true,
                    new ErrorCorrectionConfig(
                        ErrorCorrectionConfig.DistanceErrorCorrectionMode.INDISCRIMINATE,
                        ErrorCorrectionConfig.DistanceCheckMode.CHECK_DISTANCE_ALWAYS
                    )
                );

                var generator = new ImmediateAtlasGenerator<float>(finalW, finalH, (bitmap, glyph, attrs) =>
                {
                    var proj = glyph.GetBoxProjection();
                    var gRange = glyph.GetBoxRange();
                    MsdfGenerator.GenerateMSDF(bitmap, glyph.GetShape()!, proj, gRange, generatorConfig);
                }, 3);

                generator.SetThreadCount(Environment.ProcessorCount);
                generator.Generate(glyphs);

                Log.Debug($"Saving Atlas to {imageOut}...");
                ImageSaver.Save(generator.AtlasStorage.Bitmap, imageOut);

                Log.Debug($"Exporting FNT to {fntOut}...");
                var metrics = fontGeometry.GetMetrics();
                double distanceRange = range.Upper - range.Lower;

                // FntExporter.Export parameters might vary depending on exact version, but based on user snippet:
                FntExporter.Export(
                    new[] { fontGeometry },
                    ImageType.Msdf,
                    finalW, finalH,
                    fontSize,
                    distanceRange,
                    Path.GetFileName(imageOut), // Texture filename in FNT (relative)
                    fntOut,
                    metrics,
                    YAxisOrientation.Upward,
                    new MsdfAtlasGen.Padding(0, 0, 0, 0),
                    spacing
                );

                _statusMessage = $"Success! Saved to {Path.GetFileName(imageOut)}";
                _isStatusError = false;
                Log.Debug("MSDF Generation successful!");
            }
            catch (Exception e)
            {
                _statusMessage = $"Error: {e.Message}";
                _isStatusError = true;
                Log.Error($"An error occurred during MSDF generation: {e.Message}");
                Log.Error(e.StackTrace);
            }
        }

        private static SymbolPackage? GetPackageContainingPath(string? fontPath)
        {
            if (string.IsNullOrEmpty(fontPath))
                return null;

            return SymbolPackage.AllPackages.FirstOrDefault(p => fontPath.Contains(p.Folder) || fontPath.Contains(p.ResourcesFolder));
        }

        private static string? _fontFilePath = "";
        private static SymbolPackage? _selectedPackage;
        private static string _statusMessage = "";
        private static bool _isStatusError = false;
        private static bool _useRecommended = true;
        private static float _fontSize = 90;
        private static int _width = 1024;
        private static int _height = 1024;
        private static float _miterLimit = 3.0f;
        private static int _spacing = 2;
        private static float _rangeValue = 2.0f;
        private static float _angleThreshold = 3.0f;
    }
}
