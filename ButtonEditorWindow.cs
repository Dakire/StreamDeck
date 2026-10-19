using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TouchDeck;

/// <summary>Édition d'un bouton : libellé, icône, couleur et liste d'actions.</summary>
public class ButtonEditorWindow : EditorBase
{
    static readonly (string Key, string Label, string Icon)[] Types =
    {
        ("hotkey", "Raccourci", "⌨️"), ("text", "Texte", "🔤"), ("media", "Média", "🎵"),
        ("launch", "Programme", "🚀"), ("script", "Script", "⚡"), ("url", "Lien web", "🌐"),
        ("page", "Page", "📄"), ("focus", "Fenêtre", "🪟"), ("delay", "Pause", "⏱️"),
    };

    static readonly (string Key, string Label)[] MediaKeys =
    {
        ("playpause", "⏯ Lecture / Pause"), ("prev", "⏮ Précédent"), ("next", "⏭ Suivant"), ("stop", "⏹ Stop"),
        ("mute", "🔇 Muet"), ("voldown", "🔉 Volume −"), ("volup", "🔊 Volume +"),
    };

    static readonly string[] Palette =
        { "#272B38", "#1E7F5F", "#B23A48", "#3466D6", "#6C3FD1", "#B7791F", "#0E8FA3", "#C2410C", "#BE185D", "#4D7C0F" };

    static readonly string[] Emojis =
    {
        "⭐", "🏠", "🎵", "🎬", "🎮", "🎤", "🎧", "📷", "💡", "🔔", "🔒", "📁", "📝", "📧", "💬", "🌐",
        "⚙️", "🔧", "🛠️", "💻", "🖥️", "📺", "🔊", "🔇", "⏯️", "▶️", "⏸️", "⏭️", "⏮️", "📌", "🚀", "⚡",
    };

    readonly DeckButton _b;
    readonly List<string> _pages;
    readonly bool _isNew;
    public DeckButton Result => _b;
    public bool Deleted { get; private set; }
    /// <summary>-1 / +1 : déplace le bouton d'une case avant / après.</summary>
    public int Move { get; private set; }

    TextBox _hex = null!;
    Border _preview = null!;
    TextBlock _pIcon = null!, _pLabel = null!;
    StackPanel _colors = null!, _actions = null!;
    TextBlock? _deleteText;
    bool _confirmDelete;
    StackPanel _fields = null!;
    TextBlock _note = null!;

