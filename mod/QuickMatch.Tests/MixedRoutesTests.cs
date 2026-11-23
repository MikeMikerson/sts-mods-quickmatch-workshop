using QuickMatch.Lobby;
using QuickMatch.Transport;
using Xunit;

namespace QuickMatch.Tests;

// A desktop host's traffic with a desktop client on Steam sockets and a phone on lobby chat at once.
public class MixedRoutesTests
{
    private const ulong HostId = 76561198000000102;
    private const ulong DesktopId = 76561198000000002;
    private const ulong PhoneId = 76561198000000101;


    [Fact]
    public async Task EachClientStaysOnItsOwnConnection()
    {
        var steam = new FakeSteam();
        FakeBackend hostBackend = steam.Member(HostId);
        var hostEvents = new HostEvents();
        var chat = new ChatHostCore(hostBackend, hostEvents, () => steam.Now, 2222);
        ulong lobby = (await hostBackend.CreateAsync(LobbyKind.Public, 4, default))!.Value;
        chat.Start(lobby);
        var sockets = new FakeSockets { Peers = { DesktopId } };
        var routes = new MixedRoutes(sockets, chat);
        steam.OnTick(routes.Update);

        var phoneEvents = new PhoneEvents();
        var phone = new ChatClientCore(steam.Member(PhoneId), phoneEvents, () => steam.Now, 3333);
        steam.OnTick(phone.Update);
        ushort? failure = await phone.ConnectAsync(lobby, _ => { steam.Advance(50); return Task.CompletedTask; }, default);
        Assert.Null(failure);
        phone.BeginDelivery();
        Assert.Equal(new[] { PhoneId }, hostEvents.Connected);
        Assert.Equal(new[] { DesktopId, PhoneId }, routes.Peers);

        routes.Send(DesktopId, [1], 1, reliable: true, channel: 0);
        routes.Send(PhoneId, [2], 1, reliable: true, channel: 0);
        routes.SendAll([3], 1, reliable: true, channel: 0);
        steam.Advance(2000);

        Assert.Equal(new[] { (DesktopId, (byte)1), (DesktopId, (byte)3) }, sockets.Sent);
        Assert.Equal(new byte[] { 2, 3 }, phoneEvents.Received.Select(m => m[0]));
        Assert.True(sockets.Updates > 0);

        routes.Disconnect(PhoneId, ChatReasons.Quit, now: false);
        routes.Disconnect(DesktopId, ChatReasons.Quit, now: true);
        steam.Advance(2000);

        Assert.Equal(new[] { ChatReasons.Quit }, phoneEvents.Disconnected);
        Assert.Equal(new[] { (DesktopId, ChatReasons.Quit, true) }, sockets.Disconnected);
        Assert.Empty(routes.Peers);
    }

    private sealed class FakeSockets : ISocketsHost
    {
        public List<ulong> Peers { get; } = new();

        public List<(ulong Peer, byte First)> Sent { get; } = new();

        public List<(ulong Peer, ushort Reason, bool Now)> Disconnected { get; } = new();

        public int Updates { get; private set; }

        IEnumerable<ulong> ISocketsHost.Peers => Peers;

        public void Send(ulong peer, byte[] bytes, int length, bool reliable, int channel) => Sent.Add((peer, bytes[0]));

        public void Disconnect(ulong peer, ushort reason, bool now)
        {
            Disconnected.Add((peer, reason, now));
            Peers.Remove(peer);
        }

        public void Update() => Updates++;
    }

    private sealed class HostEvents : IChatHostEvents
    {
        public List<ulong> Connected { get; } = new();

        public void PeerConnected(ulong peer) => Connected.Add(peer);

        public void PeerDisconnected(ulong peer, ushort reason, bool selfInitiated)
        {
        }

        public void Data(ulong peer, byte[] data, bool reliable, byte channel)
        {
        }
    }

    private sealed class PhoneEvents : IChatClientEvents
    {
        public List<ushort> Disconnected { get; } = new();

        public List<byte[]> Received { get; } = new();

        void IChatClientEvents.Disconnected(ushort reason) => Disconnected.Add(reason);

        void IChatClientEvents.Data(byte[] data, bool reliable, byte channel) => Received.Add(data);
    }
}
