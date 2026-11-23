using System;

namespace QuickMatch.Transport;

/// <summary>
/// A binary WebSocket to the QuickMatch relay (see the relay's README), opened by the platform: on
/// desktop and in the Android launcher QuickMatch.Platforms.Relay (from QuickMatch.cfg), in the iPhone
/// app the app itself, each in <see cref="RelayTransport.Current"/>. The rest of the mod opens no
/// network connections, so it also runs where code mods may not (the iPhone app compiles mods in,
/// removes the platform code and refuses network use in what is left).
/// </summary>
public interface IRelayTransport
{
    /// <summary>
    /// Starts connecting to the relay's <c>/v1/relay/&lt;region&gt;</c> address and returns at once;
    /// everything that happens to the connection goes to <paramref name="listener"/>.
    /// <paramref name="region"/> is <c>auto</c> or a region name the relay reported.
    /// </summary>
    IRelaySocket Open(string region, IRelayListener listener);
}

/// <summary>
/// What happens to one relay connection. Called on any thread, in order, and never after the socket
/// was disposed or <see cref="Closed"/> was called.
/// </summary>
public interface IRelayListener
{
    /// <summary>The connection is open and binary messages can be sent.</summary>
    void Opened();

    /// <summary>One whole binary message from the relay.</summary>
    void Received(byte[] message);

    /// <summary>The connection could not be opened, or ended. Called once; nothing follows it.</summary>
    void Closed(string reason);
}

/// <summary>One relay connection. QuickMatch calls it from the game's thread only.</summary>
public interface IRelaySocket : IDisposable
{
    /// <summary>Queues one binary message. Messages are sent in order; ignored once closed.</summary>
    void Send(byte[] message);
}

/// <summary>
/// The relay connection QuickMatch uses, or null for none: matches then keep to lobby chat. Set by a platform before the first match.
/// </summary>
public static class RelayTransport
{
    public static IRelayTransport? Current { get; set; }
}
