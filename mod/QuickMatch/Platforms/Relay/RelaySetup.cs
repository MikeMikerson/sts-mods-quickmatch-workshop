using System;
using System.IO;
using QuickMatch.Transport;

namespace QuickMatch.Platforms.Relay;

internal static class RelaySetup
{
    public static void Install()
    {
        string? folder = Path.GetDirectoryName(typeof(RelaySetup).Assembly.Location);
        if (folder == null)
        {
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(folder, RelaySettings.FileName));
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MainFile.Logger.Warn($"could not read {RelaySettings.FileName}: {e.Message}; the fast link stays off");
            return;
        }

        RelaySettings settings = RelaySettings.Parse(text, MainFile.Logger.Warn);
        if (settings.Enabled && settings.Relay is { } address)
        {
            RelayTransport.Current = new WebSocketRelayTransport(address);
        }
    }
}
