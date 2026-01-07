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
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Msdfgen.Extensions;

namespace T3.Editor.Gui.Windows.Utilities
{
    public static class MsdfGeneration
    {
        public static void Draw()
        {
            FormInputs.SetIndent(50);
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
                
                // Initialize default selection if needed
                if (_selectedPackage == null || !editablePackages.Contains(_selectedPackage))
                {
                    // Try to use the currently valid project from the focused view
                    var focusedProject = ProjectView.Focused?.OpenedProject.Package;
                    if (focusedProject != null && !focusedProject.IsReadOnly)
                    {
                        _selectedPackage = focusedProject;
                    }
                    else
                    {
                        _selectedPackage = editablePackages.FirstOrDefault();
                    }
                }

                var packageNames = editablePackages.Select(p => p.DisplayName).OrderBy(n => n).ToList();
                string selectedName = _selectedPackage?.DisplayName ?? "";

                if (FormInputs.AddStringInputWithSuggestions("Target Project", ref selectedName, packageNames.AsEnumerable().OrderBy(n => n), "Select Project"))
                {
                    _selectedPackage = editablePackages.FirstOrDefault(p => p.DisplayName == selectedName);
                }
                
                // If the user typed a valid name but we missed it (e.g. didn't hit enter but clicked away, or typed perfectly), try to resolve
                if (_selectedPackage == null || _selectedPackage.DisplayName != selectedName)
                {
                     var match = editablePackages.FirstOrDefault(p => p.DisplayName == selectedName);
                     if (match != null)
                        _selectedPackage = match;
                }

                usagePackage = _selectedPackage;
                
                if (usagePackage == null)
                {
                    ImGui.TextColored(UiColors.StatusError, "Target Project: Invalid or None selected.");
                }
            }

            bool hasFile = !string.IsNullOrEmpty(_fontFilePath) && File.Exists(_fontFilePath);
            if (CustomComponents.DisablableButton("Generate MSDF", hasFile && usagePackage != null))
            {
                Generate(usagePackage!);
            }
            
