using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Google Chat : nombre lu dans les libellés d'accessibilité, activité par favicon sur demande seulement (ADR 0022).</summary>
public class NotificationUnreadLabelTests
{
    static readonly Regex Default = new(NotificationProvider.DefaultLabelPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static int? Read(string label)
    {
        var m = Default.Match(label);
        if (!m.Success) return null;
        for (int i = 1; i < m.Groups.Count; i++) if (m.Groups[i].Success) return int.Parse(m.Groups[i].Value);
        return null;
    }

    [Theory]
    [InlineData("3 messages non lus", 3)]
    [InlineData("1 message non lu", 1)]
    [InlineData("2 conversations non lues", 2)]
    [InlineData("2 unread", 2)]
    [InlineData("5 unread messages", 5)]
    [InlineData("Non lus : 4", 4)]
    [InlineData("Unread (6)", 6)]
    [InlineData("Messages non lus", null)]
    [InlineData("Inbox", null)]
    [InlineData("-4 non lus", null)]
    [InlineData("12 membres", null)]
    [InlineData("1234567 unread", null)]           // plus de 6 chiffres : pas un compteur
    public void TheDefaultPatternReadsFrenchAndEnglishLabels(string label, int? expected)
    {
        Assert.Equal(expected, Read(label));
    }

    [Fact]
    public void GoogleChatReadsLabelsByDefaultAndOtherServicesDoNot()
    {
        var all = new NotificationProviders();
        var chat = (NotificationProvider)all.Find("gchat")!;
        Assert.True(chat.UnreadLabels);
        Assert.NotNull(chat.ObserverScript);
        Assert.Contains("aria-label", chat.ObserverScript);
        Assert.False(chat.ShowFaviconActivity);
        Assert.All(all.All.Where(p => p.Id != "gchat"), p => Assert.Null(p.ObserverScript));
        Assert.All(all.All, p => Assert.False(p.ShowFaviconActivity));
    }

    [Fact]
    public void ASelectorReplacesTheLabelsAndTitleModeInjectsNothing()
    {
        var chat = (NotificationProvider)new NotificationProviders().Find("gchat")!;
        var withSelector = chat.With(BadgeMode.Auto, "span.x", "data-n");
        Assert.DoesNotContain("aria-label", withSelector.ObserverScript);
        Assert.Contains("span.x", withSelector.ObserverScript);
        Assert.Null(chat.With(BadgeMode.Title, null, null).ObserverScript);
        Assert.Null(chat.With(BadgeMode.Off, null, null).ObserverScript);
    }

    [Fact]
    public void FaviconAndLabelPatternAreOptionalValidatedSettings()
    {
        var warnings = new List<string>();
        var ok = NotificationProviders.ParseOverrides("""{ "badges": { "gchat": { "favicon": true, "labelPattern": "([0-9]+) à lire" } } }""", warnings.Add);
        Assert.Empty(warnings);
        Assert.True(ok["gchat"].Favicon);
        var providers = new NotificationProviders(null, ok);
        var chat = (NotificationProvider)providers.Find("gchat")!;
        Assert.True(chat.ShowFaviconActivity);
        Assert.Contains("lire", chat.ObserverScript);

        var old = NotificationProviders.ParseOverrides("""{ "badges": { "gchat": { "badgeMode": "title" } } }""", warnings.Add);
        Assert.Null(old["gchat"].Favicon);
        Assert.False(((NotificationProvider)new NotificationProviders(null, old).Find("gchat")!).ShowFaviconActivity);

        foreach (var bad in new[]
        {
            """{ "badges": { "gchat": { "favicon": "oui" } } }""",
            """{ "badges": { "gchat": { "labelPattern": "sans groupe" } } }""",
            """{ "badges": { "gchat": { "labelPattern": "(" } } }""",
        })
        {
            warnings.Clear();
            Assert.Empty(NotificationProviders.ParseOverrides(bad, warnings.Add));
            Assert.Single(warnings);
        }
        warnings.Clear();
        Assert.Empty(NotificationProviders.ParseOverrides("{ \"badges\": { \"gchat\": { \"labelPattern\": \"(" + new string('a', 300) + ")\" } } }", warnings.Add));
        Assert.Single(warnings);
    }

    [Fact]
    public void ACountOfZeroOrNoNumberNeverShowsABadge()
    {
        Assert.Null(NotificationBadge.Text(NotificationBadge.Resolve(BadgeMode.Auto, 0, 0, false).Count, NotificationBadge.Resolve(BadgeMode.Auto, 0, 0, false).Activity));
        Assert.Null(NotificationBadge.Text(NotificationBadge.Resolve(BadgeMode.Auto, 0, null, false).Count, false));
        Assert.Null(NotificationBadge.Text(NotificationBadge.Resolve(BadgeMode.Selector, 0, 0, false).Count, false));
    }

    [Theory]
    [InlineData("""{"type":"streamdak.badge","count":3,"candidates":4}""", true, 3, 4)]
    [InlineData("""{"type":"streamdak.badge","count":3}""", true, 3, -1)]
    [InlineData("""{"type":"streamdak.badge","count":3,"candidates":-1}""", false, 0, -1)]
    [InlineData("""{"type":"streamdak.badge","count":3,"candidates":"x"}""", false, 0, -1)]
    [InlineData("""{"type":"streamdak.badge","count":3,"candidates":100001}""", false, 0, -1)]
    [InlineData("""{"type":"streamdak.badge","count":3,"candidates":1,"texte":"a"}""", false, 0, -1)]
    public void TheMessageMayCarryABoundedCandidateCountAndNothingElse(string json, bool ok, int count, int candidates)
    {
        Assert.Equal(ok, NotificationMessage.TryParse(json, out var n, out var c));
        Assert.Equal(count, n);
        Assert.Equal(candidates, c);
    }

    [Fact]
    public void TheBadgeNumberIsReadableOnTheBadgeInEveryTheme()
    {
        foreach (var p in new[] { Theme.DarkPalette, Theme.LightPalette })
        {
            Assert.True(Theme.Contrast(p.OnBadge, p.Badge) >= 4.5, $"{p.Badge} / {p.OnBadge}");
            Assert.True(Theme.Contrast(p.Badge, p.Bar) >= 3.0, "la pastille se détache de la barre");
        }
    }
}
