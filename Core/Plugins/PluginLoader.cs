using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using StreamDak.Integrations;

namespace StreamDak;

/// <summary>Un plugin chargé : son nom, son fichier et les intégrations qu'il apporte.</summary>
/// <param name="Name">Nom du dossier du plugin.</param>
/// <param name="Path">Fichier .dll chargé.</param>
/// <param name="Sha256">Empreinte du fichier, écrite au journal : tout changement est visible.</param>
/// <param name="Integrations">Types d'intégration (publics, non abstraits, constructeur sans paramètre).</param>
public sealed record LoadedPlugin(string Name, string Path, string Sha256, IReadOnlyList<Type> Integrations);

/// <summary>
/// Chargement des plugins posés par l'utilisateur dans <c>plugins\&lt;nom&gt;\&lt;nom&gt;.dll</c> du dossier de configuration. Un plugin est une
/// bibliothèque .NET 10 qui référence <c>StreamDak.Core</c> et contient une ou plusieurs classes <see cref="IIntegration"/>.
/// Chaque plugin vit dans son contexte de chargement (ses dépendances ne se mélangent pas à celles de StreamDak ni des autres
/// plugins) ; <c>StreamDak.Core</c> est partagé. <b>Un plugin s'exécute avec les droits de l'utilisateur</b> : seul du code de
/// confiance doit être posé dans ce dossier. StreamDak ne télécharge jamais de plugin.
/// </summary>
public static class PluginLoader
{
    /// <summary>Nom du dossier des plugins dans le dossier de configuration.</summary>
    public const string FolderName = "plugins";

    sealed class PluginContext : AssemblyLoadContext
    {
        readonly AssemblyDependencyResolver _resolver;

        public PluginContext(string mainDll) : base("plugin:" + Path.GetFileNameWithoutExtension(mainDll), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainDll);
        }

        protected override Assembly? Load(AssemblyName name)
        {
            // Le contrat (StreamDak.Core) vient de l'application : ainsi un IIntegration du plugin est bien un IIntegration de StreamDak
            if (name.Name == typeof(IIntegration).Assembly.GetName().Name) return null;
            var path = _resolver.ResolveAssemblyToPath(name);
            return path == null ? null : LoadFromAssemblyPath(path);
        }
    }

    /// <summary>Charge tous les plugins du dossier. Un plugin illisible est ignoré et consigné : il ne bloque ni les autres ni le démarrage.</summary>
    public static IReadOnlyList<LoadedPlugin> Load(string pluginsDir, PluginTrust? trust = null)
    {
        var loaded = new List<LoadedPlugin>();
        if (trust != null) Directory.CreateDirectory(pluginsDir);   // la liste d'autorisation vit ici : un plugin posé plus tard ne doit pas être autorisé d'office
        if (!Directory.Exists(pluginsDir)) return loaded;
        var folders = Directory.EnumerateDirectories(pluginsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        // Première utilisation de la liste : les plugins déjà posés étaient acceptés jusque-là, ils le restent (empreintes relevées avant tout chargement)
        if (trust is { FileExists: false, IsValid: true })
        {
            var existing = folders.Where(f => File.Exists(Path.Combine(f, Path.GetFileName(f) + ".dll"))).Select(f => (Path.GetFileName(f), PluginTrust.FolderHash(f))).ToList();
            try { trust.TrustAndSave(existing); Log.Info($"Plugins : liste d'autorisation créée ({existing.Count} plugin(s) déjà présent(s))"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Plugins : liste d'autorisation impossible à écrire ({ex.Message})"); }
        }
        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            var dll = Path.Combine(folder, name + ".dll");
            if (!File.Exists(dll)) { Log.Warn($"Plugin « {name} » ignoré : {name}.dll est introuvable dans son dossier"); continue; }
            try
            {
                if (trust != null)
                {
                    // Vérifié avant le chargement : le code d'un plugin non autorisé ne s'exécute jamais
                    var folderHash = PluginTrust.FolderHash(folder);
                    if (!trust.IsTrusted(name, folderHash))
                    {
                        Log.Warn($"Plugin « {name} » refusé : son empreinte n'est pas dans {PluginTrust.FileName}. Si tu fais confiance à ce code, ajoute \"{name}\": \"{folderHash}\" au fichier {FolderName}/{PluginTrust.FileName}.");
                        continue;
                    }
                }
                var assembly = new PluginContext(dll).LoadFromAssemblyPath(dll);
                var types = FindIntegrations(assembly);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)));
                if (types.Count == 0) { Log.Warn($"Plugin « {name} » ignoré : aucune intégration (classe IIntegration publique avec un constructeur sans paramètre)"); continue; }
                Log.Info($"Plugin « {name} » chargé : {string.Join(", ", types.Select(t => t.Name))} (SHA-256 {hash[..16]}…)");
                loaded.Add(new LoadedPlugin(name, dll, hash, types));
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ReflectionTypeLoadException or IOException or InvalidOperationException or TypeLoadException or UnauthorizedAccessException)
            {
                Log.Warn($"Plugin « {name} » ignoré : {ex.Message}");
            }
        }
        return loaded;
    }

    /// <summary>Les types d'intégration d'un assembly : publics, non abstraits, avec un constructeur public sans paramètre.</summary>
    public static IReadOnlyList<Type> FindIntegrations(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(t => typeof(IIntegration).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) != null)
            .ToList();
}
