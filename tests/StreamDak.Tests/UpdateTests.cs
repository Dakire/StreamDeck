using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Numéros de version : lecture et ordre SemVer 2.0.</summary>
public class SemVerTests
{
    [Theory]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("v1.4.0", "1.4.0")]
    [InlineData("V2.0.1", "2.0.1")]
    [InlineData(" 1.4.0 ", "1.4.0")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("1.5.0-beta.1", "1.5.0-beta.1")]
    [InlineData("1.5.0-rc.1+build.7", "1.5.0-rc.1")]
    [InlineData("0.0.1-0", "0.0.1-0")]
    public void ValidVersionsAreRead(string text, string expected) => Assert.Equal(expected, SemVer.Parse(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("latest")]
    [InlineData("nightly-2026-10-26")]
    [InlineData("1.x.0")]
    [InlineData("1.4.0.0")]
    [InlineData("1.4.0-")]
    [InlineData("1.4.0-beta..1")]
    [InlineData("1.4.0-01")]
    [InlineData("v-1.4.0")]
    [InlineData("1.4.0;calc")]
    [InlineData("99999999999.0.0")]
    public void ThingsThatAreNotVersionsAreRefused(string text) => Assert.False(SemVer.TryParse(text, out _));

    [Fact]
    public void NullAndHugeTagsAreRefused()
    {
        Assert.False(SemVer.TryParse(null, out _));
        Assert.False(SemVer.TryParse("1.0.0-" + new string('a', 200), out _));
        Assert.Throws<FormatException>(() => SemVer.Parse("nope"));
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.9.0", "1.10.0")]                     // numérique, pas alphabétique
    [InlineData("1.0.9", "1.0.10")]
    [InlineData("1.4.0-beta.2", "1.4.0")]               // une pré-version précède sa version finale
    [InlineData("1.4.0", "1.4.1-alpha")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]        // exemple de la spécification SemVer, dans l'ordre
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]       // 2 < 11 en numérique
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    public void TheOrderFollowsSemVer(string older, string newer)
    {
        var a = SemVer.Parse(older);
        var b = SemVer.Parse(newer);
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a <= b && b >= a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void BuildMetadataAndTheLeadingVAreIgnoredInComparisons()
    {
        Assert.Equal(SemVer.Parse("1.4.0"), SemVer.Parse("v1.4.0+abc123"));
        Assert.True(SemVer.Parse("1.4.0") == SemVer.Parse("1.4.0+other"));
        Assert.Equal(SemVer.Parse("1.4.0").GetHashCode(), SemVer.Parse("1.4.0+x").GetHashCode());
        Assert.True(SemVer.Parse("1.4.0-beta").IsPreRelease);
        Assert.False(SemVer.Parse("1.4.0").IsPreRelease);
    }

    [Fact]
    public void TheRunningApplicationHasAReadableVersion() => Assert.True(UpdateEnvironment.CurrentVersion() >= SemVer.Parse("0.0.0"));
}

/// <summary>Lecture des réponses de l'API GitHub, dépôt et adresses de téléchargement.</summary>
public class GitHubReleaseParsingTests
{
    static readonly UpdateRepo Repo = new("Dakire", "StreamDeck");

    public static string Asset(string tag, string name, long size = 1000, string? url = null) =>
        new JsonObject
        {
            ["name"] = name, ["size"] = size,
            ["browser_download_url"] = url ?? ("https://github.com/Dakire/StreamDeck/releases/download/" + tag + "/" + name),
        }.ToJsonString();

    public static string Release(string tag, bool prerelease = false, bool draft = false, string body = "Notes", params string[] assets) =>
        new JsonObject
        {
            ["tag_name"] = tag, ["name"] = "StreamDak " + tag, ["draft"] = draft, ["prerelease"] = prerelease,
            ["html_url"] = "https://github.com/Dakire/StreamDeck/releases/tag/" + tag, ["published_at"] = "2026-10-26T10:00:00Z", ["body"] = body,
            ["assets"] = new JsonArray(assets.Select(a => JsonNode.Parse(a)).ToArray()),
        }.ToJsonString();

    [Theory]
    [InlineData("Dakire", "StreamDeck", true)]
    [InlineData("a", "b", true)]
    [InlineData("my-org", "my.repo_1", true)]
    [InlineData("", "x", false)]
    [InlineData("x", "", false)]
    [InlineData("-bad", "x", false)]
    [InlineData("a/b", "x", false)]
    [InlineData("x", "a/b", false)]
    [InlineData("x", "..", false)]
    [InlineData("x", "a b", false)]
    [InlineData("x", "a?b=c", false)]
    public void OwnerAndRepoMustBeGitHubNames(string owner, string repo, bool valid) => Assert.Equal(valid, new UpdateRepo(owner, repo).IsValid);

    [Fact]
    public void TheApiAddressesAreHttpsOnTheApiHost()
    {
        Assert.Equal("https://api.github.com/repos/Dakire/StreamDeck/releases/latest", Repo.LatestUrl.AbsoluteUri);
        Assert.StartsWith("https://api.github.com/repos/Dakire/StreamDeck/releases?per_page=", Repo.ListUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://github.com/Dakire/StreamDeck/releases/download/v1.4.0/StreamDak-1.4.0-self-contained-Setup.exe", true)]
    [InlineData("https://GitHub.com/dakire/streamdeck/releases/download/v1.4.0/x.exe", true)]               // casse du dépôt tolérée
    [InlineData("http://github.com/Dakire/StreamDeck/releases/download/v1.4.0/x.exe", false)]               // pas de HTTP
    [InlineData("https://github.com/Autre/StreamDeck/releases/download/v1.4.0/x.exe", false)]               // un autre propriétaire
    [InlineData("https://github.com/Dakire/Autre/releases/download/v1.4.0/x.exe", false)]                   // un autre dépôt
    [InlineData("https://github.com/Dakire/StreamDeck/releases/download/v1.3.0/x.exe", false)]              // une autre release
    [InlineData("https://github.com/Dakire/StreamDeck/archive/refs/tags/v1.4.0.zip", false)]
    [InlineData("https://evil.com/Dakire/StreamDeck/releases/download/v1.4.0/x.exe", false)]
    [InlineData("https://github.com.evil.com/Dakire/StreamDeck/releases/download/v1.4.0/x.exe", false)]
    [InlineData("https://user@github.com/Dakire/StreamDeck/releases/download/v1.4.0/x.exe", false)]
    [InlineData("https://github.com:8443/Dakire/StreamDeck/releases/download/v1.4.0/x.exe", false)]
    [InlineData("https://github.com/Dakire/StreamDeck/releases/download/v1.4.0/x.exe?token=1", false)]
    [InlineData("https://github.com/Dakire/StreamDeck/releases/download/v1.4.0/sub/x.exe", false)]
    [InlineData("https://github.com/Dakire/StreamDeck/releases/download/v1.4.0/", false)]
    [InlineData("https://github.com/Dakire/StreamDeck/releases/download/v1.4.0/..%2f..%2fx.exe", false)]
    public void OnlyAFileOfTheConfiguredRepositoryReleaseIsAnAssetUrl(string url, bool expected) => Assert.Equal(expected, Repo.IsAssetUrl(new Uri(url), "v1.4.0"));

    [Theory]
    [InlineData("https://github.com/x", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/2?sig=3", true)]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("http://objects.githubusercontent.com/x", false)]
    [InlineData("https://githubusercontent.com.evil.com/x", false)]
    [InlineData("https://evilgithubusercontent.com/x", false)]
    [InlineData("https://evil.com/x", false)]
    [InlineData("https://user:pw@github.com/x", false)]
    [InlineData("https://objects.githubusercontent.com:8443/x", false)]
    public void RedirectsOnlyGoToGitHubHostsOverHttps(string url, bool allowed) => Assert.Equal(allowed, UpdateRepo.IsAllowedRedirect(new Uri(url)));

    [Fact]
    public void AReleaseKeepsItsNotesDateAndOnlyItsOwnRepositoryAssets()
    {
        var json = Release("v1.4.0", body: "# Nouveautés\n- a", assets: new[]
        {
            Asset("v1.4.0", "StreamDak-1.4.0-self-contained-Setup.exe", 5000),
            Asset("v1.4.0", "checksums.txt", 200),
            Asset("v1.4.0", "piege.exe", 1, "https://evil.com/piege.exe"),
            Asset("v1.4.0", "autre.exe", 1, "https://github.com/Autre/Depot/releases/download/v1.4.0/autre.exe"),
        });
        var release = GitHubReleases.ParseRelease(JsonNode.Parse(json), Repo)!;
        Assert.Equal("v1.4.0", release.Tag);
        Assert.Equal(SemVer.Parse("1.4.0"), release.Version);
        Assert.Equal("# Nouveautés\n- a", release.Notes);
        Assert.Equal(new[] { "StreamDak-1.4.0-self-contained-Setup.exe", "checksums.txt" }, release.Assets.Select(a => a.Name));
        Assert.Equal(5000, release.Assets[0].Size);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal("https://github.com/Dakire/StreamDeck/releases/tag/v1.4.0", release.PageUrl.AbsoluteUri);
    }

    [Fact]
    public void DraftsBadTagsAndGarbageAreNotReleases()
    {
        Assert.Null(GitHubReleases.ParseRelease(JsonNode.Parse(Release("v1.4.0", draft: true)), Repo));
        Assert.Null(GitHubReleases.ParseRelease(JsonNode.Parse(Release("nightly")), Repo));
        Assert.Null(GitHubReleases.ParseRelease(JsonNode.Parse("[1,2]"), Repo));
        Assert.Null(GitHubReleases.ParseRelease(JsonNode.Parse("""{ "name": "sans tag" }"""), Repo));
        Assert.Null(GitHubReleases.ParseRelease(null, Repo));
    }

    [Fact]
    public void AForeignPageAddressFallsBackToTheRepositoryTagPage()
    {
        var json = Release("v1.4.0").Replace("https://github.com/Dakire/StreamDeck/releases/tag/v1.4.0", "https://evil.com/phish");
        Assert.Equal("https://github.com/Dakire/StreamDeck/releases/tag/v1.4.0", GitHubReleases.ParseRelease(JsonNode.Parse(json), Repo)!.PageUrl.AbsoluteUri);
    }

    [Fact]
    public void LongNotesAreCut()
    {
        var release = GitHubReleases.ParseRelease(JsonNode.Parse(Release("v1.4.0", body: new string('x', GitHubReleases.MaxNotesLength + 500))), Repo)!;
        Assert.True(release.Notes.Length <= GitHubReleases.MaxNotesLength + 2);
    }

    [Fact]
    public void TheNewestReleaseIgnoresDraftsAndPreReleasesUnlessTheBetaChannelIsOn()
    {
        var list = "[" + string.Join(",", Release("v1.3.0"), Release("v1.5.0-beta.1", prerelease: true), Release("v1.4.0"), Release("v9.9.9", draft: true), Release("nightly")) + "]";
        var json = JsonNode.Parse(list);
        Assert.Equal("v1.4.0", GitHubReleases.Newest(json, Repo, includePreReleases: false)!.Tag);
        Assert.Equal("v1.5.0-beta.1", GitHubReleases.Newest(json, Repo, includePreReleases: true)!.Tag);
        Assert.Equal("v1.4.0", GitHubReleases.Newest(JsonNode.Parse(Release("v1.4.0")), Repo, false)!.Tag);                       // objet (« latest »)
        Assert.Null(GitHubReleases.Newest(JsonNode.Parse(Release("v1.5.0-beta.1", prerelease: true)), Repo, false));               // seulement une pré-version
        Assert.Null(GitHubReleases.Newest(JsonNode.Parse("[]"), Repo, true));
    }

    [Fact]
    public void APreReleaseFlagIsKeptEvenWhenTheTagLooksFinal() =>
        Assert.True(GitHubReleases.ParseRelease(JsonNode.Parse(Release("v1.4.0", prerelease: true)), Repo)!.PreRelease);
}

/// <summary>Réglages et mémoire des mises à jour : fichiers, valeurs par défaut, dépôt configurable.</summary>
public sealed class UpdateSettingsTests : IDisposable
{
    readonly string _user = Path.Combine(Path.GetTempPath(), "sd-upd-u-" + Guid.NewGuid().ToString("N"));
    readonly string _app = Path.Combine(Path.GetTempPath(), "sd-upd-a-" + Guid.NewGuid().ToString("N"));

    public UpdateSettingsTests() { Directory.CreateDirectory(_user); Directory.CreateDirectory(_app); }
    public void Dispose() { try { Directory.Delete(_user, true); Directory.Delete(_app, true); } catch (IOException) { } }

    [Fact]
    public void TheRepositoryComesFromTheShippedDefaultsAndTheUserFileOverridesIt()
    {
        Assert.Null(UpdateSettings.Load(_user, _app).Repository);                                         // rien de configuré : fonction désactivée
        File.WriteAllText(Path.Combine(_app, UpdateSettings.DefaultsFileName), """{ "owner": "Dakire", "repo": "StreamDeck" }""");
        var defaults = UpdateSettings.Load(_user, _app);
        Assert.Equal(new UpdateRepo("Dakire", "StreamDeck"), defaults.Repository);
        Assert.True(defaults.AutoCheck);
        Assert.False(defaults.Beta);

        File.WriteAllText(Path.Combine(_user, UpdateSettings.FileName), """{ "owner": "Mon-Org", "repo": "Fork", "beta": true, "autoCheck": false }""");
        var user = UpdateSettings.Load(_user, _app);
        Assert.Equal(new UpdateRepo("Mon-Org", "Fork"), user.Repository);
        Assert.True(user.Beta);
        Assert.False(user.AutoCheck);
    }

    [Fact]
    public void AUserFileWithoutARepositoryKeepsTheShippedOneAndInvalidNamesDisableTheFeature()
    {
        File.WriteAllText(Path.Combine(_app, UpdateSettings.DefaultsFileName), """{ "owner": "Dakire", "repo": "StreamDeck" }""");
        File.WriteAllText(Path.Combine(_user, UpdateSettings.FileName), """{ "beta": true }""");
        var settings = UpdateSettings.Load(_user, _app);
        Assert.Equal("StreamDeck", settings.Repository!.Repo);
        Assert.True(settings.Beta);

        File.WriteAllText(Path.Combine(_user, UpdateSettings.FileName), """{ "owner": "a/b", "repo": "x" }""");
        Assert.Null(UpdateSettings.Load(_user, _app).Repository);                                         // un nom invalide n'entre jamais dans une adresse
    }

    [Fact]
    public void UnreadableFilesAreIgnoredAndTheSettingsRoundTrip()
    {
        File.WriteAllText(Path.Combine(_user, UpdateSettings.FileName), "{ pas du json");
        File.WriteAllText(Path.Combine(_app, UpdateSettings.DefaultsFileName), new string(' ', 20_000));
        Assert.Null(UpdateSettings.Load(_user, _app).Repository);

        var settings = new UpdateSettings { Owner = "Dakire", Repo = "StreamDeck", Beta = true, AutoCheck = false };
        settings.Save(_user);
        var loaded = UpdateSettings.Load(_user, _app);
        Assert.Equal((new UpdateRepo("Dakire", "StreamDeck"), true, false), (loaded.Repository, loaded.Beta, loaded.AutoCheck));
        Assert.False(File.Exists(Path.Combine(_user, UpdateSettings.FileName + ".tmp")));
    }

    [Fact]
    public void TheStateRoundTripsAndABrokenFileGivesAFreshOne()
    {
        var state = new UpdateState { LastCheckUtc = new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc), ETag = "W/\"abc\"", IgnoredVersion = "v1.4.0", CachedBody = "[]" };
        state.Save(_user);
        var again = UpdateState.Load(_user);
        Assert.Equal(("W/\"abc\"", "v1.4.0", "[]"), (again.ETag, again.IgnoredVersion, again.CachedBody));
        Assert.Equal(state.LastCheckUtc, again.LastCheckUtc);
        File.WriteAllText(Path.Combine(_user, UpdateState.FileName), "garbage");
        Assert.Null(UpdateState.Load(_user).ETag);
    }

    [Fact]
    public void TheShippedDefaultsFileNamesTheProjectRepository()
    {
        var path = Path.Combine(AppContext.BaseDirectory, UpdateSettings.DefaultsFileName);
        Assert.True(File.Exists(path), "update.defaults.json doit être livré à côté de l'exécutable");
        Assert.NotNull(UpdateSettings.Load(_user, AppContext.BaseDirectory).Repository);
    }
}

/// <summary>Fausse API GitHub : on y décrit les réponses par adresse, on y lit les requêtes reçues.</summary>
sealed class FakeGitHub : HttpMessageHandler
{
    public sealed record Seen(HttpMethod Method, Uri Url, HttpRequestHeaders Headers);

    public List<Seen> Requests { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(new Seen(request.Method, request.RequestUri!, request.Headers));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Respond(request));
    }

    public static HttpResponseMessage Json(string body, string? etag = null)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (etag != null) r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag, isWeak: true);
        return r;
    }
}

/// <summary>Vérification : version plus récente, ETag, limite de l'API, incidents réseau.</summary>
public class UpdateCheckerTests
{
    static readonly SemVer Current = SemVer.Parse("1.0.0");
    static readonly UpdateSettings Settings = new() { Owner = "Dakire", Repo = "StreamDeck" };
    DateTime _now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    UpdateChecker Checker(FakeGitHub github) => new(new HttpClient(github), Current, () => _now);

    static string Latest(string tag, bool prerelease = false) => GitHubReleaseParsingTests.Release(tag, prerelease, body: "Notes de " + tag,
        assets: GitHubReleaseParsingTests.Asset(tag, "checksums.txt"));

    [Fact]
    public async Task ANewerTagIsAvailableWithItsNotes()
    {
        var github = new FakeGitHub { Respond = _ => FakeGitHub.Json(Latest("v1.4.0"), "\"e1\"") };
        var result = await Checker(github).CheckAsync(Current, Settings, new UpdateState(), default);
        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal("v1.4.0", result.Release!.Tag);
        Assert.Equal("Notes de v1.4.0", result.Release.Notes);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.0")]
    [InlineData("1.0.0")]
    public async Task TheSameOrAnOlderVersionIsUpToDate(string tag)
    {
        var github = new FakeGitHub { Respond = _ => FakeGitHub.Json(Latest(tag)) };
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(github).CheckAsync(Current, Settings, new UpdateState(), default)).Status);
    }

    [Fact]
    public async Task TheRequestIsHttpsWithAUserAgentTheApiVersionAndNoCredentials()
    {
        var github = new FakeGitHub { Respond = _ => FakeGitHub.Json(Latest("v1.4.0")) };
        await Checker(github).CheckAsync(Current, Settings, new UpdateState(), default);
        var seen = github.Requests.Single();
        Assert.Equal("https://api.github.com/repos/Dakire/StreamDeck/releases/latest", seen.Url.AbsoluteUri);
        Assert.Equal("StreamDak/1.0.0", seen.Headers.UserAgent.ToString());
        Assert.Equal("application/vnd.github+json", seen.Headers.Accept.ToString());
        Assert.Equal("2022-11-28", seen.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Null(seen.Headers.Authorization);                                    // aucun jeton : les releases d'un dépôt public se lisent sans compte
    }

    [Fact]
    public async Task ETagsMakeTheSecondRequestConditionalAndA304ReplaysTheKeptAnswer()
    {
        var github = new FakeGitHub { Respond = _ => FakeGitHub.Json(Latest("v1.4.0"), "\"e1\"") };
        var state = new UpdateState();
        var checker = Checker(github);
        await checker.CheckAsync(Current, Settings, state, default);
        Assert.Equal("W/\"e1\"", state.ETag);
        Assert.Null(github.Requests[0].Headers.IfNoneMatch.FirstOrDefault());      // première requête : pas de condition

        github.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);
        _now = _now.AddHours(25);
        var again = await checker.CheckAsync(Current, Settings, state, default);
        Assert.Equal("W/\"e1\"", github.Requests[1].Headers.IfNoneMatch.Single().ToString());
        Assert.Equal((UpdateStatus.Available, true, "v1.4.0"), (again.Status, again.FromCache, again.Release!.Tag));
        Assert.Equal(_now, state.LastCheckUtc);                                     // un 304 compte comme une vérification faite
    }

    [Fact]
    public async Task AnETagNeverServesAnotherChannelOrRepository()
    {
        var github = new FakeGitHub { Respond = _ => FakeGitHub.Json(Latest("v1.4.0"), "\"e1\"") };
        var state = new UpdateState();
        var checker = Checker(github);
        await checker.CheckAsync(Current, Settings, state, default);

        var beta = new UpdateSettings { Owner = "Dakire", Repo = "StreamDeck", Beta = true };
        await checker.CheckAsync(Current, beta, state, default);                    // canal différent : requête sans condition
        Assert.Empty(github.Requests[1].Headers.IfNoneMatch);
        Assert.StartsWith("https://api.github.com/repos/Dakire/StreamDeck/releases?per_page=", github.Requests[1].Url.AbsoluteUri);

        await checker.CheckAsync(Current, new UpdateSettings { Owner = "Dakire", Repo = "Autre", Beta = true }, state, default);
        Assert.Empty(github.Requests[2].Headers.IfNoneMatch);                      // autre dépôt : idem
    }

    [Fact]
    public async Task TheBetaChannelProposesAPreReleaseAndTheStableChannelDoesNot()
    {
        var list = "[" + Latest("v1.5.0-beta.1", prerelease: true) + "," + Latest("v1.1.0") + "]";
        var github = new FakeGitHub { Respond = _ => FakeGitHub.Json(list) };
        var beta = await Checker(github).CheckAsync(Current, new UpdateSettings { Owner = "Dakire", Repo = "StreamDeck", Beta = true }, new UpdateState(), default);
        Assert.Equal("v1.5.0-beta.1", beta.Release!.Tag);
        var stable = await Checker(github).CheckAsync(Current, new UpdateSettings { Owner = "Dakire", Repo = "StreamDeck", Beta = true }, new UpdateState(), default);
        Assert.Equal(UpdateStatus.Available, stable.Status);

        var onlyPre = new FakeGitHub { Respond = _ => FakeGitHub.Json(Latest("v1.5.0-beta.1", prerelease: true)) };
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(onlyPre).CheckAsync(Current, Settings, new UpdateState(), default)).Status);   // /latest ne rend jamais de pré-version ; une réponse piégée est ignorée aussi
    }

    [Fact]
    public async Task NoReleaseYetIsNotAnError()
    {
        var github = new FakeGitHub();                                              // 404 partout
        var result = await Checker(github).CheckAsync(Current, Settings, new UpdateState(), default);
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Contains("Aucune version", result.Message);
    }

    [Fact]
    public async Task NoConfiguredRepositoryDisablesTheCheckWithoutAnyRequest()
    {
        var github = new FakeGitHub();
        var result = await Checker(github).CheckAsync(Current, new UpdateSettings(), new UpdateState(), default);
        Assert.Equal(UpdateStatus.Disabled, result.Status);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task TheApiRateLimitIsRespectedUntilItsResetWithNoMoreRequests()
    {
        var github = new FakeGitHub
        {
            Respond = _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Forbidden);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(30));
                return r;
            },
        };
        var state = new UpdateState();
        var checker = Checker(github);
        var first = await checker.CheckAsync(Current, Settings, state, default);
        Assert.Equal(UpdateStatus.Error, first.Status);
        Assert.Contains("limite", first.Message);
        Assert.Equal(_now.AddMinutes(30), state.NextAllowedUtc);

        _now = _now.AddMinutes(10);
        var second = await checker.CheckAsync(Current, Settings, state, default);
        Assert.Equal(UpdateStatus.Error, second.Status);
        Assert.Single(github.Requests);                                             // aucune requête pendant l'attente

        _now = _now.AddMinutes(30);
        github.Respond = _ => FakeGitHub.Json(Latest("v1.4.0"));
        Assert.Equal(UpdateStatus.Available, (await checker.CheckAsync(Current, Settings, state, default)).Status);
        Assert.Null(state.NextAllowedUtc);
    }

    [Fact]
    public async Task TheResetHeaderIsUsedWhenThereIsNoRetryAfter()
    {
        var reset = new DateTimeOffset(_now.AddMinutes(45));
        var github = new FakeGitHub
        {
            Respond = _ =>
            {
                var r = new HttpResponseMessage((HttpStatusCode)429);
                r.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                return r;
            },
        };
        var state = new UpdateState();
        await Checker(github).CheckAsync(Current, Settings, state, default);
        Assert.Equal(reset.UtcDateTime, state.NextAllowedUtc!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task NetworkTroubleBecomesAnErrorResultNeverAnException()
    {
        var down = new FakeGitHub { Respond = _ => throw new HttpRequestException("No such host") };
        var result = await Checker(down).CheckAsync(Current, Settings, new UpdateState(), default);
        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Contains("connexion", result.Message);

        foreach (var status in new[] { HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway, HttpStatusCode.Unauthorized })
        {
            var broken = new FakeGitHub { Respond = _ => new HttpResponseMessage(status) };
            var r = await Checker(broken).CheckAsync(Current, Settings, new UpdateState(), default);
            Assert.Equal(UpdateStatus.Error, r.Status);
            Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), r.Message);
        }
    }

    [Fact]
    public async Task AnUnreadableOrHugeAnswerIsAnError()
    {
        foreach (var body in new[] { "pas du json", "{ \"tag_name\": ", new string('x', GitHubReleases.MaxResponseBytes + 10) })
        {
            var github = new FakeGitHub { Respond = _ => FakeGitHub.Json(body) };
            var state = new UpdateState();
            var result = await Checker(github).CheckAsync(Current, Settings, state, default);
            Assert.Equal(UpdateStatus.Error, result.Status);
            Assert.Null(state.ETag);                                               // une mauvaise réponse n'est jamais gardée
        }
    }

    [Fact]
    public async Task AUserCancellationIsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        var github = new FakeGitHub { Respond = _ => { cts.Cancel(); return FakeGitHub.Json(Latest("v1.4.0")); } };
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Checker(github).CheckAsync(Current, Settings, new UpdateState(), cts.Token));
    }

    [Fact]
    public void AutomaticChecksAreDueOncePerDayAndSurviveAClockGoingBack()
    {
        var now = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateChecker.IsDue(new UpdateState(), now, TimeSpan.FromHours(24)));
        Assert.False(UpdateChecker.IsDue(new UpdateState { LastCheckUtc = now.AddHours(-23) }, now, TimeSpan.FromHours(24)));
        Assert.True(UpdateChecker.IsDue(new UpdateState { LastCheckUtc = now.AddHours(-24) }, now, TimeSpan.FromHours(24)));
        Assert.True(UpdateChecker.IsDue(new UpdateState { LastCheckUtc = now.AddDays(3) }, now, TimeSpan.FromHours(24)));   // dernière vérification « dans le futur »
    }
}

