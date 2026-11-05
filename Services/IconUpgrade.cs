using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StreamDak;

/// <summary>
/// Les versions précédentes enregistraient l'icône d'une application en 32 px (<c>ExtractAssociatedIcon</c>), floue sur une touche. Au démarrage, chaque image
/// d'application encore petite est comparée à ce que l'ancien chemin produirait pour le programme du bouton : si c'est exactement elle (même nom de hachage),
/// elle est remplacée par la grande icône (256 px). Une image choisie par l'utilisateur n'est jamais touchée, l'ancienne image reste dans <c>Icons\</c>.
/// Le remplacement est un réglage (hors historique d'annulation) : rien de visible ne change, sauf la netteté.
/// </summary>
public static class IconUpgrade
{
    /// <summary>Côté (pixels) en dessous duquel une image d'application est candidate au remplacement.</summary>
    public const int SmallIconPixels = 48;

    /// <summary>Bouton candidat : image « contenue » petite, et un programme existant dans ses étapes.</summary>
    public sealed record Candidate(string ImagePath, string Program);

    /// <summary>Candidats d'une configuration (un par couple image + programme).</summary>
    /// <param name="config">Configuration lue.</param>
    /// <param name="pixelSize">Côté le plus long d'une image enregistrée, ou null si elle est illisible.</param>
    /// <param name="program">Programme désigné par les étapes d'un bouton, ou null.</param>
    public static IReadOnlyList<Candidate> Candidates(DeckConfig config, Func<string, int?> pixelSize, Func<DeckButton, string?> program) =>
        config.Profiles.SelectMany(p => p.Pages.SelectMany(pg => DeckDocument.AllButtons(pg.Buttons)))
            .Where(b => b.ImageFit == "contain" && IconStore.IsValidName(b.ImagePath) && pixelSize(b.ImagePath!) is <= SmallIconPixels)
            .Select(b => program(b) is { } exe ? new Candidate(b.ImagePath!, exe) : null)
            .OfType<Candidate>().Distinct().ToList();

    /// <summary>Remplacements à faire : ancienne image reconnue (même nom que ce que l'ancien chemin produit) → nouvelle image enregistrée.</summary>
    public static IReadOnlyDictionary<Candidate, string> Plan(IEnumerable<Candidate> candidates, Func<string, string?> legacyName, Func<string, string?> store)
    {
        var plan = new Dictionary<Candidate, string>();
        foreach (var c in candidates)
        {
            if (legacyName(c.Program) != c.ImagePath) continue;   // image d'une autre origine (importée, autre version de l'icône) : gardée
            if (store(c.Program) is { } name && name != c.ImagePath) plan[c] = name;
        }
        return plan;
    }

    /// <summary>Applique les remplacements aux boutons qui ont encore l'ancienne image et le même programme ; retourne le nombre de boutons modifiés.</summary>
    public static int Apply(DeckConfig config, IReadOnlyDictionary<Candidate, string> plan, Func<DeckButton, string?> program)
    {
        int changed = 0;
        foreach (var b in config.Profiles.SelectMany(p => p.Pages.SelectMany(pg => DeckDocument.AllButtons(pg.Buttons))))
            if (b.ImagePath != null && b.ImageFit == "contain" && program(b) is { } exe && plan.TryGetValue(new Candidate(b.ImagePath, exe), out var name))
            {
                b.ImagePath = name;
                changed++;
            }
        return changed;
    }

    /// <summary>Programme d'un bouton, comme pour « Icône de l'application ».</summary>
    public static string? ProgramOf(DeckButton b) => AppIcon.FirstProgram(b.Actions);

    /// <summary>Côté le plus long d'une image enregistrée.</summary>
    static int? PixelSize(string name) => IconStore.Load(name) is BitmapSource s ? Math.Max(s.PixelWidth, s.PixelHeight) : null;

    /// <summary>
    /// Lance la vérification sur un thread STA d'arrière-plan (l'extraction d'icônes passe par le shell), puis applique les remplacements sur le thread de
    /// l'interface par <see cref="DeckDocument.UpdateSettings"/>.
    /// </summary>
    public static void Start(DeckDocument document, Dispatcher dispatcher)
    {
        var config = document.Config;
        var thread = new Thread(() =>
        {
            try
            {
                var plan = Plan(Candidates(config, PixelSize, ProgramOf), AppIcon.LegacyName, AppIcon.Store);
                if (plan.Count == 0) return;
                dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    int changed = 0;
                    document.UpdateSettings(c => changed = Apply(c, plan, ProgramOf), "Icônes d'applications");
                    Log.Info($"Icônes d'applications agrandies : {changed} bouton(s), {plan.Count} image(s)");
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
            {
                Log.Warn("Agrandissement des icônes d'applications : " + ex.Message);
            }
        }) { IsBackground = true, Name = "Icônes d'applications", Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
