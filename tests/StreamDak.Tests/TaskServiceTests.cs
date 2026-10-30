using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.Notion;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Notion contre un faux serveur : authentification, requêtes envoyées, erreurs lisibles, limite de débit.</summary>
public class TaskServiceTests
{
    const string Token = "pk_secret_de_test";

    static IntegrationSettings For(FakeHttpServer server, params (string, string)[] more)
    {
        var values = new Dictionary<string, string> { ["baseurl"] = $"http://localhost:{server.Port}/api" };
        foreach (var (k, v) in more) values[k] = v;
        return new IntegrationSettings { Values = values, Secrets = new Dictionary<string, string> { ["token"] = Token } };
    }

    static SeenRequest Last(FakeHttpServer server, string pathStart) => server.Requests.Last(r => r.PathAndQuery.StartsWith(pathStart, StringComparison.Ordinal));

    // ---------------------------------------------------------------- Notion

    [Fact]
    public async Task NotionAddsRowsAndChangesStatusWithTheConfiguredPropertyNames()
    {
        using var server = new FakeHttpServer();
        server.Handler = r => r.PathAndQuery switch
        {
            "/api/users/me" => (200, """{"object":"user"}"""),
            "/api/pages" => (200, """{"id":"p1"}"""),
            "/api/pages/page-1" => (200, "{}"),
            _ => (404, "{}"),
        };
        using var notion = new NotionIntegration();
        await notion.ConnectAsync(For(server, ("database", "db-1"), ("titleprop", "Nom"), ("statusprop", "Statut")), CancellationToken.None);

        var me = Last(server, "/api/users/me");
        Assert.Equal("Bearer " + Token, me.Headers["Authorization"]);
        Assert.Equal("2022-06-28", me.Headers["Notion-Version"]);

        Assert.Equal("Ligne ajoutée : Réunion lundi", await notion.ExecuteAsync("page", "Réunion lundi", default));
        var created = JsonNode.Parse(Last(server, "/api/pages").Body)!;
        Assert.Equal("db-1", created["parent"]!["database_id"]!.GetValue<string>());
        Assert.Equal("Réunion lundi", created["properties"]!["Nom"]!["title"]![0]!["text"]!["content"]!.GetValue<string>());

        await notion.ExecuteAsync("status", "page-1=Terminé", default);
        var patch = Last(server, "/api/pages/page-1");
        Assert.Equal("PATCH", patch.Method);
        Assert.Equal("Terminé", JsonNode.Parse(patch.Body)!["properties"]!["Statut"]!["status"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task NotionTellsHowLongToWaitWhenRateLimited()
    {
        using var server = new FakeHttpServer();
        int calls = 0;
        server.Handler = r => r.PathAndQuery == "/api/users/me" ? (200, "{}") : (++calls == 1 ? (429, "{}") : (200, "{}"));
        using var notion = new NotionIntegration();
        await notion.ConnectAsync(For(server, ("database", "db")), CancellationToken.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => notion.ExecuteAsync("page", "x", default));
        Assert.Contains("limite le nombre de requêtes", ex.Message);
    }

    [Fact]
    public async Task ARefusedTokenFailsTheConnectionWithoutEchoingTheToken()
    {
        using var server = new FakeHttpServer();
        server.Handler = _ => (401, """{"message":"API token is invalid."}""");
        using var notion = new NotionIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => notion.ConnectAsync(For(server), CancellationToken.None));
        Assert.Contains("refuse le jeton", ex.Message);
        Assert.DoesNotContain(Token, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, notion.Error ?? "", StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Error, notion.State);
    }

    [Fact]
    public async Task ConnectingWithoutATokenAsksForIt()
    {
        using var notion = new NotionIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => notion.ConnectAsync(new IntegrationSettings(), CancellationToken.None));
        Assert.Contains("jeton", ex.Message);
    }

    [Fact]
    public void EveryCommandIsDescribedAndTheirTokensAreSecrets()
    {
        foreach (IIntegration i in new IIntegration[] { new NotionIntegration() })
        {
            Assert.All(i.Commands, c => { Assert.NotEmpty(c.Label); Assert.NotEmpty(c.Description); Assert.True(Enum.TryParse(c.Icon, out Wpf.Ui.Controls.SymbolRegular _), $"{i.Id}.{c.Name} : icône {c.Icon}"); });
            Assert.Equal(SettingKind.Secret, i.Fields.First(f => f.Key == "token").Kind);
            (i as IDisposable)?.Dispose();
        }
    }
}