/// <summary>Fichier de sommes SHA-256, règle de signature, Markdown des notes.</summary>
public class UpdateSupportTests
{
    static readonly string Hash = new('a', 64);

    [Fact]
    public void ChecksumLinesAreReadLikeSha256sumWritesThem()
    {
        var text = $"{Hash}  StreamDak-1.4.0-self-contained-Setup.exe\r\n{new string('B', 64)} *StreamDak-1.4.0-framework-dependent-Setup.exe\n\nnot a line\n";
        Assert.Equal(Hash, ChecksumFile.Find(text, "StreamDak-1.4.0-self-contained-Setup.exe"));
        Assert.Equal(new string('b', 64), ChecksumFile.Find(text, "streamdak-1.4.0-framework-dependent-setup.exe"));   // casse du nom ignorée, somme en minuscules
        Assert.Null(ChecksumFile.Find(text, "StreamDak-1.4.0-other-Setup.exe"));
    }

    [Theory]
    [InlineData("abc  x.exe")]                                  // somme trop courte
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz  x.exe")]
    [InlineData("")]
    public void MalformedLinesGiveNoChecksum(string line) => Assert.Null(ChecksumFile.Find(line, "x.exe"));

    [Fact]
    public void AChecksumNameWithAPathNeverMatches() => Assert.Null(ChecksumFile.Find($"{Hash}  ..\\x.exe\n{Hash}  sub/x.exe", "x.exe"));

