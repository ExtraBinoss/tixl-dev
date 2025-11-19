namespace Skills.T001_Welcome;

[Guid("468cafa2-d5ef-46f3-b0b7-626cdb0322cf")]
internal sealed class L03_DragFromStack : Instance<L03_DragFromStack>
{
    [Output(Guid = "1fd4183f-2c57-430c-8c06-9c001b635e02")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}