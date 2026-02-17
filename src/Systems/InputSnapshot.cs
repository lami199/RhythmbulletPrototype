using Microsoft.Xna.Framework;

namespace RhythmbulletPrototype.Systems;

public readonly record struct InputSnapshot(
    Point MousePosition,
    int MouseWheelDelta,
    bool LeftPressed,
    bool LeftDown,
    bool LeftReleased,
    bool RightPressed,
    bool MiddlePressed);
