using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.ClickUp;
using StreamDak.Integrations.Monday;
using StreamDak.Integrations.Notion;
using Xunit;

namespace StreamDak.Tests;

/// <summary>ClickUp, Notion et monday contre un faux serveur : authentification, requêtes envoyées, erreurs lisibles, limite de débit.</summary>
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

    // ---------------------------------------------------------------- ClickUp

    [Fact]
    public async Task ClickUpCreatesTasksChangesStatusAndDrivesTheTimer()
    {
        using var server = new FakeHttpServer();
        server.Handler = r => r.PathAndQuery switch
        {
            "/api/user" => (200, """{"user":{"id":1}}"""),
            "/api/list/901/task" => (200, """{"id":"abc","name":"Appeler le client"}"""),
            "/api/task/86abc" => (200, "{}"),
            "/api/team/555/time_entries/start" => (200, "{}"),
            "/api/team/555/time_entries/stop" => (200, "{}"),
            _ => (404, """{"err":"Route not found"}"""),
        };
        using var clickup = new ClickUpIntegration();
        await clickup.ConnectAsync(For(server, ("list", "901"), ("team", "555")), CancellationToken.None);

        Assert.Equal(Token, Last(server, "/api/user").Headers["Authorization"]);   // jeton ClickUp : sans « Bearer »
        Assert.Equal("Tâche créée : Appeler le client", await clickup.ExecuteAsync("task", "Appeler le client", default));
        Assert.Equal("Appeler le client", JsonNode.Parse(Last(server, "/api/list/901/task").Body)!["name"]!.GetValue<string>());

        Assert.Equal("Statut « en cours »", await clickup.ExecuteAsync("status", "86abc=en cours", default));
        var put = Last(server, "/api/task/86abc");
        Assert.Equal("PUT", put.Method);
        Assert.Equal("en cours", JsonNode.Parse(put.Body)!["status"]!.GetValue<string>());

        await clickup.ExecuteAsync("timer-start", "86abc", default);
        Assert.Equal("86abc", JsonNode.Parse(Last(server, "/api/team/555/time_entries/start").Body)!["tid"]!.GetValue<string>());
        await clickup.ExecuteAsync("timer-stop", null, default);
        Assert.Equal("POST", Last(server, "/api/team/555/time_entries/stop").Method);
    }

    [Fact]
    public async Task ClickUpExplainsMissingSettingsBadArgumentsAndRefusals()
    {
        using var server = new FakeHttpServer();
        server.Handler = r => r.PathAndQuery == "/api/user" ? (200, "{}") : (401, """{"err":"Token invalid"}""");
        using var clickup = new ClickUpIntegration();
        await clickup.ConnectAsync(For(server), CancellationToken.None);   // ni liste ni équipe renseignées

        Assert.Contains("liste par défaut", (await Assert.ThrowsAsync<InvalidOperationException>(() => clickup.ExecuteAsync("task", "x", default))).Message);
        Assert.Contains("espace de travail", (await Assert.ThrowsAsync<InvalidOperationException>(() => clickup.ExecuteAsync("timer-stop", null, default))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => clickup.ExecuteAsync("status", "sans-egal", default));
        await Assert.ThrowsAsync<ArgumentException>(() => clickup.ExecuteAsync("task", " ", default));
        await Assert.ThrowsAsync<ArgumentException>(() => clickup.ExecuteAsync("dance", null, default));

        server.Handler = _ => (401, """{"err":"Token invalid"}""");
        using var refused = new ClickUpIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => refused.ConnectAsync(For(server, ("list", "1")), CancellationToken.None));
        Assert.Contains("refuse le jeton", ex.Message);
        Assert.Equal(ConnectionState.Error, refused.State);
    }

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

    // ---------------------------------------------------------------- monday

    [Fact]
    public async Task MondaySendsGraphQlWithVariablesNeverInlinedValues()
    {
        using var server = new FakeHttpServer();
        server.Handler = r =>
        {
            var query = JsonNode.Parse(r.Body)?["query"]?.GetValue<string>() ?? "";
            return query.Contains("me {", StringComparison.Ordinal) ? (200, """{"data":{"me":{"id":"7"}}}""") : (200, """{"data":{"x":{"id":"1"}}}""");
        };
        using var monday = new MondayIntegration();
        await monday.ConnectAsync(For(server, ("board", "123")), CancellationToken.None);
        Assert.Equal(MondayIntegration.ApiVersion, Last(server, "/api/v2").Headers["API-Version"]);

        const string hostile = "x\") { id } } mutation { delete_board(board_id: 1) { id";
        Assert.Contains("Élément créé", (await monday.ExecuteAsync("item", hostile, default))!);
        var body = JsonNode.Parse(server.Requests.Last().Body)!;
        Assert.DoesNotContain("delete_board", body["query"]!.GetValue<string>(), StringComparison.Ordinal);   // la valeur n'est que dans les variables
        Assert.Equal(hostile, body["variables"]!["name"]!.GetValue<string>());
        Assert.Equal("123", body["variables"]!["board"]!.GetValue<string>());

        await monday.ExecuteAsync("status", "456=En cours", default);
        var status = JsonNode.Parse(server.Requests.Last().Body)!["variables"]!;
        Assert.Equal("456", status["item"]!.GetValue<string>());
        Assert.Equal("En cours", status["value"]!.GetValue<string>());
        Assert.Equal("status", status["column"]!.GetValue<string>());
    }

    [Fact]
    public async Task MondayGraphQlErrorsComeBackWithA200AndAreReported()
    {
        using var server = new FakeHttpServer();
        server.Handler = r => JsonNode.Parse(r.Body)!["query"]!.GetValue<string>().Contains("me {", StringComparison.Ordinal)
            ? (200, """{"data":{"me":{"id":"7"}}}""")
            : (200, """{"errors":[{"message":"Column not found"}]}""");
        using var monday = new MondayIntegration();
        await monday.ConnectAsync(For(server, ("board", "1")), CancellationToken.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => monday.ExecuteAsync("status", "9=Fait", default));
        Assert.Contains("Column not found", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MondayIntegration().ExecuteAsync("item", "x", default));
    }

    [Fact]
    public void EveryCommandIsDescribedAndTheirTokensAreSecrets()
    {
        foreach (IIntegration i in new IIntegration[] { new ClickUpIntegration(), new NotionIntegration(), new MondayIntegration() })
        {
            Assert.All(i.Commands, c => { Assert.NotEmpty(c.Label); Assert.NotEmpty(c.Description); Assert.True(Enum.TryParse(c.Icon, out Wpf.Ui.Controls.SymbolRegular _), $"{i.Id}.{c.Name} : icône {c.Icon}"); });
            Assert.Equal(SettingKind.Secret, i.Fields.First(f => f.Key == "token").Kind);
            (i as IDisposable)?.Dispose();
        }
    }
}
