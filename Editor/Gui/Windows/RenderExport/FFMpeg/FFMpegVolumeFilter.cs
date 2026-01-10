using FFMpegCore.Arguments;

namespace T3.Editor.Gui.Windows.RenderExport.FFMpeg;
/// <summary>
///     Changes the volume of the audio.
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