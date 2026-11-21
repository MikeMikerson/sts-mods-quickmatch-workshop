using System.Collections.Generic;
using Godot;

namespace QuickMatch.Voice;

// The game's Master Volume (its slider, the game's start and the fade when muted in the background) sets Godot's
// Master bus, which every Godot sound passes through, so at 0 nothing can be heard. While voice plays, the game's
// buses send through a bus of their own that takes the game's Master Volume instead, and the Master bus stays at
// full for voice. Closed, the layout and the game's volume are back as the game left them.
internal static class GameMasterBus
{
    private const string Name = "QuickMatchGame";
    private const string Master = "Master";
    private static readonly List<string> Moved = new();

    public static void Open()
    {
        if (AudioServer.GetBusIndex(Name) >= 0)
        {
            return;
        }
        AudioServer.AddBus(1);
        AudioServer.SetBusName(1, Name);
        AudioServer.SetBusSend(1, Master);
        for (int i = 2; i < AudioServer.BusCount; i++)
        {
            if (AudioServer.GetBusSend(i).ToString() == Master)
            {
                AudioServer.SetBusSend(i, Name);
                Moved.Add(AudioServer.GetBusName(i));
            }
        }
        Follow();
    }

    // After the game sets its Master Volume: moved onto the game's bus while voice plays.
    public static void Follow()
    {
        int game = AudioServer.GetBusIndex(Name);
        if (game < 0)
        {
            return;
        }
        AudioServer.SetBusVolumeDb(game, AudioServer.GetBusVolumeDb(0));
        AudioServer.SetBusVolumeDb(0, 0f);
    }

    public static void Close()
    {
        int game = AudioServer.GetBusIndex(Name);
        if (game < 0)
        {
            return;
        }
        AudioServer.SetBusVolumeDb(0, AudioServer.GetBusVolumeDb(game));
        foreach (string bus in Moved)
        {
            int index = AudioServer.GetBusIndex(bus);
            if (index >= 0 && AudioServer.GetBusSend(index).ToString() == Name)
            {
                AudioServer.SetBusSend(index, Master);
            }
        }
        Moved.Clear();
        AudioServer.RemoveBus(game);
    }
}