    [Fact]
    public async Task AFileHashMatchesTheSha256Of()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", await ChecksumFile.HashFileAsync(path, default));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(SignatureState.NotSigned, null, SignatureState.NotSigned, null, null)]                       // ni l'un ni l'autre : somme seule
    [InlineData(SignatureState.Valid, "CN=Moi", SignatureState.NotSigned, null, null)]                       // la confiance augmente
    [InlineData(SignatureState.Valid, "CN=Moi", SignatureState.Valid, "cn=moi", null)]                       // même éditeur (casse ignorée)
    [InlineData(SignatureState.Invalid, null, SignatureState.NotSigned, null, "pas valide")]
    [InlineData(SignatureState.Invalid, null, SignatureState.Valid, "CN=Moi", "pas valide")]
    [InlineData(SignatureState.NotSigned, null, SignatureState.Valid, "CN=Moi", "pas signé")]                 // une release ne perd pas sa signature
    [InlineData(SignatureState.Valid, "CN=Pirate", SignatureState.Valid, "CN=Moi", "même éditeur")]
    public void TheSignaturePolicyRefusesDowngradesAndForgeries(SignatureState installer, string? installerPublisher, SignatureState current, string? currentPublisher, string? refusal)
    {
        var result = SignaturePolicy.Refusal(new SignatureInfo(installer, installerPublisher), new SignatureInfo(current, currentPublisher));
        if (refusal == null) Assert.Null(result);
        else Assert.Contains(refusal, result);
    }

