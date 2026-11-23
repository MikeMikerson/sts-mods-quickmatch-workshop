using Godot;
using QuickMatch.Emotes;
using QuickMatch.Voice;

namespace QuickMatch.Ui;

// The emote button beside the player's own row of the run's player list, right of the mic when voice is on. A
// click opens the picker (EmotePicker); the button fades while the player has to wait before the next emote.
// Shown only in a quick-match run.
internal sealed class EmoteButton
{
    public const float Size = 56f;
    private const float WithMicX = 328f;
    private const float AloneX = 264f;
    private const float Y = 6f;

    private readonly Control _control;
    private SceneTree? _tree;
    private bool _pressed;

    private EmoteButton(string name)
    {
        _control = new Control
        {
            Name = name,
            CustomMinimumSize = new Vector2(Size, Size),
            Size = new Vector2(Size, Size),
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
            Visible = false,
        };
        _control.AddChild(new TextureRect
        {
            Texture = EmoteArt.ButtonIcon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 4,
            OffsetTop = 4,
            OffsetRight = -4,
            OffsetBottom = -4,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _control.GuiInput += OnGuiInput;
        _control.MouseEntered += () => _control.Scale = Vector2.One * 1.1f;
        _control.MouseExited += () => _control.Scale = Vector2.One;
        _control.PivotOffset = new Vector2(Size, Size) / 2;
        _control.TreeEntered += OnTreeEntered;
        _control.TreeExiting += OnTreeExiting;
    }

    public static Control Create(string name) => new EmoteButton(name)._control;

    private void OnTreeEntered()
    {
        _tree = _control.GetTree();
        _tree.ProcessFrame += OnProcessFrame;
    }

    private void OnTreeExiting()
    {
        if (_tree != null)
        {
            _tree.ProcessFrame -= OnProcessFrame;
            _tree = null;
        }
        EmotePicker.Close();
    }

    private void OnProcessFrame()
    {
        bool show = EmoteRoom.Here;
        if (_control.Visible != show)
        {
            _control.Visible = show;
        }
        if (!show)
        {
            return;
        }
        _control.Position = new Vector2(VoiceChat.InMatch ? WithMicX : AloneX, Y);
        _control.Modulate = EmoteRoom.CanSend ? Colors.White : new Color(1, 1, 1, 0.4f);
    }

    private void OnGuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            return;
        }
        if (button.Pressed)
        {
            _pressed = true;
        }
        else if (_pressed)
        {
            _pressed = false;
            if (_control.GetGlobalRect().HasPoint(button.GlobalPosition))
            {
                EmotePicker.Toggle(_control);
            }
        }
        _control.AcceptEvent();
    }
}
