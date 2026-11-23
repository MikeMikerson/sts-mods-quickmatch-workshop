using QuickMatch.Transport;
using Api = STS2Mobile.ModApi;

namespace QuickMatch.Platforms.IPhone;

internal sealed class IPhoneRelay(Api.IRelayTransport relay) : IRelayTransport
{
    public IRelaySocket Open(string region, IRelayListener listener) =>
        new Socket(relay.Open(region, new Listener(listener)));

    private sealed class Socket(Api.IRelaySocket socket) : IRelaySocket
    {
        public void Send(byte[] message) => socket.Send(message);
        public void Dispose() => socket.Dispose();
    }

    private sealed class Listener(IRelayListener listener) : Api.IRelayListener
    {
        public void Opened() => listener.Opened();
        public void Received(byte[] message) => listener.Received(message);
        public void Closed(string reason) => listener.Closed(reason);
    }
}
