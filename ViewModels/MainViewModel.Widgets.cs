using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using StreamDak.Integrations;

namespace StreamDak.ViewModels;

/// <summary>Widgets du deck : création des tuiles d'affichage (données, niveaux, mélangeur, web, agenda, système), cycle de vie des relevés et saisie dans une page web.</summary>
public partial class MainViewModel
{
    /// <summary>Empreinte des réglages d'un widget : une tuile n'est gardée que si rien de ce qui la décrit n'a changé.</summary>
    static string WidgetSignature(DeckButton def) =>
        def.Widget + "|" + def.Color + "|" + string.Join(";", (def.WidgetOptions ?? new()).Where(o => o.Key != WidgetOptionKeys.Zoom)   // le zoom se règle sur la tuile : la garder
            .OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key + "=" + o.Value));

    /// <summary>Tuile d'affichage d'un widget (heure, agenda ou page web).</summary>
    WidgetTileViewModel CreateWidgetTile(DeckButton def, Place place, int index)
    {
        var edit = new RelayCommand(() => OpenTileMenu(place, index));            // clic droit, appui long
        string name = WidgetTypes.Find(def.Widget)?.Name ?? "Widget";
        WidgetTileViewModel tile = def.Widget switch
        {
            _ when _widgets.Find(def.Widget) is { } data => CreateDataWidget(data, def, edit, name),
            WidgetTypes.Agenda => new AgendaWidgetViewModel { Button = def, Edit = edit, AccessibleName = name },
            WidgetTypes.Web => CreateWebWidget(def, edit, name, place, index),
            WidgetTypes.Level => CreateLevelWidget(def, edit, name),
            WidgetTypes.Mixer => CreateMixerWidget(def, edit, name),
            WidgetTypes.Cpu or WidgetTypes.Ram => new SystemMeterWidgetViewModel { Button = def, Edit = edit, AccessibleName = name },
            _ => new ClockWidgetViewModel { Button = def, Edit = edit, AccessibleName = name },
        };
        tile.Face = TileFaceData.From(def);
        if (tile is ClockWidgetViewModel clock) clock.Update(DateTime.Now);
        else if (tile is AgendaWidgetViewModel agenda) RefreshAgenda(agenda);
        else if (tile is SystemMeterWidgetViewModel meter) RefreshSystem(meter);
        return tile;
    }

    /// <summary>Tuile d'un widget de données : ses relevés démarrent ici, et s'arrêtent quand la tuile quitte la grille (<see cref="ReleaseDataWidgets"/>).</summary>
    DataWidgetViewModel CreateDataWidget(IWidget widget, DeckButton def, System.Windows.Input.ICommand edit, string name)
    {
        var options = new Dictionary<string, string>(def.WidgetOptions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var context = new WidgetContext { Options = options, Http = _engine.Context.Http, GetValue = _integrations.GetValue };
        var tile = new DataWidgetViewModel(widget, context, Economize(WidgetRegistry.IntervalFor(widget.Info, options)), a => _ui.BeginInvoke(a))
        {
            Button = def, Edit = edit, AccessibleName = name,
        };
        _dataWidgets.Add(tile);
        tile.SetPaused(!_active);
        tile.Start();
        return tile;
    }

    /// <summary>Tuile d'un niveau audio (roue ou curseur) : sa lecture démarre ici et s'arrête quand la tuile quitte la grille.</summary>
    LevelWidgetViewModel CreateLevelWidget(DeckButton def, System.Windows.Input.ICommand edit, string name)
    {
        var source = StreamDak.Audio.LevelSources.Create(def.Option(StreamDak.Audio.LevelOptions.Source), _integrations, _engine.Context.Platform.Audio);
        var tile = new LevelWidgetViewModel(source, StreamDak.Audio.LevelShapes.Parse(def.Option(StreamDak.Audio.LevelOptions.Shape)), a => _ui.BeginInvoke(a), resetLevel: ResetLevelOf(def))
        {
            Button = def, Edit = edit, AccessibleName = source == null ? name : $"{name} : {source.Title}",
            ErrorReported = message => SetStatus(message),
        };
        _levelWidgets.Add(tile);
        tile.SetPaused(!_active);
        tile.Start();
        return tile;
    }

    static int? ResetLevelOf(DeckButton def) => def.Option(StreamDak.Audio.LevelOptions.Reset).Length > 0 ? def.OptionInt(StreamDak.Audio.LevelOptions.Reset, 100, 0, 100) : null;

    /// <summary>Mélangeur : une ligne de niveau par cible du réglage « sources » ; les relevés démarrent ici et s'arrêtent quand la tuile quitte la grille.</summary>
    MixerWidgetViewModel CreateMixerWidget(DeckButton def, System.Windows.Input.ICommand edit, string name)
    {
        var rows = StreamDak.Audio.LevelOptions.MixerTargets(def.Option(StreamDak.Audio.LevelOptions.Sources)).Select(target =>
        {
            var source = StreamDak.Audio.LevelSources.Create(target, _integrations, _engine.Context.Platform.Audio);
            return new LevelWidgetViewModel(source, StreamDak.Audio.LevelShape.HorizontalSlider, a => _ui.BeginInvoke(a), resetLevel: ResetLevelOf(def))
            {
                Button = def, Edit = edit, AccessibleName = source?.Title ?? target.ToText(), ErrorReported = message => SetStatus(message),
                Face = TileFaceData.From(def),   // couleurs du texte de la tuile (contraste dans les deux thèmes)
            };
        }).ToList();
        var mixer = new MixerWidgetViewModel(rows) { Button = def, Edit = edit, AccessibleName = name };
        _mixers.Add(mixer);
        mixer.SetPaused(!_active);
        mixer.Start();
        return mixer;
    }

    /// <summary>Fenêtre fermée : tous les relevés de widgets s'arrêtent.</summary>
    public void StopWidgets()
    {
        foreach (var mixer in _mixers) mixer.Dispose();
        _mixers.Clear();
        foreach (var data in _dataWidgets) data.Dispose();
        _dataWidgets.Clear();
        foreach (var level in _levelWidgets) level.Dispose();
        _levelWidgets.Clear();
    }

    /// <summary>Arrête les relevés des widgets de données dont la tuile n'est plus dans la grille (page changée, widget supprimé, thème changé).</summary>
    void ReleaseDataWidgets()
    {
        if (_mixers.Count > 0)
        {
            var shownMixers = Tiles.OfType<MixerWidgetViewModel>().ToHashSet();
            foreach (var gone in _mixers.Where(m => !shownMixers.Contains(m)).ToList())
            {
                gone.Dispose();
                _mixers.Remove(gone);
            }
        }
        if (_levelWidgets.Count > 0)
        {
            var shownLevels = Tiles.OfType<LevelWidgetViewModel>().ToHashSet();
            foreach (var gone in _levelWidgets.Where(w => !shownLevels.Contains(w)).ToList())
            {
                gone.Dispose();
                _levelWidgets.Remove(gone);
            }
        }
        if (_dataWidgets.Count == 0) return;
        var shown = Tiles.OfType<DataWidgetViewModel>().ToHashSet();
        foreach (var gone in _dataWidgets.Where(w => !shown.Contains(w)).ToList())
        {
            gone.Dispose();
            _dataWidgets.Remove(gone);
        }
    }

    WebWidgetViewModel CreateWebWidget(DeckButton def, System.Windows.Input.ICommand edit, string name, Place place, int index)
    {
        WidgetTypes.IsWebAddress(def.Option(WidgetOptionKeys.Url), out var uri);   // adresse absente ou refusée : uri reste null, la tuile le dit
        var web = new WebWidgetViewModel
        {
            Button = def, Edit = edit, AccessibleName = name, Source = uri, Suspended = MenuOpen || HasDialog,
            FrameKey = uri == null ? null : $"widget|{place.Profile}|{place.Page}|{string.Join('.', place.Path)}|{index}|{uri.AbsoluteUri}",
            ZoomPercent = def.OptionInt(WidgetOptionKeys.Zoom, WebZoom.Normal, WebZoom.Min, WebZoom.Max),
            IdleExit = ThemeOverrideKeys.WebIdle(_doc.Config),
            InputMode = IsEdit ? WebInputModes.Passive : WebInputModes.Normalize(def.Option(WidgetOptionKeys.Input)),
        };
        web.ZoomChanged = z => _doc.UpdateSettings(c =>
        {
            int stored = WebZoom.ForStorage(z);
            if (_doc.Get(place, index) is { } button) button.SetOption(WidgetOptionKeys.Zoom, stored > 0 ? stored.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        }, "Zoom du widget web");
        web.TypingRequested = on => SetTyping(on ? web : null, on);
        web.BrowserRequested = OpenInBrowser;
        return web;
    }

    /// <summary>Ouvre une adresse web dans le navigateur par défaut (issue de secours d'un site qui refuse le cadre intégré).</summary>
    void OpenInBrowser(Uri uri)
    {
        try { _engine.Context.Platform.Launcher.Open(uri.AbsoluteUri, null); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            Log.Warn("Ouverture dans le navigateur impossible : " + ex.Message);
            SetStatus("Impossible d'ouvrir le navigateur");
        }
    }

    // ---------- widget web : saisie au clavier et masquage sous les feuilles ----------

    bool _typing;

    /// <summary>
    /// Saisie au clavier dans un widget web : la fenêtre devient activable comme en mode édition (le clavier va à la page), puis redevient
    /// sans focus et rend le focus à l'application précédente.
    /// </summary>
    void SetTyping(WebWidgetViewModel? tile, bool on)
    {
        if (on && IsEdit) return;   // en édition la fenêtre est déjà activable
        foreach (var w in Tiles.OfType<WebWidgetViewModel>()) w.IsTyping = on && ReferenceEquals(w, tile);
        if (_typing == on) return;
        _typing = on;
        _shell.SetEditing(on);
    }

    void StopTyping()
    {
        if (_typing) SetTyping(null, false);
    }

    string _lastWhere = "";

    /// <summary>Remet toutes les pages web au repos (toucher hors de la page, changement de page ou de dossier). Sans effet sur les pages toujours interactives.</summary>
    public void EndWebInteractions(WebWidgetViewModel? except = null)
    {
        foreach (var w in Tiles.OfType<WebWidgetViewModel>())
            if (!ReferenceEquals(w, except) && w.IsInteracting) w.EndInteraction();
    }

    /// <summary>Le cadre d'une page web est une fenêtre Win32 : un menu ou une boîte de dialogue ouvert le masque (la page garde son état).</summary>
    void UpdateWebSuspension()
    {
        bool suspended = MenuOpen || HasDialog;
        foreach (var w in Tiles.OfType<WebWidgetViewModel>()) w.Suspended = suspended;
    }

    /// <summary>Contenu d'un widget agenda : les prochains événements de l'intégration Google, ou ce qu'il manque pour en afficher.</summary>
    void RefreshAgenda(AgendaWidgetViewModel agenda)
    {
        var none = Array.Empty<AgendaEntry>();
        var google = _integrations.Find(GoogleId);
        if (google == null || !_integrations.IsEnabled(GoogleId)) { agenda.Show(none, "Active Google Workspace dans Paramètres › Intégrations pour voir ton agenda"); return; }
        if (google.State != ConnectionState.Connected)
        {
            agenda.Show(none, google.State == ConnectionState.Error && google.Error != null ? google.Error : "Connexion à Google en cours…");
            return;
        }
        if (_integrations.GetValue($"{GoogleId}.{CalendarFeed.ErrorKey}") is { Length: > 0 } error) { agenda.Show(none, error); return; }
        var lines = AgendaFormatter.Upcoming(CalendarFeed.Parse(_integrations.GetValue($"{GoogleId}.{CalendarFeed.EventsKey}")), DateTime.Now,
            agenda.Button.OptionInt(WidgetOptionKeys.Days, 7, 1, 60), agenda.Button.OptionInt(WidgetOptionKeys.Count, 4, 1, 20));
        if (lines.Count == 0) agenda.Show(none, "Aucun événement à venir");
        else agenda.Show(lines.Select(l => new AgendaEntry(l.When, l.Title, l.Day, l.IsNow)).ToList(), "");
    }

    const string GoogleId = "google";

    bool _systemEnableRequested;

    /// <summary>
    /// Contenu d'un widget Processeur ou Mémoire : le dernier relevé de l'intégration « Système Windows ». Cette intégration n'a ni compte ni
    /// réglage : si elle est éteinte, le premier widget affiché l'active (une fois, sans toucher aux boutons).
    /// </summary>
    void RefreshSystem(SystemMeterWidgetViewModel meter)
    {
        var sys = _integrations.Find(SystemMeter.IntegrationId);
        if (sys == null) { meter.ShowStatus("Le connecteur Système Windows est indisponible"); return; }
        if (!_integrations.IsEnabled(SystemMeter.IntegrationId))
        {
            meter.ShowStatus("Activation du connecteur Système Windows…");
            EnableSystemIntegration();
            return;
        }
        if (sys.State != ConnectionState.Connected)
        {
            meter.ShowStatus(sys.State == ConnectionState.Error && sys.Error != null ? sys.Error : "Première mesure en cours…");
            return;
        }
        string? Value(string key) => _integrations.GetValue($"{SystemMeter.IntegrationId}.{key}");
        var reading = meter.IsRam ? SystemMeter.Ram(Value("ram.used"), Value("ram.total")) : SystemMeter.Cpu(Value("cpu"));
        if (reading == null) meter.ShowStatus("Première mesure en cours…");
        else meter.Show(reading);
    }

    /// <summary>Active l'intégration « Système Windows » (réglage, hors historique) ; hors de la reconstruction de la grille en cours.</summary>
    void EnableSystemIntegration()
    {
        if (_systemEnableRequested) return;
        _systemEnableRequested = true;
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_integrations.IsEnabled(SystemMeter.IntegrationId))
                _doc.UpdateSettings(c => (c.Integrations ??= new())[SystemMeter.IntegrationId] = new IntegrationConfig { Enabled = true }, "Système Windows");
            _systemEnableRequested = false;
        });
    }
}
