using System;
using System.Collections.Generic;
using Wpf.Ui.Controls;

namespace TouchDeck;

/// <summary>
/// Icônes des boutons : noms Fluent (« Play24 »…) pour un rendu minimaliste. Les anciens emojis connus sont
/// affichés avec l'icône Fluent équivalente (sans toucher à la configuration), sauf si IconStyle = "emoji".
/// </summary>
public static class IconCatalog
{
    /// <summary>true : les emojis connus sont remplacés par leur équivalent Fluent à l'affichage.</summary>
    public static bool UseFluentForEmoji { get; set; } = true;

    /// <summary>Icônes proposées dans l'éditeur de bouton.</summary>
    public static readonly SymbolRegular[] Picker =
    {
        SymbolRegular.Play24, SymbolRegular.Pause24, SymbolRegular.Previous24, SymbolRegular.Next24,
        SymbolRegular.Speaker224, SymbolRegular.Speaker124, SymbolRegular.SpeakerMute24, SymbolRegular.MusicNote224,
        SymbolRegular.Mic24, SymbolRegular.Headphones24, SymbolRegular.Video24, SymbolRegular.Camera24,
        SymbolRegular.Games24, SymbolRegular.Desktop24, SymbolRegular.Laptop24, SymbolRegular.Tv24,
        SymbolRegular.Home24, SymbolRegular.Star24, SymbolRegular.Heart24, SymbolRegular.Bookmark24,
        SymbolRegular.Globe24, SymbolRegular.Link24, SymbolRegular.Search24, SymbolRegular.Mail24,
        SymbolRegular.Chat24, SymbolRegular.Call24, SymbolRegular.CalendarLtr24, SymbolRegular.Document24,
        SymbolRegular.Folder24, SymbolRegular.Notepad24, SymbolRegular.Calculator24, SymbolRegular.Clipboard24,
        SymbolRegular.Keyboard24, SymbolRegular.Code24, SymbolRegular.Flash24, SymbolRegular.Rocket24,
        SymbolRegular.Wrench24, SymbolRegular.Settings24, SymbolRegular.Lightbulb24, SymbolRegular.Alert24,
        SymbolRegular.LockClosed24, SymbolRegular.Key24, SymbolRegular.Eye24, SymbolRegular.Person24,
        SymbolRegular.Cloud24, SymbolRegular.ArrowSync24, SymbolRegular.ArrowDownload24, SymbolRegular.Power24,
    };

    static readonly Dictionary<string, SymbolRegular> EmojiMap = new()
    {
        ["⏯"] = SymbolRegular.Play24, ["▶"] = SymbolRegular.Play24, ["⏸"] = SymbolRegular.Pause24,
        ["⏭"] = SymbolRegular.Next24, ["⏮"] = SymbolRegular.Previous24,
        ["🔊"] = SymbolRegular.Speaker224, ["🔉"] = SymbolRegular.Speaker124, ["🔇"] = SymbolRegular.SpeakerMute24,
        ["🎵"] = SymbolRegular.MusicNote224, ["🎤"] = SymbolRegular.Mic24, ["🎧"] = SymbolRegular.Headphones24,
        ["🎬"] = SymbolRegular.Video24, ["🎥"] = SymbolRegular.Video24, ["📷"] = SymbolRegular.Camera24,
        ["🎮"] = SymbolRegular.Games24, ["🖥"] = SymbolRegular.Desktop24, ["💻"] = SymbolRegular.Laptop24,
        ["📺"] = SymbolRegular.Tv24, ["🏠"] = SymbolRegular.Home24, ["⭐"] = SymbolRegular.Star24,
        ["🌐"] = SymbolRegular.Globe24, ["🔍"] = SymbolRegular.Search24, ["✉"] = SymbolRegular.Mail24,
        ["📧"] = SymbolRegular.Mail24, ["💬"] = SymbolRegular.Chat24, ["📞"] = SymbolRegular.Call24,
        ["☎"] = SymbolRegular.Call24, ["📅"] = SymbolRegular.CalendarLtr24, ["📄"] = SymbolRegular.Document24,
        ["📁"] = SymbolRegular.Folder24, ["📝"] = SymbolRegular.Notepad24, ["🧮"] = SymbolRegular.Calculator24,
        ["⌨"] = SymbolRegular.Keyboard24, ["⚡"] = SymbolRegular.Flash24, ["🚀"] = SymbolRegular.Rocket24,
        ["🔧"] = SymbolRegular.Wrench24, ["🛠"] = SymbolRegular.Wrench24, ["⚙"] = SymbolRegular.Settings24,
        ["💡"] = SymbolRegular.Lightbulb24, ["🔔"] = SymbolRegular.Alert24, ["🔒"] = SymbolRegular.LockClosed24,
        ["🔐"] = SymbolRegular.LockClosed24, ["🔑"] = SymbolRegular.Key24, ["🗝"] = SymbolRegular.Key24,
        ["📌"] = SymbolRegular.Pin24, ["🕶"] = SymbolRegular.Eye24, ["➕"] = SymbolRegular.Add24,
        ["✖"] = SymbolRegular.Dismiss24, ["➡"] = SymbolRegular.ArrowRight24, ["⬅"] = SymbolRegular.ArrowLeft24,
        ["↩"] = SymbolRegular.ArrowUndo24, ["🔄"] = SymbolRegular.ArrowSync24, ["📊"] = SymbolRegular.DataBarVertical24,
        ["🗺"] = SymbolRegular.Map24, ["✂"] = SymbolRegular.Cut24, ["⏻"] = SymbolRegular.Power24,
    };

    /// <summary>Résout le texte d'icône d'un bouton en icône Fluent : nom exact, ou emoji connu.</summary>
    public static bool TryResolve(string? icon, out SymbolRegular symbol)
    {
        symbol = default;
        if (string.IsNullOrWhiteSpace(icon)) return false;
        icon = icon.Trim();
        if (char.IsLetter(icon[0]) && icon.Length > 2 && Enum.TryParse(icon, out SymbolRegular named) && Enum.IsDefined(named))
        {
            symbol = named;
            return true;
        }
        if (!UseFluentForEmoji) return false;
        return EmojiMap.TryGetValue(icon.Replace("️", ""), out symbol);
    }
}