    [Fact]
    public void TheInstalledFlavorIsReadFromTheFlavorFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sd-flavor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(InstallFlavor.SelfContained, InstallFlavor.Read(dir));                                  // absent : version de développement
            File.WriteAllText(Path.Combine(dir, "flavor.txt"), "framework-dependent");
            Assert.Equal(InstallFlavor.FrameworkDependent, InstallFlavor.Read(dir));
            File.WriteAllText(Path.Combine(dir, "flavor.txt"), "n'importe quoi");
            Assert.Equal(InstallFlavor.SelfContained, InstallFlavor.Read(dir));
            Assert.Equal("StreamDak-1.4.0-self-contained-Setup.exe", InstallFlavor.SetupName(SemVer.Parse("v1.4.0"), "self-contained"));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---------- Markdown ----------

    [Fact]
    public void MarkdownGivesHeadingsParagraphsListsRulesAndCode()
    {
        var blocks = SimpleMarkdown.Parse("# Nouveautés\n\nTexte sur\ndeux lignes.\n\n## Corrections\n- un\n- deux\n  - sous-point\n1. premier\n\n---\n```\ncode 1\ncode 2\n```\nFin");
        Assert.Equal(new[]
        {
            MarkdownBlockKind.Heading, MarkdownBlockKind.Paragraph, MarkdownBlockKind.Heading, MarkdownBlockKind.ListItem, MarkdownBlockKind.ListItem, MarkdownBlockKind.ListItem,
            MarkdownBlockKind.ListItem, MarkdownBlockKind.Rule, MarkdownBlockKind.CodeBlock, MarkdownBlockKind.Paragraph,
        }, blocks.Select(b => b.Kind));
        Assert.Equal((1, "Nouveautés"), (blocks[0].Level, blocks[0].Runs[0].Text));
        Assert.Equal("Texte sur deux lignes.", blocks[1].Runs[0].Text);
        Assert.Equal(("•", 1), (blocks[5].Marker, blocks[5].Level));                                              // sous-point imbriqué
        Assert.Equal("1.", blocks[6].Marker);
        Assert.Equal("code 1\ncode 2", blocks[8].Runs[0].Text);
        Assert.True(blocks[8].Runs[0].Code);
    }

    [Fact]
    public void InlineFormattingIsBoldItalicCodeAndLinks()
    {
        var runs = SimpleMarkdown.Inline("a **gras** b *ital* c _under_ d `code` e [lien](https://x.fr/y) ![alt](http://img/a.png)");
        Assert.Contains(runs, r => r is { Text: "gras", Bold: true });
        Assert.Contains(runs, r => r is { Text: "ital", Italic: true });
        Assert.Contains(runs, r => r is { Text: "under", Italic: true });
        Assert.Contains(runs, r => r is { Text: "code", Code: true });
        Assert.Contains(runs, r => r is { Text: "lien", LinkTarget: "https://x.fr/y" });
        Assert.Contains(runs, r => r.Text == "alt" && r.LinkTarget == null);                                       // une image devient son texte alternatif
        Assert.DoesNotContain("http://img", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void HtmlSnakeCaseAndStrayMarkersStayPlainText()
    {
        Assert.Equal("<script>alert(1)</script>", string.Concat(SimpleMarkdown.Inline("<script>alert(1)</script>").Select(r => r.Text)));
        Assert.Equal("un_nom_en_snake_case", Assert.Single(SimpleMarkdown.Inline("un_nom_en_snake_case")).Text);
        Assert.Equal("2 * 3 = 6", string.Concat(SimpleMarkdown.Inline("2 * 3 = 6").Select(r => r.Text)));
        Assert.Equal("[pas un lien]", string.Concat(SimpleMarkdown.Inline("[pas un lien]").Select(r => r.Text)));
        Assert.Empty(SimpleMarkdown.Parse(null));
        Assert.Empty(SimpleMarkdown.Parse("   \n  "));
    }

    [Fact]
    public void ANoteIsBoundedInBlocks() => Assert.Equal(SimpleMarkdown.MaxBlocks, SimpleMarkdown.Parse(string.Join("\n", Enumerable.Repeat("- ligne", 5000))).Count);
}

/// <summary>Téléchargement : intégrité, redirections, reprise, coupures réseau, annulation.</summary>
public sealed class UpdateDownloaderTests : IDisposable
{
    static readonly UpdateRepo Repo = new("Dakire", "StreamDeck");
    static readonly SemVer Current = SemVer.Parse("1.0.0");
    const string Tag = "v1.4.0";
    const string SetupName = "StreamDak-1.4.0-self-contained-Setup.exe";
    static readonly string SetupUrl = $"https://github.com/Dakire/StreamDeck/releases/download/{Tag}/{SetupName}";
    static readonly string SumsUrl = $"https://github.com/Dakire/StreamDeck/releases/download/{Tag}/checksums.txt";

    readonly string _folder = Path.Combine(Path.GetTempPath(), "sd-dl-" + Guid.NewGuid().ToString("N"));
    readonly byte[] _payload = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31 % 251)).ToArray();

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    string PayloadHash => Convert.ToHexString(SHA256.HashData(_payload)).ToLowerInvariant();