    public ButtonEditorWindow(Window owner, DeckButton source, List<string> pageNames, bool isNew)
        : base(owner, isNew ? "Nouveau bouton" : "Modifier le bouton", 820, 900)
    {
        _b = source; _pages = pageNames; _isNew = isNew;
        if (_b.Actions.Count == 0) _b.Actions.Add(new DeckAction { Type = "hotkey" });

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly };
        var body = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };
        scroll.Content = body;
        root.Children.Add(scroll);

        // --- modèles (uniquement à la création) ---
        _note = new TextBlock
        {
            Foreground = Ui.B("#FFD38A"), FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 0, 4), Visibility = Visibility.Collapsed,
        };
        if (_isNew)
        {
            var tplPanel = new StackPanel { Visibility = Visibility.Collapsed };
            foreach (var group in Templates.All.GroupBy(t => t.Group))
            {
                tplPanel.Children.Add(Ui.Caption(group.Key.ToUpperInvariant()));
                var wrap = new WrapPanel();
                foreach (var t in group)
                {
                    var tpl = t;
                    var chip = Ui.Chip($"{tpl.Icon} {tpl.Name}", false, () => ApplyTemplate(tpl), 40);
                    wrap.Children.Add(chip);
                }
                tplPanel.Children.Add(wrap);
            }
            var toggle = Ui.Chip("📋  Partir d'un modèle  (Chrome, Google, 3CX, Keeper)  ▾", false, () =>
                tplPanel.Visibility = tplPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible, 46);
            body.Children.Add(toggle);
            body.Children.Add(tplPanel);
        }
        body.Children.Add(_note);

        // --- aperçu + libellé / icône ---
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _pIcon = new TextBlock { FontSize = 52, FontFamily = new FontFamily("Segoe UI Emoji"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        _pLabel = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4, 6, 0) };
        _preview = new Border
        {
            Width = 150, Height = 150, CornerRadius = new CornerRadius(22), BorderBrush = Ui.B("#26FFFFFF"), BorderThickness = new Thickness(1),
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _pIcon, _pLabel } },
        };
        top.Children.Add(_preview);

        var fields = _fields = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        fields.Children.Add(Ui.Caption("LIBELLÉ"));
        fields.Children.Add(Ui.Input(_b.Label, t => { _b.Label = t; UpdatePreview(); }));
        fields.Children.Add(Ui.Caption("ICÔNE (EMOJI OU TEXTE COURT)"));
        fields.Children.Add(Ui.Input(_b.Icon, t => { _b.Icon = t; UpdatePreview(); }));
        Grid.SetColumn(fields, 1);
        top.Children.Add(fields);
        body.Children.Add(top);

        var emojiPanel = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var e in Emojis)
        {
            var chip = Ui.Chip(e, false, () => { _b.Icon = e; UpdatePreview(); IconBox(fields).Text = e; }, 44);
            chip.Padding = new Thickness(0); chip.Width = 44; chip.Margin = new Thickness(0, 0, 6, 6);
            ((TextBlock)chip.Content).FontFamily = new FontFamily("Segoe UI Emoji");
            ((TextBlock)chip.Content).FontSize = 20;
            emojiPanel.Children.Add(chip);
        }
        body.Children.Add(emojiPanel);
        body.Children.Add(Ui.Hint("Astuce : sur un clavier, Win + ; ouvre le sélecteur d'emoji de Windows."));

        // --- couleur ---
        body.Children.Add(Ui.Caption("COULEUR"));
        _colors = new StackPanel { Orientation = Orientation.Horizontal };
        var colorRow = new Grid();
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        colorRow.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _colors, PanningMode = PanningMode.HorizontalOnly });
        _hex = Ui.Input(_b.Color, t => { _b.Color = t; UpdatePreview(); BuildColors(); });
        Grid.SetColumn(_hex, 1);
        colorRow.Children.Add(_hex);
        body.Children.Add(colorRow);

        // --- actions ---
        body.Children.Add(Ui.Caption("ACTIONS (EXÉCUTÉES DANS L'ORDRE)"));
        _actions = new StackPanel();
        body.Children.Add(_actions);

        BuildColors();
        BuildActions();
        UpdatePreview();

        // --- pied de fenêtre ---
        var footer = new Grid { Margin = new Thickness(24, 10, 24, 18) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        if (!_isNew)
        {
            var del = Ui.Chip("🗑  Supprimer", false, OnDelete, 48, "#3A1F26");
            _deleteText = (TextBlock)del.Content;
            del.Margin = new Thickness(0, 0, 8, 0);
            left.Children.Add(del);
            var prev = Ui.Chip("◀ Avant", false, () => { Move = -1; Finish(true); }, 48);
            var next = Ui.Chip("Après ▶", false, () => { Move = 1; Finish(true); }, 48);
            prev.Margin = new Thickness(0, 0, 8, 0); next.Margin = new Thickness(0);
            left.Children.Add(prev); left.Children.Add(next);
        }
        footer.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Footer("Annuler", Ui.SurfaceHi, () => Finish(false)));
        right.Children.Add(Footer(_isNew ? "Ajouter" : "Enregistrer", Ui.Accent, () => Finish(true)));
        Grid.SetColumn(right, 2);
        footer.Children.Add(right);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
    }

    static TextBox IconBox(StackPanel fields) => fields.Children.OfType<TextBox>().ElementAt(1);

    void ApplyTemplate(ButtonTemplate t)
    {
        var boxes = _fields.Children.OfType<TextBox>().ToList();
        _b.Actions = ConfigStore.Clone(t.Actions.ToList());
        boxes[0].Text = t.Name;          // déclenche la mise à jour du modèle de données
        boxes[1].Text = t.Icon;
        _hex.Text = t.Color;
        _b.Label = t.Name; _b.Icon = t.Icon; _b.Color = t.Color;
        _note.Text = t.Note == null ? "" : "ℹ  " + t.Note;
        _note.Visibility = t.Note == null ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreview();
        BuildColors();
        BuildActions();
    }

    void OnDelete()
    {
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            if (_deleteText != null) { _deleteText.Text = "Confirmer la suppression ?"; _deleteText.Foreground = Ui.B("#FFB4BE"); }
            return;
        }
        Deleted = true;
        Finish(true);
    }

    void UpdatePreview()
    {
        _pIcon.Text = _b.Icon;
        _pLabel.Text = _b.Label;
        _preview.Background = Ui.TryBrush(_b.Color) ?? Ui.B(Ui.SurfaceHi);
    }

    void BuildColors()
    {
        _colors.Children.Clear();
        foreach (var c in Palette)
        {
            bool sel = string.Equals(_b.Color, c, StringComparison.OrdinalIgnoreCase);
            var swatch = new Button
            {
                Style = (Style)Application.Current.FindResource("FlatButton"), Width = 44, Height = 44,
                Background = Ui.B(c), Tag = new CornerRadius(22), Margin = new Thickness(0, 0, 8, 0),
                Content = new TextBlock { Text = sel ? "✓" : "", FontSize = 20, Foreground = Brushes.White, FontWeight = FontWeights.Bold },
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            string color = c;
            swatch.Click += (_, _) => { _b.Color = color; _hex.Text = color; UpdatePreview(); BuildColors(); };
            _colors.Children.Add(swatch);
        }
    }

    // ---------- actions ----------

    void BuildActions()
    {
        _actions.Children.Clear();
        for (int i = 0; i < _b.Actions.Count; i++) _actions.Children.Add(ActionCard(i));
        _actions.Children.Add(Ui.Chip("＋  Ajouter une action", false, () => { _b.Actions.Add(new DeckAction { Type = "hotkey" }); BuildActions(); }));
    }

    Border ActionCard(int index)
    {
        var a = _b.Actions[index];
        var sp = new StackPanel();

        // en-tête : titre + monter / descendre / supprimer
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = $"Action {index + 1}", Foreground = Ui.B(Ui.Dim), FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        if (index > 0) tools.Children.Add(Ui.IconButton("▲", () => { Swap(index, index - 1); }));
        if (index < _b.Actions.Count - 1) tools.Children.Add(Ui.IconButton("▼", () => { Swap(index, index + 1); }));
        if (_b.Actions.Count > 1) tools.Children.Add(Ui.IconButton("✕", () => { _b.Actions.RemoveAt(index); BuildActions(); }));
        Grid.SetColumn(tools, 1);
        head.Children.Add(tools);
        sp.Children.Add(head);

        // type
        var types = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        foreach (var t in Types)
        {
            var key = t.Key;
            types.Children.Add(Ui.Chip($"{t.Icon} {t.Label}", a.Type == key, () =>
            {
                if (a.Type == key) return;
                a.Type = key; a.Value = key == "delay" ? "500" : ""; a.Args = null;
                BuildActions();
            }, 40));
        }
        sp.Children.Add(types);

        // valeur selon le type
        switch (a.Type)
        {
            case "hotkey":
                sp.Children.Add(Ui.Caption("COMBINAISON"));
                var hk = Ui.Input(a.Value, t => a.Value = t);
                hk.PreviewKeyDown += CaptureHotkey;
                sp.Children.Add(hk);
                sp.Children.Add(Ui.Hint("Clique dans le champ puis appuie sur ta combinaison (ex. Ctrl+Maj+M, F13). Pour la touche Windows, tape-la : win+d, win+shift+s."));
                break;
            case "text":
                sp.Children.Add(Ui.Caption("TEXTE À SAISIR"));
                sp.Children.Add(Ui.Input(a.Value, t => a.Value = t));
                break;
            case "media":
                sp.Children.Add(Ui.Caption("COMMANDE MÉDIA"));
                var mp = new WrapPanel();
                foreach (var m in MediaKeys)
                {
                    var mk = m.Key;
                    mp.Children.Add(Ui.Chip(m.Label, a.Value == mk, () => { a.Value = mk; BuildActions(); }, 40));
                }
                sp.Children.Add(mp);
                break;
            case "launch":
                sp.Children.Add(Ui.Caption("PROGRAMME OU DOSSIER"));
                AddPathField(sp, a.Value, v => a.Value = v, "Programmes|*.exe;*.lnk;*.bat;*.cmd|Tous les fichiers|*.*");
                sp.Children.Add(Ui.Caption("ARGUMENTS (FACULTATIF)"));
                sp.Children.Add(Ui.Input(a.Args ?? "", t => a.Args = t));
                break;
            case "script":
                sp.Children.Add(Ui.Caption("COMMANDE POWERSHELL OU FICHIER .ps1 / .bat / .cmd"));
                AddPathField(sp, a.Value, v => a.Value = v, "Scripts|*.ps1;*.bat;*.cmd|Tous les fichiers|*.*");
                sp.Children.Add(Ui.Caption("ARGUMENTS (FICHIERS UNIQUEMENT, FACULTATIF)"));
                sp.Children.Add(Ui.Input(a.Args ?? "", t => a.Args = t));
                break;
            case "url":
                sp.Children.Add(Ui.Caption("ADRESSE"));
                sp.Children.Add(Ui.Input(a.Value, t => a.Value = t));
                break;
            case "page":
                sp.Children.Add(Ui.Caption("ALLER À LA PAGE"));
                var pp = new WrapPanel();
                foreach (var name in new[] { "home", "back" }.Concat(_pages))
                {
                    var n = name;
                    string text = n == "home" ? "🏠 Accueil" : n == "back" ? "↩ Retour" : n;
                    pp.Children.Add(Ui.Chip(text, string.Equals(a.Value, n, StringComparison.OrdinalIgnoreCase), () => { a.Value = n; BuildActions(); }, 40));
                }
                sp.Children.Add(pp);
                break;
            case "focus":
                sp.Children.Add(Ui.Caption("PROGRAMME OU TITRE DE LA FENÊTRE"));
                sp.Children.Add(Ui.Input(a.Value, t => a.Value = t));
                sp.Children.Add(Ui.Hint("Nom du programme (chrome, 3CXDesktopApp…) ou texte présent dans le titre de la fenêtre. Fenêtres ouvertes en ce moment :"));
                var open = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var proc in WindowFocus.OpenProcessNames())
                {
                    var pn = proc;
                    open.Children.Add(Ui.Chip(pn, string.Equals(a.Value, pn, StringComparison.OrdinalIgnoreCase), () => { a.Value = pn; BuildActions(); }, 36));
                }
                sp.Children.Add(open);
                sp.Children.Add(Ui.Caption("SI ABSENTE, LANCER (FACULTATIF)"));
                AddPathField(sp, a.Args ?? "", v => a.Args = v, "Programmes|*.exe;*.lnk;*.bat;*.cmd|Tous les fichiers|*.*");
                sp.Children.Add(Ui.Hint("Astuce : place cette action en premier, puis une Pause (≈150 ms), puis un Raccourci : il sera envoyé à cette fenêtre."));
                break;
            case "delay":
                sp.Children.Add(Ui.Caption("DURÉE (MILLISECONDES)"));
                var dl = Ui.Input(a.Value, t => a.Value = t);
                dl.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
                sp.Children.Add(dl);
                break;
        }

        return new Border
        {
            Background = Ui.B(Ui.Surface), BorderBrush = Ui.B(Ui.Line), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 10, 16, 14), Margin = new Thickness(0, 0, 0, 12),
            Child = sp,
        };
    }

    void Swap(int i, int j)
    {
        (_b.Actions[i], _b.Actions[j]) = (_b.Actions[j], _b.Actions[i]);
        BuildActions();
    }

    void AddPathField(StackPanel sp, string initial, Action<string> set, string filter)
    {
        var tb = Ui.Input(initial, set);
        sp.Children.Add(tb);
        var browse = Ui.Chip("📂  Parcourir…", false, () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog(this) == true) tb.Text = dlg.FileName;
        }, 40);
        browse.Margin = new Thickness(0, 8, 0, 0);
        browse.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(browse);
    }

    // ---------- capture de raccourci ----------

    static string? KeyName(Key k) => k switch
    {
        >= Key.A and <= Key.Z => k.ToString().ToLowerInvariant(),
        >= Key.D0 and <= Key.D9 => ((int)k - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)k - (int)Key.NumPad0).ToString(),
        >= Key.F1 and <= Key.F24 => k.ToString().ToLowerInvariant(),
        Key.Enter => "enter", Key.Escape => "esc", Key.Tab => "tab", Key.Space => "space",
        Key.Back => "backspace", Key.Delete => "delete", Key.Insert => "insert",
        Key.Home => "home", Key.End => "end", Key.PageUp => "pageup", Key.Next => "pagedown",
        Key.Left => "left", Key.Up => "up", Key.Right => "right", Key.Down => "down",
        Key.Snapshot => "printscreen",
        _ => null,
    };

    static void CaptureHotkey(object sender, KeyEventArgs e)
    {
        var tb = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var name = KeyName(key);
        if (name == null) return;
        var mods = Keyboard.Modifiers;
        bool isF = key >= Key.F1 && key <= Key.F24;
        // Sans Ctrl/Alt/Win (et hors touches F), on laisse la saisie manuelle normale
        if (!isF && (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) return;

        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("win");
        parts.Add(name);
        tb.Text = string.Join("+", parts);
        tb.CaretIndex = tb.Text.Length;
        e.Handled = true;
    }
}

