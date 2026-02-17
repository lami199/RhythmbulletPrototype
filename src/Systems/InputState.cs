using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace RhythmbulletPrototype.Systems;

public sealed class InputState
{
    private KeyboardState _prevKeyboard;
    private KeyboardState _currKeyboard;
    private MouseState _prevMouse;
    private MouseState _currMouse;

    public Point MousePosition => _currMouse.Position;
    public int MouseWheelDelta => _currMouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

    public void Update()
    {
        _prevKeyboard = _currKeyboard;
        _currKeyboard = Keyboard.GetState();
        _prevMouse = _currMouse;
        _currMouse = Mouse.GetState();
    }

    public bool IsKeyDown(Keys key) => _currKeyboard.IsKeyDown(key);

    public bool IsKeyPressed(Keys key) => _currKeyboard.IsKeyDown(key) && !_prevKeyboard.IsKeyDown(key);

    public bool LeftPressed => _currMouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
    public bool LeftDown => _currMouse.LeftButton == ButtonState.Pressed;
    public bool LeftReleased => _currMouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

    public bool RightPressed => _currMouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

    public bool MiddlePressed => _currMouse.MiddleButton == ButtonState.Pressed && _prevMouse.MiddleButton == ButtonState.Released;

    public InputSnapshot CreateSnapshot()
    {
        return new InputSnapshot(
            _currMouse.Position,
            _currMouse.ScrollWheelValue - _prevMouse.ScrollWheelValue,
            LeftPressed,
            LeftDown,
            LeftReleased,
            RightPressed,
            MiddlePressed);
    }
}