    ReleaseInfo Release(long? size = null, bool withSums = true) => new(Tag, SemVer.Parse(Tag), "StreamDak", "", false, new Uri("https://github.com/Dakire/StreamDeck/releases/tag/" + Tag), null,
        new[] { new ReleaseAsset(SetupName, new Uri(SetupUrl), size ?? _payload.Length) }.Concat(withSums ? new[] { new ReleaseAsset("checksums.txt", new Uri(SumsUrl), 200) } : Array.Empty<ReleaseAsset>()).ToList());

    UpdateDownloader Downloader(FakeGitHub github) => new(new HttpClient(github), Current, _ => TimeSpan.Zero);

    /// <summary>Serveur de l'installateur : 200 entier, ou 206 si une plage est demandée ; <paramref name="cut"/> coupe la connexion après ce nombre d'octets (une fois).</summary>
    HttpResponseMessage Serve(HttpRequestMessage request, ref int cut, string? sums = null)
    {
        if (request.RequestUri!.AbsoluteUri == SumsUrl) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sums ?? $"{PayloadHash}  {SetupName}\n") };
        long from = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
        var body = _payload.AsMemory((int)from).ToArray();
        Stream stream = cut > 0 ? new CuttingStream(body, cut) : new MemoryStream(body);
        cut = 0;
        var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentLength = body.Length;
        return response;
    }

    sealed class CuttingStream : MemoryStream
    {
        readonly int _limit;
        int _given;
        public CuttingStream(byte[] data, int limit) : base(data) { _limit = limit; }
        public override int Read(byte[] buffer, int offset, int count) => ReadLimited(base.Read(buffer, offset, Math.Min(count, Math.Max(0, _limit - _given))), count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = await base.ReadAsync(buffer[..Math.Min(buffer.Length, Math.Max(0, _limit - _given))], cancellationToken);
            return ReadLimited(n, buffer.Length);
        }
        int ReadLimited(int read, int _)
        {
            if (read == 0 && _given >= _limit) throw new IOException("Connexion réinitialisée");
            _given += read;
            return read;
        }
    }

    [Fact]
    public void TheInstallerIsPickedByFlavorAndNeedsItsChecksums()
    {
        Assert.Equal(SetupName, UpdateDownloader.Select(Release(), InstallFlavor.SelfContained)!.Value.Installer.Name);
        Assert.Null(UpdateDownloader.Select(Release(), InstallFlavor.FrameworkDependent));                      // cette variante n'est pas publiée
        Assert.Null(UpdateDownloader.Select(Release(withSums: false), InstallFlavor.SelfContained));            // sans checksums.txt : jamais installable
    }

    [Fact]
    public async Task AGoodDownloadIsVerifiedReportedAndKeptUnderItsFinalName()
    {
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut);
        var reports = new List<DownloadProgress>();
        var path = await Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, new Progress<DownloadProgress>(reports.Add), default);
        Assert.Equal(Path.Combine(_folder, SetupName), path);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
        await Task.Delay(100);                                                                                  // Progress<T> publie sur le pool de threads
        Assert.Contains(reports, p => p.Received == _payload.Length && p.Fraction == 1);
        Assert.All(github.Requests, r => Assert.Equal("StreamDak/1.0.0", r.Headers.UserAgent.ToString()));
    }

    [Fact]
    public async Task AWrongHashDeletesTheFileAndRefusesTheInstallation()
    {
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut, sums: $"{new string('0', 64)}  {SetupName}\n");
        var ex = await Assert.ThrowsAsync<UpdateIntegrityException>(() => Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default));
        Assert.Contains("SHA-256", ex.Message);
        Assert.Empty(Directory.GetFiles(_folder));                                                              // ni le fichier final ni le .part
    }

    [Fact]
    public async Task ATamperedPayloadIsCaughtEvenWhenTheSizeIsRight()
    {
        var tampered = (byte[])_payload.Clone();
        tampered[1234] ^= 0xFF;
        var github = new FakeGitHub
        {
            Respond = r => r.RequestUri!.AbsoluteUri == SumsUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{PayloadHash}  {SetupName}\n") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(tampered) },
        };
        await Assert.ThrowsAsync<UpdateIntegrityException>(() => Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task AMissingChecksumEntryRefusesBeforeDownloadingTheInstaller()
    {
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut, sums: $"{PayloadHash}  une-autre-release.exe\n");
        var ex = await Assert.ThrowsAsync<UpdateIntegrityException>(() => Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default));
        Assert.Contains("somme", ex.Message);
        Assert.DoesNotContain(github.Requests, r => r.Url.AbsoluteUri == SetupUrl);
    }

    [Fact]
    public async Task AReleaseWithoutChecksumsOrFromAnotherRepositoryIsRefusedWithoutAnyRequest()
    {
        var github = new FakeGitHub();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Downloader(github).DownloadAsync(Repo, Release(withSums: false), InstallFlavor.SelfContained, _folder, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Downloader(github).DownloadAsync(new UpdateRepo("Autre", "Depot"), Release(), InstallFlavor.SelfContained, _folder, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Downloader(github).DownloadAsync(Repo, Release(size: 0), InstallFlavor.SelfContained, _folder, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Downloader(github).DownloadAsync(Repo, Release(size: UpdateDownloader.MaxInstallerBytes + 1), InstallFlavor.SelfContained, _folder, null, default));
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task GitHubRedirectsToItsCdnAreFollowedAndAnyOtherHostIsRefused()
    {
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r =>
        {
            var url = r.RequestUri!.AbsoluteUri;
            if (url == SetupUrl) return Redirect("https://objects.githubusercontent.com/cdn/file?sig=1");
            if (url.StartsWith("https://objects.githubusercontent.com/", StringComparison.Ordinal)) return Serve(new HttpRequestMessage(HttpMethod.Get, SetupUrl) { Headers = { Range = r.Headers.Range } }, ref cut);
            return Serve(r, ref cut);
        };
        await Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default);
        Assert.Contains(github.Requests, r => r.Url.Host == "objects.githubusercontent.com");

        foreach (var evil in new[] { "https://evil.example/x.exe", "http://objects.githubusercontent.com/x.exe", "https://githubusercontent.com.evil.example/x" })
        {
            var hijack = new FakeGitHub();
            int zero = 0;
            hijack.Respond = r => r.RequestUri!.AbsoluteUri == SetupUrl ? Redirect(evil) : Serve(r, ref zero);
            var folder = Path.Combine(_folder, "evil");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Downloader(hijack).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, folder, null, default));
            Assert.Contains("Redirection refusée", ex.Message);
            Assert.DoesNotContain(hijack.Requests, r => r.Url.Host is "evil.example" or "githubusercontent.com.evil.example");   // la requête vers l'hôte refusé n'est jamais envoyée
        }
    }

    [Fact]
    public async Task AnEndlessRedirectLoopStops()
    {
        var github = new FakeGitHub { Respond = r => r.RequestUri!.AbsoluteUri == SumsUrl ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{PayloadHash}  {SetupName}") } : Redirect(SetupUrl) };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default));
        Assert.Contains("redirections", ex.Message);
    }

    static HttpResponseMessage Redirect(string to)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(to);
        return r;
    }

    [Fact]
    public async Task ADroppedConnectionResumesWithARangeRequestAndEndsWithAVerifiedFile()
    {
        int cut = 120_000;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut);
        var path = await Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(path));
        var installerRequests = github.Requests.Where(r => r.Url.AbsoluteUri == SetupUrl).ToList();
        Assert.Equal(2, installerRequests.Count);
        Assert.Null(installerRequests[0].Headers.Range);
        long resumed = installerRequests[1].Headers.Range!.Ranges.Single().From!.Value;
        Assert.InRange(resumed, 1, _payload.Length - 1);                                                          // reprend là où la coupure a eu lieu, pas à zéro
    }

    [Fact]
    public async Task AServerThatIgnoresRangeRestartsFromZeroAndStillVerifies()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllBytesAsync(Path.Combine(_folder, SetupName + ".part"), _payload.AsSpan(0, 50_000).ToArray());   // reste d'un téléchargement interrompu
        var github = new FakeGitHub
        {
            Respond = r => r.RequestUri!.AbsoluteUri == SumsUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{PayloadHash}  {SetupName}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_payload) },        // 200 entier malgré la plage demandée
        };
        var path = await Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task APartialFileFromAnEarlierRunIsCompletedNotRestarted()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllBytesAsync(Path.Combine(_folder, SetupName + ".part"), _payload.AsSpan(0, 200_000).ToArray());
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut);
        var path = await Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(path));
        Assert.Equal(200_000, github.Requests.Single(r => r.Url.AbsoluteUri == SetupUrl).Headers.Range!.Ranges.Single().From);
    }

    [Fact]
    public async Task ABadResumeProducesAFileThatFailsTheHashAndIsDeleted()
    {
        Directory.CreateDirectory(_folder);
        var corrupt = _payload.AsSpan(0, 100_000).ToArray();
        corrupt[10] ^= 0xFF;                                                                                    // le début déjà téléchargé est abîmé
        await File.WriteAllBytesAsync(Path.Combine(_folder, SetupName + ".part"), corrupt);
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut);
        await Assert.ThrowsAsync<UpdateIntegrityException>(() => Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default));
        Assert.False(File.Exists(Path.Combine(_folder, SetupName + ".part")));                                  // la prochaine tentative repart propre
    }

    [Fact]
    public async Task ATransientNetworkFailureIsRetriedAndAPermanentOneGivesUp()
    {
        int cut = 0, failures = 2;
        var flaky = new FakeGitHub();
        flaky.Respond = r =>
        {
            if (r.RequestUri!.AbsoluteUri == SetupUrl && failures-- > 0) throw new HttpRequestException("réseau coupé");
            return Serve(r, ref cut);
        };
        var path = await Downloader(flaky).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default);
        Assert.True(File.Exists(path));
        Assert.Equal(3, flaky.Requests.Count(r => r.Url.AbsoluteUri == SetupUrl));

        var dead = new FakeGitHub { Respond = r => r.RequestUri!.AbsoluteUri == SumsUrl ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{PayloadHash}  {SetupName}") } : throw new HttpRequestException("hors ligne") };
        await Assert.ThrowsAsync<HttpRequestException>(() => Downloader(dead).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, Path.Combine(_folder, "dead"), null, default));
        Assert.Equal(UpdateDownloader.MaxAttempts, dead.Requests.Count(r => r.Url.AbsoluteUri == SetupUrl));
    }

    [Fact]
    public async Task ACancelledDownloadKeepsThePartialFileForALaterResume()
    {
        using var cts = new CancellationTokenSource();
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut);
        var progress = new SyncProgress(p => { if (p.Received > 100_000) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(github).DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, progress, cts.Token));
        Assert.True(File.Exists(Path.Combine(_folder, SetupName + ".part")));
        Assert.False(File.Exists(Path.Combine(_folder, SetupName)));
    }

    sealed class SyncProgress : IProgress<DownloadProgress>
    {
        readonly Action<DownloadProgress> _report;
        public SyncProgress(Action<DownloadProgress> report) { _report = report; }
        public void Report(DownloadProgress value) => _report(value);
    }

    [Fact]
    public async Task AnAlreadyVerifiedFileIsNotDownloadedAgain()
    {
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut);
        var downloader = Downloader(github);
        await downloader.DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default);
        int before = github.Requests.Count(r => r.Url.AbsoluteUri == SetupUrl);
        await downloader.DownloadAsync(Repo, Release(), InstallFlavor.SelfContained, _folder, null, default);
        Assert.Equal(before, github.Requests.Count(r => r.Url.AbsoluteUri == SetupUrl));
    }

    [Fact]
    public async Task ASizeDifferentFromTheReleaseIsAnIntegrityError()
    {
        int cut = 0;
        var github = new FakeGitHub();
        github.Respond = r => Serve(r, ref cut);
        await Assert.ThrowsAsync<UpdateIntegrityException>(() => Downloader(github).DownloadAsync(Repo, Release(size: _payload.Length - 10), InstallFlavor.SelfContained, _folder, null, default));
    }
}

