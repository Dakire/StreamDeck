using System.IO;
using StreamDak.Platform.Windows;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Les secrets des intégrations sont chiffrés pour l'utilisateur courant et n'apparaissent jamais en clair.</summary>
public sealed class SecretStoreTests : System.IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "td-secrets-" + System.Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void ASecretRoundTripsAndTheFileNeverContainsItInClear()
    {
        var store = new DpapiSecretStore(_dir);
        Assert.Null(store.Get("integration.obs.password"));
        store.Set("integration.obs.password", "Mot de passe très secret é");
        Assert.Equal("Mot de passe très secret é", store.Get("integration.obs.password"));

        var file = File.ReadAllText(Path.Combine(_dir, "secrets.dat"));
        Assert.DoesNotContain("secret", file);
        Assert.Contains("integration.obs.password", file);   // la clé n'est pas un secret, la valeur si

        Assert.Equal("Mot de passe très secret é", new DpapiSecretStore(_dir).Get("integration.obs.password"));   // relu depuis le disque
    }

    [Fact]
    public void AnEmptyValueRemovesTheSecretAndOthersAreKept()
    {
        var store = new DpapiSecretStore(_dir);
        store.Set("a", "1"); store.Set("b", "2");
        store.Set("a", "");
        Assert.Null(store.Get("a"));
        Assert.Equal("2", store.Get("b"));
        Assert.Null(new DpapiSecretStore(_dir).Get("a"));
    }

    [Fact]
    public void CorruptedOrForeignDataIsIgnoredWithoutThrowing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "secrets.dat"), """{ "k": "AAAA", "j": "pas du base64 !" }""");
        var store = new DpapiSecretStore(_dir);
        Assert.Null(store.Get("k"));          // chiffré par un autre compte : illisible
        Assert.Null(store.Get("j"));
        File.WriteAllText(Path.Combine(_dir, "secrets.dat"), "{ pas du json");
        Assert.Null(new DpapiSecretStore(_dir).Get("k"));
        var recovering = new DpapiSecretStore(_dir);
        recovering.Set("k", "v");              // un fichier abîmé ne bloque pas l'écriture suivante
        Assert.Equal("v", recovering.Get("k"));
    }

    [Fact]
    public void SecretsEncryptedBeforeTheRenameStillDecrypt()
    {
        // secrets.dat écrit par TouchDeck : l'entropie DPAPI d'origine ne doit jamais changer avec le nom du produit
        Directory.CreateDirectory(_dir);
        var legacy = System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes("jeton"),
            System.Text.Encoding.UTF8.GetBytes("TouchDeck.secrets.v1"), System.Security.Cryptography.DataProtectionScope.CurrentUser);
        File.WriteAllText(Path.Combine(_dir, "secrets.dat"), System.Text.Json.JsonSerializer.Serialize(
            new System.Collections.Generic.Dictionary<string, string> { ["integration.x.token"] = System.Convert.ToBase64String(legacy) }));
        Assert.Equal("jeton", new DpapiSecretStore(_dir).Get("integration.x.token"));
    }

    [Fact]
    public void SecretsStayOutOfTheLogs()
    {
        using var log = new MemoryLogSink();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "secrets.dat"), """{ "integration.x.token": "AAAA" }""");
        new DpapiSecretStore(_dir).Get("integration.x.token");
        Assert.True(log.Contains("integration.x.token"));      // la clé est consignée…
        Assert.False(log.Contains("AAAA"));                    // …jamais la valeur
    }
}