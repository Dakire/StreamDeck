using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TouchDeck.Actions;
using TouchDeck.Integrations;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>API locale : commandes JSON, validation des actions, transport par canal nommé réservé à l'utilisateur.</summary>
[Collection("ui")]
public class LocalApiTests
{
    sealed class FakeApiShell : IApiShell, INavigator
    {
        public readonly List<string> Calls = new();
        public INavigator Navigator => this;
        public void GoToPage(string name) => Calls.Add("page:" + name);
        public void SwitchProfile(string name) => Calls.Add("profile:" + name);
        public void ShowDeck() => Calls.Add("show");
        public void OpenEditor() => Calls.Add("edit");
        public void OpenSettings() => Calls.Add("settings");
    }

    readonly WpfHost _host;
    public LocalApiTests(WpfHost host) => _host = host;

    (LocalApi Api, FakeApiShell Shell) Create()
    {
        var shell = new FakeApiShell();
        var api = new LocalApi(_host.Services.GetRequiredService<DeckDocument>(), _host.Services.GetRequiredService<ActionEngine>(),
            _host.Services.GetRequiredService<IntegrationManager>(), shell);
        return (api, shell);
    }

    static async Task<JsonObject> Ask(LocalApi api, string request) => JsonNode.Parse(await api.HandleAsync(request))!.AsObject();

    [Fact]
    public async Task PingStatusAndValuesDescribeTheRunningApplication()
    {
        var (api, _) = Create();

        var ping = await Ask(api, """{"cmd":"ping"}""");
        Assert.True(ping["ok"]!.GetValue<bool>());
        Assert.Equal(LocalApi.ProtocolVersion, ping["protocol"]!.GetValue<int>());

        var status = await Ask(api, """{"cmd":"status"}""");
        Assert.Equal(_host.Document.ActiveProfile.Name, status["activeProfile"]!.GetValue<string>());
        Assert.NotEmpty(status["profiles"]!.AsArray());
        Assert.Equal("disabled", status["integrations"]!["obs"]!.GetValue<string>());

        Assert.NotNull((await Ask(api, """{"cmd":"values"}"""))["values"]);
    }

    [Fact]
    public async Task NavigationAndWindowCommandsReachTheShell()
    {
        var (api, shell) = Create();
        var profile = _host.Document.ActiveProfile.Name;

        Assert.True((await Ask(api, $$"""{"cmd":"profile","name":"{{profile.ToUpperInvariant()}}"}"""))["ok"]!.GetValue<bool>());
        Assert.True((await Ask(api, """{"cmd":"page","name":"Accueil"}"""))["ok"]!.GetValue<bool>());
        await Ask(api, """{"cmd":"show"}"""); await Ask(api, """{"cmd":"edit"}"""); await Ask(api, """{"cmd":"settings"}""");

        Assert.Equal(new[] { "profile:" + profile, "page:Accueil", "show", "edit", "settings" }, shell.Calls);
    }

    [Fact]
    public async Task RunExecutesAValidatedActionThroughTheEngine()
    {
        var (api, _) = Create();
        var reply = await Ask(api, """{"cmd":"run","action":{"type":"delay","value":"10"}}""");
        Assert.True(reply["ok"]!.GetValue<bool>());
        Assert.Equal("success", reply["outcome"]!.GetValue<string>());

        var many = await Ask(api, """{"cmd":"run","actions":[{"type":"delay","value":"5"},{"type":"delay","value":"5"}]}""");
        Assert.True(many["ok"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("""{"cmd":"run","action":{"type":"hotkey","value":""}}""", "")]
    [InlineData("""{"cmd":"run","action":{"type":"nope","value":"x"}}""", "")]
    [InlineData("""{"cmd":"run"}""", "action")]
    [InlineData("""{"cmd":"run","actions":[]}""", "Entre 1 et 20")]
    [InlineData("""{"cmd":"profile","name":"inconnu"}""", "introuvable")]
    [InlineData("""{"cmd":"page"}""", "nom de la page")]
    [InlineData("""{"cmd":"explode"}""", "inconnue")]
    [InlineData("""{"x":1}""", "cmd")]
    [InlineData("""[1,2]""", "objet JSON")]
    [InlineData("pas du json", "JSON invalide")]
    public async Task InvalidRequestsAreRefusedWithAReadableReason(string request, string expected)
    {
        var (api, shell) = Create();
        var reply = await Ask(api, request);
        Assert.False(reply["ok"]!.GetValue<bool>());
        Assert.Contains(expected, reply["error"]!.GetValue<string>());
        Assert.Empty(shell.Calls);
    }

    [Fact]
    public async Task AnActionTooLongToBeReasonableIsRefused()
    {
        var (api, _) = Create();
        var actions = string.Join(",", Enumerable.Repeat("""{"type":"delay","value":"1"}""", 21));
        var reply = await Ask(api, $$"""{"cmd":"run","actions":[{{actions}}]}""");
        Assert.False(reply["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TheNamedPipeCarriesSeveralRequestsAndRefusesOversizedOnes()
    {
        var (api, shell) = Create();
        var name = "TouchDeck.Api.Test." + Guid.NewGuid().ToString("N");
        using var server = new LocalApiServer(api, name);
        server.Start();

        var ping = JsonNode.Parse(await LocalApiClient.SendAsync(name, """{"cmd":"ping"}""", TimeSpan.FromSeconds(5)))!;
        Assert.True(ping["ok"]!.GetValue<bool>());
        var edit = JsonNode.Parse(await LocalApiClient.SendAsync(name, """{"cmd":"edit"}""", TimeSpan.FromSeconds(5)))!;
        Assert.True(edit["ok"]!.GetValue<bool>());
        Assert.Contains("edit", shell.Calls);

        var huge = "{\"cmd\":\"ping\",\"pad\":\"" + new string('x', LocalApiServer.MaxRequestBytes + 10) + "\"}";
        var refused = JsonNode.Parse(await LocalApiClient.SendAsync(name, huge, TimeSpan.FromSeconds(5)))!;
        Assert.False(refused["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task NobodyListeningMeansATimeoutNotAHang()
    {
        var name = "TouchDeck.Api.Absent." + Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<TimeoutException>(() => LocalApiClient.SendAsync(name, """{"cmd":"ping"}""", TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void ThePipeNameIsPerUserAndPerConfigurationFolder()
    {
        Assert.Contains(Environment.UserName, LocalApiServer.DefaultPipeName);
        Assert.EndsWith(LocalApiServer.InstanceKey(), LocalApiServer.DefaultPipeName);
        Assert.Equal(8, LocalApiServer.InstanceKey().Length);
    }
}