            if (!hasFile || usagePackage == null)
            {
                string reason = !hasFile ? "Please select a valid .ttf font file." : "Please select a target project.";
                CustomComponents.TooltipForLastItem(reason);
            }
            
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                var color = _isStatusError ? UiColors.StatusError : UiColors.StatusAutomated;
                ImGui.TextColored(color, _statusMessage);
            }
        }

        public static void RefreshSelection()
        {
            _selectedPackage = null;
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

                var settings = GetSettings();
                string outputDir = PrepareOutputDirectory(package);
                string fontName = Path.GetFileNameWithoutExtension(settings.FontPath);
                
                string imageOut = Path.Combine(outputDir, $"{fontName}_msdf.png");
                string fntOut = Path.Combine(outputDir, $"{fontName}_msdf.fnt");

                Log.Debug($"Initializing FreeType for {settings.FontPath}...");
                using var ft = FreetypeHandle.Initialize();
                if (ft == null)
                {
                    Log.Error("Failed to initialize FreeType.");
                    return;
                }

                using var fontHandle = FontHandle.LoadFont(ft, settings.FontPath);
                if (fontHandle == null)
                {
                    Log.Error("Failed to load font.");
                    return;
                }

                var fontGeometry = SetupFontGeometry(fontHandle, fontName, settings);
                var glyphs = fontGeometry.GetGlyphs().Glyphs.ToArray();

                if (!TryPackGlyphs(glyphs, settings, out int finalW, out int finalH))
                    return;

                Log.Debug($"Generating Atlas ({finalW}x{finalH})...");
                var generator = GenerateAtlas(glyphs, finalW, finalH, settings);

                Log.Debug($"Saving Results to {outputDir}...");
                SaveResults(generator, fontGeometry, settings, imageOut, fntOut, finalW, finalH);

                _statusMessage = $"Success! Saved to {Path.GetFileName(imageOut)}";
                _isStatusError = false;
                Log.Debug("MSDF Generation successful!");
            }
            catch (Exception e)
            {
                _statusMessage = $"Error: {e.Message}";
                _isStatusError = true;
                Log.Error($"An error occurred during MSDF generation: {e.Message}");
                Log.Error(e.StackTrace ?? string.Empty);
            }
        }

        private static GenerationSettings GetSettings()
        {
            return new GenerationSettings
                       {
                           FontPath = _fontFilePath ?? string.Empty,
                           FontSize = _useRecommended ? 90.0 : (double)_fontSize,
                           Width = _useRecommended ? 1024 : _width,
                           Height = _useRecommended ? 1024 : _height,
                           MiterLimit = _useRecommended ? 3.0 : (double)_miterLimit,
                           Spacing = _useRecommended ? 2 : _spacing,
                           RangeValue = _useRecommended ? 2.0 : (double)_rangeValue,
                           AngleThreshold = _useRecommended ? 3.0 : (double)_angleThreshold
                       };
        }

        private static string PrepareOutputDirectory(SymbolPackage package)
        {
            string outputDir = Path.Combine(package.ResourcesFolder, "fonts");
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
            return outputDir;
        }

        private static FontGeometry SetupFontGeometry(FontHandle fontHandle, string fontName, GenerationSettings settings)
        {
            var fontGeometry = new FontGeometry();
            var charset = Charset.ASCII;
            fontGeometry.LoadCharset(fontHandle, settings.FontSize, charset);
            fontGeometry.SetName(fontName);

            foreach (var glyph in fontGeometry.GetGlyphs().Glyphs)
            {
                glyph.EdgeColoring(Msdfgen.EdgeColoring.EdgeColoringSimple, settings.AngleThreshold, 0);
            }

            return fontGeometry;
        }

        private static bool TryPackGlyphs(GlyphGeometry[] glyphs, GenerationSettings settings, out int finalW, out int finalH)
        {
            var packer = new TightAtlasPacker();
            packer.SetDimensions(settings.Width, settings.Height);
            packer.SetMiterLimit(settings.MiterLimit);
            packer.SetSpacing(settings.Spacing);
            packer.SetPixelRange(new Msdfgen.Range(settings.RangeValue));

            int packResult = packer.Pack(glyphs);
            if (packResult < 0)
            {
                Log.Error("Packing failed!");
                finalW = 0;
                finalH = 0;
                return false;
            }

            packer.GetDimensions(out finalW, out finalH);
            return true;
        }

        private static ImmediateAtlasGenerator<float> GenerateAtlas(GlyphGeometry[] glyphs, int width, int height, GenerationSettings settings)
        {
            var generatorConfig = new MSDFGeneratorConfig(true,
                                                          new ErrorCorrectionConfig(ErrorCorrectionConfig.DistanceErrorCorrectionMode.INDISCRIMINATE,
                                                                                      ErrorCorrectionConfig.DistanceCheckMode.CHECK_DISTANCE_ALWAYS));

            var generator = new ImmediateAtlasGenerator<float>(width, height, (bitmap, glyph, attrs) =>
            {
                var proj = glyph.GetBoxProjection();
                var gRange = glyph.GetBoxRange();
                MsdfGenerator.GenerateMSDF(bitmap, glyph.GetShape()!, proj, gRange, generatorConfig);
            }, 3);

            generator.SetThreadCount(Environment.ProcessorCount);
            generator.Generate(glyphs);
            return generator;
        }

        private static void SaveResults(ImmediateAtlasGenerator<float> generator, FontGeometry fontGeometry, GenerationSettings settings, string imageOut, string fntOut, int finalW, int finalH)
        {
            ImageSaver.Save(generator.AtlasStorage.Bitmap, imageOut);

            var metrics = fontGeometry.GetMetrics();
            FntExporter.Export(new[] { fontGeometry },
                               ImageType.Msdf,
                               finalW, finalH,
                               settings.FontSize,
                               settings.RangeValue, // distanceRange is rangeValue based on previous code
                               Path.GetFileName(imageOut),
                               fntOut,
                               metrics,
                               YAxisOrientation.Upward,
                               new MsdfAtlasGen.Padding(0, 0, 0, 0),
                               settings.Spacing);
        }

        private struct GenerationSettings
        {
            public string FontPath;
            public double FontSize;
            public int Width;
            public int Height;
            public double MiterLimit;
            public int Spacing;
            public double RangeValue;
            public double AngleThreshold;
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
