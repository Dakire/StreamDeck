using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Modèles de profils : chaque case existe dans le catalogue (intégrations comprises), est valide, et le profil se crée en une opération annulable.</summary>
[Collection("ui")]
public class ProfileTemplateTests
{
    readonly WpfHost _host;
    public ProfileTemplateTests(WpfHost host) => _host = host;

    [Fact]
    public void EveryTemplateSlotResolvesToAValidButtonUnlessDeclaredEmpty()
    {
        _ = _host.Services;   // le conteneur enregistre les catégories des intégrations dans le catalogue
        var cfg = ConfigStore.DefaultConfig();
        Assert.NotEmpty(ProfileTemplates.All);
        foreach (var template in ProfileTemplates.All)
        {
            var profile = template.Build();
            var page = Assert.Single(profile.Pages);
            Assert.Equal(template.Columns * template.Rows, page.Buttons.Count);
            foreach (var (slot, button) in template.Slots.Zip(page.Buttons))
            {
                if (slot.CatalogId == null) { Assert.True(button.Blank); continue; }
                Assert.False(button.Blank, $"{template.Id} : « {slot.CatalogId} » absent du catalogue");
                var errors = ActionValidator.Validate(button, cfg, profile).Where(i => i.Severity == Severity.Error).ToList();
                Assert.True(errors.Count == 0, $"{template.Id} / {slot.CatalogId} : {errors.FirstOrDefault()?.Message}");
            }
        }
    }

    [Fact]
    public void AMissingCatalogEntryLeavesAnEmptySlotInsteadOfFailing()
    {
        var template = new ProfileTemplate("x", "X", "", "Star24", 2, 1, new[] { new TemplateSlot("n'existe.pas"), new TemplateSlot("sys.desktop") });
        var page = template.Build().Pages[0];
        Assert.True(page.Buttons[0].Blank);
        Assert.False(page.Buttons[1].Blank);
    }

    [Fact]
    public void TemplatesOverrideTheArgumentTheLabelAndTheValue()
    {
        _ = _host.Services;
        var button = ProfileTemplates.Find("streaming")!.Build().Pages[0].Buttons[0];
        Assert.Equal("Jeu", button.Label);
        Assert.Equal("Jeu", button.Actions[0].Args);
        Assert.Equal("obs", button.Actions[0].Type);
        Assert.Null(ProfileTemplates.Find("inconnu"));
    }

    [Fact]
    public void TheEditorCreatesTheProfileFromTheMenuAndUndoRemovesIt()
    {
        var editor = _host.Run(() => _host.Create<EditorViewModel>(new FakeShell()));
        var doc = _host.Services.GetRequiredService<DeckDocument>();
        int before = doc.Config.Profiles.Count;

        var menu = editor.BuildProfileMenu("Principal");
        var parent = menu.First(i => i.Header == "Nouveau profil depuis un modèle");
        Assert.Equal(ProfileTemplates.All.Count, parent.Children!.Count);

        _host.Run(() => parent.Children.First(c => c.Header == "Réunions").Execute!());
        _host.Settle();
        Assert.Equal(before + 1, doc.Config.Profiles.Count);
        Assert.Contains(doc.Config.Profiles, p => p.Name == "Réunions");

        _host.Run(() => doc.Undo());
        Assert.Equal(before, doc.Config.Profiles.Count);
        _host.Run(editor.Dispose);
    }
}
