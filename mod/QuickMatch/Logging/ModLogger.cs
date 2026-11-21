namespace QuickMatch.Logging;

// The prefix makes the mod's lines in godot.log easy to grep.
public class ModLogger(string prefix)
{
    public void Info(string message) => Godot.GD.Print($"[{prefix}] {message}");

    public void Warn(string message) => Godot.GD.Print($"[{prefix}] WARN {message}");

    public void Error(string message) => Godot.GD.Print($"[{prefix}] ERROR {message}");
}
