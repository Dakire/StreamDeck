using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.Twitch;
using Xunit;

namespace StreamDak.Tests;

public class TwitchTests
{
    const string Token = "jeton-twitch-de-test";

    static IntegrationSettings For(FakeHttpServer server, string clientId = "client-123") => new()
    {
        Values = new Dictionary<string, string> { ["baseurl"] = $"http://localhost:{server.Port}/helix", ["clientid"] = clientId },
        Secrets = new Dictionary<string, string> { ["token"] = Token },
    };

    static (int, string) Helix(SeenRequest r) => r.PathAndQuery.Split('?')[0] switch
    {
        "/helix/users" => (200, """{"data":[{"id":"42","login":"guillaume"}]}"""),
        "/helix/clips" => (202, """{"data":[{"id":"c1","edit_url":"https://clips.twitch.tv/c1/edit"}]}"""),
        "/helix/streams/markers" => (200, """{"data":[{"id":"m1"}]}"""),
        "/helix/channels/commercial" => (200, """{"data":[{"length":60,"message":"","retry_after":480}]}"""),
        "/helix/channels" => (204, ""),
        "/helix/chat/messages" => (200, """{"data":[{"message_id":"x","is_sent":true}]}"""),
        _ => (404, """{"message":"inconnu"}"""),
    };

    [Fact]
    public async Task ConnectionSendsTheClientIdAndTheBearerTokenAndNamesTheChannel()
    {
        using var server = new FakeHttpServer { Handler = Helix };
        using var twitch = new TwitchIntegration();
        await twitch.ConnectAsync(For(server), CancellationToken.None);

        var users = server.Requests.Last();
        Assert.Equal("Bearer " + Token, users.Headers["Authorization"]);
        Assert.Equal("client-123", users.Headers["Client-Id"]);
        Assert.Equal("guillaume", twitch.Values["channel"]);
    }

    [Fact]
    public async Task CommandsCallTheRightEndpointsWithTheChannelIdentity()
    {
        using var server = new FakeHttpServer { Handler = Helix };
        using var twitch = new TwitchIntegration();
        await twitch.ConnectAsync(For(server), CancellationToken.None);

        Assert.Contains("https://clips.twitch.tv/c1/edit", (await twitch.ExecuteAsync("clip", null, default))!);
        Assert.Equal("/helix/clips?broadcaster_id=42", server.Requests.Last().PathAndQuery);

        await twitch.ExecuteAsync("marker", "Moment fort", default);
        var marker = JsonNode.Parse(server.Requests.Last().Body)!;
        Assert.Equal("42", marker["user_id"]!.GetValue<string>());
        Assert.Equal("Moment fort", marker["description"]!.GetValue<string>());

        Assert.Equal("Publicité de 60 s", await twitch.ExecuteAsync("ad", "60", default));
        Assert.Equal(60, JsonNode.Parse(server.Requests.Last().Body)!["length"]!.GetValue<int>());

        await twitch.ExecuteAsync("title", "Soirée détente", default);
        var patch = server.Requests.Last();
        Assert.Equal("PATCH", patch.Method);
        Assert.Equal("/helix/channels?broadcaster_id=42", patch.PathAndQuery);
        Assert.Equal("Soirée détente", JsonNode.Parse(patch.Body)!["title"]!.GetValue<string>());

        await twitch.ExecuteAsync("chat", "Bonjour le chat", default);
        var chat = JsonNode.Parse(server.Requests.Last().Body)!;
        Assert.Equal("42", chat["sender_id"]!.GetValue<string>());
        Assert.Equal("Bonjour le chat", chat["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task DefaultAdLengthIs30AndInvalidOnesAreRefusedBeforeAnyRequest()
    {
        using var server = new FakeHttpServer { Handler = Helix };
        using var twitch = new TwitchIntegration();
        await twitch.ConnectAsync(For(server), CancellationToken.None);
        int before = server.Requests.Count;

        await twitch.ExecuteAsync("ad", null, default);
        Assert.Equal(30, JsonNode.Parse(server.Requests.Last().Body)!["length"]!.GetValue<int>());
        before = server.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => twitch.ExecuteAsync("ad", "45", default));
        await Assert.ThrowsAsync<ArgumentException>(() => twitch.ExecuteAsync("ad", "long", default));
        await Assert.ThrowsAsync<ArgumentException>(() => twitch.ExecuteAsync("title", " ", default));
        await Assert.ThrowsAsync<ArgumentException>(() => twitch.ExecuteAsync("stream", null, default));
        Assert.Equal(before, server.Requests.Count);
    }

    [Fact]
    public async Task MissingClientIdAndRefusedTokenGiveClearMessages()
    {
        using var server = new FakeHttpServer { Handler = Helix };
        using var noClient = new TwitchIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => noClient.ConnectAsync(For(server, ""), CancellationToken.None));
        Assert.Contains("identifiant client", ex.Message);

        server.Handler = _ => (401, """{"error":"Unauthorized","status":401,"message":"Invalid OAuth token"}""");
        using var refused = new TwitchIntegration();
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => refused.ConnectAsync(For(server), CancellationToken.None));
        Assert.Contains("refuse le jeton", denied.Message);
        Assert.DoesNotContain(Token, denied.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADroppedChatMessageIsReportedNotSilentlyIgnored()
    {
        using var server = new FakeHttpServer
        {
            Handler = r => r.PathAndQuery.StartsWith("/helix/chat", StringComparison.Ordinal)
                ? (200, """{"data":[{"is_sent":false,"drop_reason":{"code":"msg_duplicate","message":"Message en double"}}]}""")
                : Helix(r),
        };
        using var twitch = new TwitchIntegration();
        await twitch.ConnectAsync(For(server), CancellationToken.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => twitch.ExecuteAsync("chat", "salut", default));
        Assert.Contains("Message en double", ex.Message);
    }
}
