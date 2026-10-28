using Godot;

namespace ArcPrefabs.Core;

/// <summary>Input actions, registered in code so the bindings live next to their defaults.</summary>
public static class Controls
{
    public const string Left = "move_left";
    public const string Right = "move_right";
    public const string Up = "move_up";
    public const string Down = "move_down";
    public const string ZoomIn = "zoom_in";
    public const string ZoomOut = "zoom_out";

    public static void Register()
    {
        Add(Left, Key.A, Key.Left);
        Add(Right, Key.D, Key.Right);
        Add(Up, Key.W, Key.Up);
        Add(Down, Key.S, Key.Down);
        Add(ZoomIn, Key.Equal, Key.KpAdd);
        InputMap.ActionAddEvent(ZoomIn, new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp });
        Add(ZoomOut, Key.Minus, Key.KpSubtract);
        InputMap.ActionAddEvent(ZoomOut, new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown });
    }

    /// <summary>The pan keys as a ground-plane direction (x right, y toward the camera).</summary>
    public static Vector2 Move() => Input.GetVector(Left, Right, Up, Down, 0.2f);

    private static void Add(string action, Key key, Key alt)
    {
        if (InputMap.HasAction(action))
            InputMap.EraseAction(action);
        InputMap.AddAction(action, 0.2f);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = alt });
    }
}