/// <summary>Le service : indicateur, notes, ignorer, plus tard, installation sécurisée, échec précédent.</summary>
public sealed class UpdateServiceTests : IDisposable
{
    const string Tag = "v1.4.0";
    const string SetupName = "StreamDak-1.4.0-self-contained-Setup.exe";
    static readonly string SetupUrl = $"https://github.com/Dakire/StreamDeck/releases/download/{Tag}/{SetupName}";
    static readonly string SumsUrl = $"https://github.com/Dakire/StreamDeck/releases/download/{Tag}/checksums.txt";

    readonly string _root = Path.Combine(Path.GetTempPath(), "sd-svc-" + Guid.NewGuid().ToString("N"));
    readonly byte[] _payload = Encoding.UTF8.GetBytes(new string('P', 5000));
    DateTime _now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    public UpdateServiceTests()
    {
        foreach (var dir in new[] { "config", "app", "work" }) Directory.CreateDirectory(Path.Combine(_root, dir));
        File.WriteAllText(Path.Combine(_root, "config", UpdateSettings.FileName), """{ "owner": "Dakire", "repo": "StreamDeck" }""");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    sealed class RecordingApplier : IUpdateApplier
    {
        public List<UpdatePlan> Plans { get; } = new();
        public void Launch(UpdatePlan plan) => Plans.Add(plan);
    }

    sealed class FakeVerifier : ISignatureVerifier
    {
        public SignatureInfo Installer { get; set; } = new(SignatureState.NotSigned);
        public SignatureInfo Application { get; set; } = new(SignatureState.NotSigned);
        public SignatureInfo Verify(string path) => path.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase) ? Installer : Application;
    }

    string PayloadHash => Convert.ToHexString(SHA256.HashData(_payload)).ToLowerInvariant();

    string ReleaseJson => GitHubReleaseParsingTests.Release(Tag, body: "# Nouveautés\n- Widgets", assets: new[]
    {
        GitHubReleaseParsingTests.Asset(Tag, SetupName, _payload.Length), GitHubReleaseParsingTests.Asset(Tag, "checksums.txt", 100),
    });

