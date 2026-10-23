using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Actions;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Un gestionnaire par type d'action et un registre qui refuse les doublons.</summary>
[Collection("ui")]
public class ActionRegistryTests
{
    sealed class Fake : ActionHandler
    {
        public Fake(string type) : base(type, null) { }
        public override System.Threading.Tasks.Task<string?> ExecuteAsync(DeckAction action, StepContext context, System.Threading.CancellationToken cancellation) =>
            System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    readonly WpfHost _host;
    public ActionRegistryTests(WpfHost host) => _host = host;

    [Fact]
    public void BuiltInHandlersCoverExactlyTheKnownTypesOneClassEach()
    {
        Assert.Equal(ActionTypes.All.OrderBy(t => t), BuiltInActions.All.Select(h => h.Type).OrderBy(t => t));
        Assert.Equal(BuiltInActions.All.Count, BuiltInActions.All.Select(h => h.GetType()).Distinct().Count());
        Assert.All(BuiltInActions.All, h => Assert.EndsWith("Action", h.GetType().Name));
    }

    [Fact]
    public void OnlyPausesAndTimersAreWithoutATimeLimit()
    {
        var unlimited = BuiltInActions.All.Where(h => h.Timeout == null).Select(h => h.Type).OrderBy(t => t);
        Assert.Equal(new[] { ActionTypes.Delay, ActionTypes.Timer }.OrderBy(t => t), unlimited);
        Assert.Equal(ActionTimeouts.Quick, BuiltInActions.All.Single(h => h.Type == ActionTypes.Hotkey).Timeout);
        Assert.Equal(ActionTimeouts.Launch, BuiltInActions.All.Single(h => h.Type == ActionTypes.Script).Timeout);
        Assert.Equal(ActionTimeouts.Http, BuiltInActions.All.Single(h => h.Type == ActionTypes.Http).Timeout);
    }

    [Fact]
    public void ADuplicateTypeIsRefusedInsteadOfShadowingTheOtherOne()
    {
        var registry = ActionRegistry.CreateDefault();
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new Fake("HOTKEY")));
        Assert.Contains("déjà enregistré", ex.Message);
        Assert.Contains("HotkeyAction", ex.Message);
        Assert.Throws<InvalidOperationException>(() => new ActionRegistry(new IActionHandler[] { new Fake("x"), new Fake("X") }));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new Fake(" ")));
    }

    [Fact]
    public void LookupIgnoresCaseAndUnknownTypesAreReportedNotThrown()
    {
        var registry = ActionRegistry.CreateDefault();
        Assert.True(registry.TryGet("Hotkey", out var handler));
        Assert.Equal(ActionTypes.Hotkey, handler.Type);
        Assert.False(registry.TryGet("nope", out _));
        Assert.False(registry.TryGet(null, out _));
    }

    [Fact]
    public void AnIntegrationAddsItsOwnTypeToTheContainerWithoutTouchingTheBuiltIns()
    {
        // Même mécanisme que AppHost : chaque gestionnaire est un IActionHandler du conteneur
        var services = new ServiceCollection();
        foreach (var h in BuiltInActions.All) services.AddSingleton(h);
        services.AddSingleton<IActionHandler>(new Fake("obs"));
        using var provider = services.BuildServiceProvider();
        var registry = new ActionRegistry(provider.GetServices<IActionHandler>());
        Assert.Contains("obs", registry.Types);
        Assert.Equal(ActionTypes.All.Count + 1, registry.Types.Count);
    }

    [Fact]
    public void TheApplicationEngineUsesTheContainerRegistry()
    {
        Assert.Equal(_host.Services.GetRequiredService<ActionRegistry>().Types.OrderBy(t => t), _host.Engine.Types.OrderBy(t => t));
        var integrations = _host.Services.GetServices<Integrations.IIntegration>().Select(i => i.Id);
        Assert.Equal(ActionTypes.All.Concat(integrations).OrderBy(t => t), _host.Engine.Types.OrderBy(t => t));   // types intégrés + un par intégration
    }
}
