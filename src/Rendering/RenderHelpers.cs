using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RhythmbulletPrototype.Rendering;

public sealed class RenderHelpers : IDisposable
{
    private readonly Texture2D _pixel;
    private readonly Texture2D _circle;

    public RenderHelpers(GraphicsDevice graphicsDevice)
    {
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _circle = CreateCircleTexture(graphicsDevice, 128);
    }

    public Texture2D Pixel => _pixel;

    public void DrawRect(SpriteBatch spriteBatch, Rectangle rect, Color color)
    {
        spriteBatch.Draw(_pixel, rect, color);
    }

    public void DrawCircleFilled(SpriteBatch spriteBatch, Vector2 center, float radius, Color color)
    {
        var size = radius * 2f;
        var rect = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)size, (int)size);
        spriteBatch.Draw(_circle, rect, color);
    }

    public void DrawCircleOutline(SpriteBatch spriteBatch, Vector2 center, float radius, float thickness, Color color)
    {
        const int segments = 24;
        var prev = center + new Vector2(radius, 0f);

        for (var i = 1; i <= segments; i++)
        {
            var t = i / (float)segments;
            var angle = t * MathHelper.TwoPi;
            var next = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            DrawLine(spriteBatch, prev, next, thickness, color);
            prev = next;
        }
    }

    public void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, float thickness, Color color)
    {
        var edge = end - start;
        var angle = MathF.Atan2(edge.Y, edge.X);
        var length = edge.Length();

        spriteBatch.Draw(
            _pixel,
            position: start,
            sourceRectangle: null,
            color: color,
            rotation: angle,
            origin: new Vector2(0f, 0.5f),
            scale: new Vector2(length, thickness),
            effects: SpriteEffects.None,
            layerDepth: 0f);
    }

    private static Texture2D CreateCircleTexture(GraphicsDevice graphicsDevice, int size)
    {
        var texture = new Texture2D(graphicsDevice, size, size);
        var data = new Color[size * size];
        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        var radius = size * 0.5f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var idx = y * size + x;
                var p = new Vector2(x, y);
                var dist = Vector2.Distance(center, p);
                var alpha = Math.Clamp(1f - ((dist - radius + 2f) / 2f), 0f, 1f);
                var a = (int)(alpha * 255);
                data[idx] = new Color(a, a, a, a);
            }
        }

        texture.SetData(data);
        return texture;
    }

    public void Dispose()
    {
        _circle.Dispose();
        _pixel.Dispose();
    }
}
