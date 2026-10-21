using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TouchDeck;

/// <summary>
/// Ce qu'on dépose sur une case vide du deck (fichiers, raccourcis .lnk, exécutables, dossiers, adresses glissées d'un navigateur)
/// transformé en boutons « Lancer » ou « Ouvrir », avec l'icône de l'application quand il y en a une. Aucun dialogue : ce qui ne
/// peut pas être ajouté est rapporté dans <see cref="DropResult.Problems"/> et affiché dans la barre d'état.
/// </summary>
public static class DropPayload
{
    /// <summary>Nombre maximal d'éléments ajoutés par un seul dépôt (un dossier entier déposé ne remplit pas la page).</summary>
    public const int MaxItems = 12;

    /// <summary>Boutons à créer et raisons des éléments refusés.</summary>
    public sealed record DropResult(IReadOnlyList<DeckButton> Buttons, IReadOnlyList<string> Problems);

    /// <summary>true si l'objet glissé contient des fichiers ou du texte (sans le lire en entier : sert au curseur pendant le survol).</summary>
    public static bool CanAccept(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(UrlFormat) || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);

    const string UrlFormat = "UniformResourceLocatorW";

    /// <summary>Lit l'objet déposé. Les fichiers l'emportent sur le texte (un glisser d'Explorateur fournit parfois les deux).</summary>
    public static DropResult Read(IDataObject data)
    {
        var buttons = new List<DeckButton>();
        var problems = new List<string>();
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            foreach (var file in files.Take(MaxItems))
            {
                if (DroppedItem.TryFromPath(file, out var button, out var error)) { AttachIcon(button, file); buttons.Add(button); }
                else problems.Add($"{Path.GetFileName(file)} : {error}");
            }
            if (files.Length > MaxItems) problems.Add($"Seuls les {MaxItems} premiers éléments ont été ajoutés");
        }
        else if (ReadText(data) is { Length: > 0 } text)
        {
            if (DroppedItem.TryFromText(text, out var button, out var error)) buttons.Add(button);
            else problems.Add(error);
        }
        else problems.Add("Rien à ajouter : dépose un fichier, un dossier ou une adresse web");
        return new DropResult(buttons, problems);
    }

    /// <summary>Adresse ou texte déposé : le format « adresse » des navigateurs d'abord, sinon la première ligne non vide du texte.</summary>
    static string? ReadText(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(UrlFormat) && data.GetData(UrlFormat) is MemoryStream stream)
            {
                var text = Encoding.Unicode.GetString(stream.ToArray()).TrimEnd('\0').Trim();
                if (text.Length > 0) return text;
            }
            var raw = data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string
                : data.GetDataPresent(DataFormats.Text) ? data.GetData(DataFormats.Text) as string : null;
            return raw?.Split('\r', '\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Warn("Lecture de l'élément déposé impossible : " + ex.Message);
            return null;
        }
    }

    /// <summary>Icône de l'application (ou du type de fichier) enregistrée comme image du bouton ; sans effet si elle est introuvable.</summary>
    static void AttachIcon(DeckButton button, string path)
    {
        if (!File.Exists(path)) return;   // un dossier garde son icône Fluent
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            button.ImagePath = IconStore.Store(bitmap);
            button.ImageFit = "contain";   // une icône de 32 px ne doit pas être étirée sur toute la touche
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or NotSupportedException or System.ComponentModel.Win32Exception
                                       or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Log.Warn($"Icône de {Path.GetFileName(path)} introuvable : {ex.Message}");
        }
    }
}