/// <summary>Édition d'une page : nom, visibilité dans les onglets, ordre, suppression.</summary>
public class PageEditorWindow : EditorBase
{
    readonly bool _canDelete;
    public string PageName { get; private set; }
    public bool Hidden { get; private set; }
    public bool Deleted { get; private set; }
    public int Move { get; private set; }
    TextBlock? _deleteText;
    bool _confirm;

    public PageEditorWindow(Window owner, DeckPage page, bool canDelete) : base(owner, "Modifier la page", 640, 440)
    {
        PageName = page.Name; Hidden = page.Hidden; _canDelete = canDelete;

        var sp = new StackPanel { Margin = new Thickness(24, 12, 24, 18) };
        sp.Children.Add(Ui.Caption("NOM DE LA PAGE"));
        sp.Children.Add(Ui.Input(PageName, t => PageName = t));

        sp.Children.Add(Ui.Caption("AFFICHAGE"));
        var vis = new WrapPanel();
        void BuildVis()
        {
            vis.Children.Clear();
            vis.Children.Add(Ui.Chip("Visible dans la barre d'onglets", !Hidden, () => { Hidden = false; BuildVis(); }, 40));
            vis.Children.Add(Ui.Chip("Masquée (accessible via un bouton)", Hidden, () => { Hidden = true; BuildVis(); }, 40));
        }
        BuildVis();
        sp.Children.Add(vis);

        sp.Children.Add(Ui.Caption("ORDRE DES ONGLETS"));
        var order = new WrapPanel();
        order.Children.Add(Ui.Chip("◀ Avant", false, () => { Move = -1; Finish(true); }, 40));
        order.Children.Add(Ui.Chip("Après ▶", false, () => { Move = 1; Finish(true); }, 40));
        sp.Children.Add(order);

        var foot = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (_canDelete)
        {
            var del = Ui.Chip("🗑  Supprimer la page", false, () =>
            {
                if (!_confirm)
                {
                    _confirm = true;
                    if (_deleteText != null) { _deleteText.Text = "Confirmer ? (supprime ses boutons)"; _deleteText.Foreground = Ui.B("#FFB4BE"); }
                    return;
                }
                Deleted = true; Finish(true);
            }, 48, "#3A1F26");
            _deleteText = (TextBlock)del.Content;
            del.Margin = new Thickness(0);
            foot.Children.Add(del);
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Footer("Annuler", Ui.SurfaceHi, () => Finish(false)));
        right.Children.Add(Footer("Enregistrer", Ui.Accent, () => Finish(true)));
        Grid.SetColumn(right, 2);
        foot.Children.Add(right);
        sp.Children.Add(foot);

        Content = sp;
    }
}
