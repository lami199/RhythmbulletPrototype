using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RhythmbulletPrototype.Utils;

public sealed class VirtualViewport
{
    private readonly GraphicsDevice _graphicsDevice;

    public VirtualViewport(GraphicsDevice graphicsDevice, int virtualWidth, int virtualHeight)
    {
        _graphicsDevice = graphicsDevice;
        VirtualWidth = virtualWidth;
        VirtualHeight = virtualHeight;
        Refresh();
    }

    public int VirtualWidth { get; }
    public int VirtualHeight { get; }

    public float Scale { get; private set; }
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }

    public Matrix Matrix => Matrix.CreateTranslation(OffsetX, OffsetY, 0f) * Matrix.CreateScale(Scale, Scale, 1f);

    public void Refresh()
    {
        var pp = _graphicsDevice.PresentationParameters;
        var width = pp.BackBufferWidth;
        var height = pp.BackBufferHeight;

        Scale = MathF.Min(width / (float)VirtualWidth, height / (float)VirtualHeight);
        OffsetX = (int)((width - (VirtualWidth * Scale)) * 0.5f);
        OffsetY = (int)((height - (VirtualHeight * Scale)) * 0.5f);
    }

    public Vector2 ScreenToVirtual(Point point)
    {
        var x = (point.X - OffsetX) / Scale;
        var y = (point.Y - OffsetY) / Scale;

        return new Vector2(
            Math.Clamp(x, 0f, VirtualWidth),
            Math.Clamp(y, 0f, VirtualHeight));
    }
}
