using FFMpegCore.Arguments;

namespace T3.Editor.Gui.Windows.RenderExport.FFMpeg;
/// <summary>
///     Mix channels with specific gain levels.
/// </summary>
public class VolumeArgument : IAudioFilterArgument
{
    private readonly float _volume;

    public VolumeArgument(float volume)
    {
        _volume = volume;
    }

    public string Key { get; } = "volume";

    public string Value => $"{_volume}";
}