    FakeGitHub Github(string? sums = null) => new()
    {
        Respond = r => r.RequestUri!.AbsoluteUri switch
        {
            var u when u.StartsWith("https://api.github.com/", StringComparison.Ordinal) => FakeGitHub.Json(ReleaseJson, "\"e\""),
            var u when u == SumsUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sums ?? $"{PayloadHash}  {SetupName}\n") },
            var u when u == SetupUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_payload) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        },
    };

    (UpdateService Service, RecordingApplier Applier, FakeVerifier Verifier, Func<int> Exits, FakeGitHub Github) Create(bool installed = true, FakeGitHub? github = null, string current = "1.0.0")
    {
        github ??= Github();
        if (installed) File.WriteAllText(Path.Combine(_root, "app", "unins000.exe"), "x");
        var env = new UpdateEnvironment(Path.Combine(_root, "config"), Path.Combine(_root, "app"), Path.Combine(_root, "work"), SemVer.Parse(current), Path.Combine(_root, "app", "StreamDak.exe"), 4242);
        var applier = new RecordingApplier();
        var verifier = new FakeVerifier();
        int exits = 0;
        var service = new UpdateService(env, new UpdateChecker(new HttpClient(github), env.Current, () => _now), new UpdateDownloader(new HttpClient(github), env.Current, _ => TimeSpan.Zero),
            verifier, applier, () => exits++, () => _now);
        return (service, applier, verifier, () => exits, github);
    }

    [Fact]
    public async Task AManualCheckShowsTheNewVersionWithItsNotes()
    {
        var (service, _, _, _, _) = Create();
        Assert.False(service.HasUpdate);
        await service.CheckAsync(manual: true);
        Assert.True(service.HasUpdate);
        Assert.True(service.ShowDetails);
        Assert.Equal("1.4.0", service.NewVersionText);
        Assert.Contains("disponible", service.StatusText);
        Assert.Equal(MarkdownBlockKind.Heading, service.Notes[0].Kind);
        Assert.Equal("1.0.0", service.CurrentVersionText);
        Assert.True(service.CanInstall);
        Assert.Equal("Dakire/StreamDeck", service.RepositoryText);
    }

    [Fact]
    public async Task UpToDateAndErrorsAreSaidNotThrown()
    {
        var (current, _, _, _, _) = Create(current: "1.4.0");
        await current.CheckAsync(manual: true);
        Assert.False(current.HasUpdate);
        Assert.Contains("dernière version", current.StatusText);

        var (offline, _, _, _, _) = Create(github: new FakeGitHub { Respond = _ => throw new HttpRequestException("off") });
        await offline.CheckAsync(manual: true);
        Assert.False(offline.HasUpdate);
        Assert.Contains("GitHub", offline.ErrorText);
        Assert.False(offline.IsChecking);
    }

    [Fact]
    public async Task TheAutomaticCheckRespectsTheSettingTheDailyLimitAndStaysSilentOnError()
    {
        var (service, _, _, _, github) = Create();
        service.SetAutoCheck(false);
        await service.CheckAsync(manual: false);
        Assert.Empty(github.Requests);                                              // désactivée

        service.SetAutoCheck(true);
        await service.CheckAsync(manual: false);
        Assert.Single(github.Requests, r => r.Url.Host == "api.github.com");
        Assert.True(service.HasUpdate);

        await service.CheckAsync(manual: false);                                    // une fois par jour
        Assert.Single(github.Requests, r => r.Url.Host == "api.github.com");
        _now = _now.AddHours(25);
        await service.CheckAsync(manual: false);
        Assert.Equal(2, github.Requests.Count(r => r.Url.Host == "api.github.com"));

        var (quiet, _, _, _, _) = Create(github: new FakeGitHub { Respond = _ => throw new HttpRequestException("off") });
        await quiet.CheckAsync(manual: false);
        Assert.Equal("", quiet.ErrorText);                                          // l'automatique n'importune pas
    }

    [Fact]
    public async Task AutomaticCheckingNeverInstallsAnything()
    {
        var (service, applier, _, exits, github) = Create();
        await service.CheckAsync(manual: false);
        Assert.True(service.HasUpdate);
        Assert.Empty(applier.Plans);
        Assert.Equal(0, exits());
        Assert.DoesNotContain(github.Requests, r => r.Url.AbsoluteUri == SetupUrl);   // pas même téléchargé
    }

    [Fact]
    public async Task IgnoringAVersionHidesItAndSurvivesARestartButANewerOneIsProposed()
    {
        var (service, _, _, _, _) = Create();
        await service.CheckAsync(manual: true);
        service.Ignore();
        Assert.False(service.HasUpdate);
        Assert.False(service.ShowDetails);

        var (restarted, _, _, _, _) = Create();
        await restarted.CheckAsync(manual: true);
        Assert.False(restarted.HasUpdate);                                          // toujours ignorée
        Assert.Contains("ignorée", restarted.StatusText);

        var state = UpdateState.Load(Path.Combine(_root, "config"));
        state.IgnoredVersion = "v1.3.0";                                            // une ancienne version ignorée ne cache pas la 1.4.0
        state.Save(Path.Combine(_root, "config"));
        var (other, _, _, _, _) = Create();
        await other.CheckAsync(manual: true);
        Assert.True(other.HasUpdate);
    }

    [Fact]
    public async Task LaterFoldsThePanelButKeepsTheIndicator()
    {
        var (service, _, _, _, _) = Create();
        await service.CheckAsync(manual: true);
        service.Later();
        Assert.False(service.ShowDetails);
        Assert.True(service.HasUpdate);
    }

    [Fact]
    public async Task TheBetaAndAutoSettingsArePersistedInUpdateJsonNotInTheDeckConfiguration()
    {
        var (service, _, _, _, github) = Create();
        service.Beta = true;
        service.AutoCheck = false;
        var saved = UpdateSettings.Load(Path.Combine(_root, "config"), Path.Combine(_root, "app"));
        Assert.True(saved.Beta);
        Assert.False(saved.AutoCheck);
        Assert.Equal("Dakire", saved.Owner);
        await service.CheckAsync(manual: true);
        Assert.StartsWith("https://api.github.com/repos/Dakire/StreamDeck/releases?per_page=", github.Requests.Single(r => r.Url.Host == "api.github.com").Url.AbsoluteUri);   // canal bêta : la liste, pré-versions comprises
    }

    [Fact]
    public async Task InstallingDownloadsVerifiesThenHandsOverToTheApplierAndClosesTheApplication()
    {
        var (service, applier, _, exits, github) = Create();
        await service.CheckAsync(manual: true);
        await service.InstallAsync();

        Assert.Equal("", service.ErrorText);
        var plan = Assert.Single(applier.Plans);
        Assert.Equal(Path.Combine(_root, "work", "1.4.0", SetupName), plan.Installer);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(plan.Installer));
        Assert.Equal((Path.Combine(_root, "app"), Path.Combine(_root, "app", "StreamDak.exe"), 4242, Path.Combine(_root, "work")), (plan.InstallDir, plan.Exe, plan.ProcessId, plan.WorkDir));
        Assert.Equal(1, exits());
        Assert.False(service.IsInstalling);
        Assert.All(github.Requests, r => Assert.Equal("https", r.Url.Scheme));
    }

    [Fact]
    public async Task AWrongHashStopsTheInstallationBeforeAnythingIsLaunched()
    {
        var (service, applier, _, exits, _) = Create(github: Github(sums: $"{new string('1', 64)}  {SetupName}\n"));
        await service.CheckAsync(manual: true);
        await service.InstallAsync();
        Assert.Contains("SHA-256", service.ErrorText);
        Assert.Empty(applier.Plans);
        Assert.Equal(0, exits());
        Assert.False(service.IsInstalling);
        Assert.True(service.HasUpdate);                                             // l'application reste ouverte, la mise à jour reste proposée
    }

    [Fact]
    public async Task AnInvalidSignatureOrALostSignatureStopsTheInstallationAndDeletesTheFile()
    {
        var (service, applier, verifier, exits, _) = Create();
        verifier.Installer = new SignatureInfo(SignatureState.Invalid);
        await service.CheckAsync(manual: true);
        await service.InstallAsync();
        Assert.Contains("signature", service.ErrorText);
        Assert.Empty(applier.Plans);
        Assert.False(File.Exists(Path.Combine(_root, "work", "1.4.0", SetupName)));

        verifier.Installer = new SignatureInfo(SignatureState.NotSigned);
        verifier.Application = new SignatureInfo(SignatureState.Valid, "CN=Moi");   // l'application est signée, pas la release
        await service.InstallAsync();
        Assert.Contains("pas signé", service.ErrorText);
        Assert.Empty(applier.Plans);
        Assert.Equal(0, exits());

        verifier.Installer = new SignatureInfo(SignatureState.Valid, "CN=Moi");
        await service.InstallAsync();
        Assert.Single(applier.Plans);
        Assert.Equal(1, exits());
    }

    [Fact]
    public async Task ACopyNotPlacedByTheInstallerOnlyOffersTheReleasePage()
    {
        var (service, applier, _, exits, github) = Create(installed: false);
        await service.CheckAsync(manual: true);
        Assert.False(service.CanInstall);
        await service.InstallAsync();
        Assert.Contains("installateur", service.ErrorText);
        Assert.Empty(applier.Plans);
        Assert.Equal(0, exits());
        Assert.DoesNotContain(github.Requests, r => r.Url.AbsoluteUri == SetupUrl);
    }

    [Fact]
    public async Task ANetworkFailureDuringTheDownloadIsReportedAndNothingIsLaunched()
    {
        var github = Github();
        var inner = github.Respond;
        github.Respond = r => r.RequestUri!.AbsoluteUri == SetupUrl ? throw new HttpRequestException("coupé") : inner(r);
        var (service, applier, _, exits, _) = Create(github: github);
        await service.CheckAsync(manual: true);
        await service.InstallAsync();
        Assert.Contains("échoué", service.ErrorText);
        Assert.Empty(applier.Plans);
        Assert.Equal(0, exits());
        Assert.False(service.IsInstalling);
    }

    [Fact]
    public async Task InstallingWithoutACheckedReleaseDoesNothing()
    {
        var (service, applier, _, exits, github) = Create();
        await service.InstallAsync();
        Assert.Empty(applier.Plans);
        Assert.Equal(0, exits());
        Assert.Empty(github.Requests);
    }

    [Fact]
    public void ThePreviousFailureMarkerIsShownOnceThenRemovedAndOldDownloadsAreCleaned()
    {
        var work = Path.Combine(_root, "work");
        File.WriteAllText(Path.Combine(work, UpdateApplier.FailedMarker), "L'installateur a échoué (code 5)");
        Directory.CreateDirectory(Path.Combine(work, "0.9.0"));
        Directory.CreateDirectory(Path.Combine(work, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(work, "2.0.0"));
        var (service, _, _, _, _) = Create();
        Assert.Contains("restaurée", service.ErrorText);
        Assert.Contains("code 5", service.ErrorText);
        Assert.False(File.Exists(Path.Combine(work, UpdateApplier.FailedMarker)));
        Assert.False(Directory.Exists(Path.Combine(work, "0.9.0")));                // déjà installées ou plus anciennes : supprimées
        Assert.False(Directory.Exists(Path.Combine(work, "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(work, "2.0.0")));                 // une version plus récente en attente reste

        Assert.Equal("", Create().Service.ErrorText);                               // la deuxième fois, plus rien à dire
    }

    [Fact]
    public async Task AnUnconfiguredServiceExplainsItselfAndMakesNoRequest()
    {
        File.Delete(Path.Combine(_root, "config", UpdateSettings.FileName));
        var (service, _, _, _, github) = Create();
        Assert.False(service.IsConfigured);
        await service.CheckAsync(manual: true);
        Assert.Contains("update.json", service.StatusText);
        Assert.Empty(github.Requests);
    }
}

/// <summary>L'applicateur réel (PowerShell) : sauvegarde, installation, relance, restauration en cas d'échec.</summary>
public sealed class UpdateApplierTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "sd-apply-" + Guid.NewGuid().ToString("N"));

    public UpdateApplierTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    /// <summary>Un « dossier d'installation » avec un fichier, un faux installateur (.cmd) qui le modifie puis rend le code demandé, un faux exécutable (.cmd) qui note sa relance.</summary>
    (UpdatePlan Plan, string App, string Relaunched) Arrange(int installerExitCode)
    {
        var install = Path.Combine(_root, "install");
        var work = Path.Combine(_root, "work");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "app.txt"), "ancienne version");
        File.WriteAllText(Path.Combine(install, "StreamDak.cmd"), $"@echo relance> \"{Path.Combine(_root, "relaunched.txt")}\"\r\n");
        var setup = Path.Combine(_root, "Setup.cmd");
        File.WriteAllText(setup, $"@echo off\r\necho nouvelle version> \"{Path.Combine(install, "app.txt")}\"\r\necho %* > \"{Path.Combine(_root, "setup-args.txt")}\"\r\nexit /b {installerExitCode}\r\n");
        return (new UpdatePlan(setup, install, Path.Combine(install, "StreamDak.cmd"), 0, work), Path.Combine(install, "app.txt"), Path.Combine(_root, "relaunched.txt"));
    }

    static async Task WaitForFile(string path, int seconds = 20)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!File.Exists(path) && DateTime.UtcNow < limit) await Task.Delay(100);
    }

    [Fact]
    public async Task ASuccessfulInstallReplacesTheFilesRelaunchesAndRemovesTheBackup()
    {
        var (plan, app, relaunched) = Arrange(0);
        using var process = UpdateApplier.Start(plan);
        Assert.True(process.WaitForExit(60_000), "l'applicateur ne s'est pas terminé");
        await WaitForFile(relaunched);

        Assert.Equal("nouvelle version", (await File.ReadAllTextAsync(app)).Trim());
        Assert.True(File.Exists(relaunched), "l'application doit être relancée");
        Assert.False(Directory.Exists(Path.Combine(plan.WorkDir, "backup")));
        Assert.False(File.Exists(Path.Combine(plan.WorkDir, UpdateApplier.FailedMarker)));
        var args = await File.ReadAllTextAsync(Path.Combine(_root, "setup-args.txt"));
        Assert.Contains("/SILENT", args);
        Assert.Contains("/NORESTART", args);
        Assert.Contains($"/DIR=\"{plan.InstallDir}\"", args);                       // le dossier choisi est conservé
        Assert.Contains("update.log", string.Join(" ", Directory.GetFiles(plan.WorkDir).Select(Path.GetFileName)));
    }

    [Fact]
    public async Task AFailedInstallRestoresTheOldVersionRelaunchesItAndLeavesAFailureMarker()
    {
        var (plan, app, relaunched) = Arrange(5);
        using var process = UpdateApplier.Start(plan);
        Assert.True(process.WaitForExit(60_000));
        await WaitForFile(relaunched);

        Assert.Equal("ancienne version", (await File.ReadAllTextAsync(app)).Trim());            // l'installateur avait écrasé le fichier : la sauvegarde est remise
        Assert.True(File.Exists(relaunched), "l'ancienne version doit être relancée");
        var marker = Path.Combine(plan.WorkDir, UpdateApplier.FailedMarker);
        Assert.True(File.Exists(marker));
        Assert.Contains("code 5", await File.ReadAllTextAsync(marker));
    }

    [Fact]
    public async Task TheApplierWaitsForTheApplicationToCloseBeforeInstalling()
    {
        var (plan, app, relaunched) = Arrange(0);
        using var running = Process.Start(new ProcessStartInfo("ping.exe", "-n 4 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        var started = DateTime.UtcNow;
        using var process = UpdateApplier.Start(plan with { ProcessId = running.Id });
        await Task.Delay(800);
        Assert.Equal("ancienne version", (await File.ReadAllTextAsync(app)).Trim());            // l'application tourne encore : rien n'est touché
        Assert.True(process.WaitForExit(60_000));
        await WaitForFile(relaunched);
        Assert.Equal("nouvelle version", (await File.ReadAllTextAsync(app)).Trim());
        Assert.True(DateTime.UtcNow - started > TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PathsWithSpacesAndQuotesStayDataNotCommands()
    {
        var (plan, app, relaunched) = Arrange(0);
        var spaced = Path.Combine(_root, "dossier avec espaces & (parentheses)");
        Directory.Move(plan.InstallDir, spaced);
        File.WriteAllText(Path.Combine(_root, "Setup.cmd"), $"@echo off\r\necho nouvelle version> \"{Path.Combine(spaced, "app.txt")}\"\r\nexit /b 0\r\n");
        var moved = plan with { InstallDir = spaced, Exe = Path.Combine(spaced, "StreamDak.cmd") };
        using var process = UpdateApplier.Start(moved);
        Assert.True(process.WaitForExit(60_000));
        await WaitForFile(relaunched);
        var log = File.Exists(Path.Combine(plan.WorkDir, "update.log")) ? await File.ReadAllTextAsync(Path.Combine(plan.WorkDir, "update.log")) : "(pas de journal)";
        Assert.True("nouvelle version" == (await File.ReadAllTextAsync(Path.Combine(spaced, "app.txt"))).Trim(), log);
        Assert.False(File.Exists(app));
    }

    [Fact]
    public void TheScriptIsAFixedTextWithNoNetworkAccess()
    {
        Assert.DoesNotContain("Invoke-WebRequest", UpdateApplier.Script);
        Assert.DoesNotContain("DownloadString", UpdateApplier.Script);
        Assert.DoesNotContain("Invoke-Expression", UpdateApplier.Script);
        Assert.DoesNotContain("iex ", UpdateApplier.Script);
    }
}

/// <summary>Signature Authenticode réelle (WinVerifyTrust) sur de vrais fichiers.</summary>
public sealed class AuthenticodeVerifierTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "sd-sig-" + Guid.NewGuid().ToString("N"));

    public AuthenticodeVerifierTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static string? SignedMicrosoftBinary()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application");
        return Directory.Exists(root) ? Directory.EnumerateFiles(root, "msedgewebview2.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    [Fact]
    public void ASignedMicrosoftFileIsValidAndNamesItsPublisher()
    {
        var signed = SignedMicrosoftBinary();
        if (signed == null) return;                                                  // pas de WebView2 sur cette machine : rien à comparer
        var info = StreamDak.Platform.Windows.WindowsPlatform.CreateSignatureVerifier().Verify(signed);
        Assert.Equal(SignatureState.Valid, info.State);
        Assert.Contains("Microsoft Corporation", info.Publisher);
    }

    [Fact]
    public void ATamperedSignedFileIsInvalid()
    {
        var signed = SignedMicrosoftBinary();
        if (signed == null) return;
        var copy = Path.Combine(_dir, "msedgewebview2.exe");
        var bytes = File.ReadAllBytes(signed);
        bytes[bytes.Length / 3] ^= 0xFF;                                             // un octet de code change : l'empreinte ne correspond plus
        File.WriteAllBytes(copy, bytes);
        Assert.Equal(SignatureState.Invalid, StreamDak.Platform.Windows.WindowsPlatform.CreateSignatureVerifier().Verify(copy).State);
    }

    [Fact]
    public void AnUnsignedFileMissingFileAndNonExecutableAreNeverValid()
    {
        var verifier = StreamDak.Platform.Windows.WindowsPlatform.CreateSignatureVerifier();
        Assert.Equal(SignatureState.NotSigned, verifier.Verify(typeof(AuthenticodeVerifierTests).Assembly.Location).State);   // notre propre DLL de test
        Assert.Equal(SignatureState.Invalid, verifier.Verify(Path.Combine(_dir, "absent.exe")).State);
        var text = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(text, "pas un exécutable");
        Assert.NotEqual(SignatureState.Valid, verifier.Verify(text).State);
    }
}
