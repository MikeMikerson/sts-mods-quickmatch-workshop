using System.Reflection;
using QuickMatch.Steam;
using QuickMatch.Transport;
using Xunit;

namespace QuickMatch.Tests;

public class TransportTests
{
    // Players meet only when their game version and both lobby protocol numbers match. A release that can play with
    // these releases keeps the numbers and joins the list; one that cannot moves them (LobbyKeys) and starts a new list.
    private const string Game = "0.111.1";
    private const string Proto = "25";
    private const string ResumeProto = "24";
    private const ushort ChatProtocol = 20;
    private static readonly string[] ReleasesThatPlayTogether = ["v0.31.0"];

    [Fact]
    public void TheManifestsReleaseCarriesTheProtocolNumbersOfThoseItPlaysWith()
    {
        string manifest = typeof(TransportTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "QuickMatchManifest").Value!;
        var root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest)).RootElement;
        string version = root.GetProperty("version").GetString()!;

        Assert.True(ReleasesThatPlayTogether.Contains(version), $"QuickMatch.json says {version}: list it here if it plays with {string.Join(", ", ReleasesThatPlayTogether)}, or move the protocol numbers");
        Assert.Equal(Game, root.GetProperty("min_game_version").GetString());
        Assert.Equal((Proto, ResumeProto, ChatProtocol), (LobbyKeys.ProtoValue, LobbyKeys.ResumeProtoValue, ChatHostCore.Protocol));
    }
}
