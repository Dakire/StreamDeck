using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using StreamDak.Audio;

namespace StreamDak.Controls;

/// <summary>
/// Contrôle de niveau audio de 0 à 100 : une roue qu'on tourne au doigt (niveau au centre, comme un potentiomètre) ou un curseur vertical / horizontal.
/// Roue : la roue garde son niveau quand le doigt se pose (aucun saut), tourner la fait suivre sans zone morte, toucher le centre coupe ou rétablit.
/// Curseur : poser le doigt sur la poignée la saisit sans saut, ailleurs la poignée va sous le doigt.
/// Le deck ne reçoit pas les événements tactiles de WPF (ADR 0013) : la fenêtre route le doigt vers <see cref="PointerDown"/>, <see cref="PointerMove"/> et
/// <see cref="PointerUp"/> ; la souris passe par les événements habituels. Le clavier agit sur le niveau (flèches, Pg, Début / Fin, Espace pour couper).
/// Le niveau affiché (<see cref="RangeBase.Value"/>) est celui du système ; ce que fait l'utilisateur sort par <see cref="ChangeCommand"/> (paramètre : le niveau entier).
/// </summary>
public sealed class LevelControl : RangeBase
{
    const double MovedSlop = 8;           // en unités WPF : en dessous, c'est un appui et non un geste
    const double InnerRatio = 0.55;       // part du rayon qui est le « centre » (couper / rétablir)
    const double MinRadiusRatio = 0.18;   // sous cette part du rayon, l'angle du doigt est trop instable pour être suivi

    /// <summary>Forme du contrôle.</summary>
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(LevelShape), typeof(LevelControl),
        new FrameworkPropertyMetadata(LevelShape.Wheel, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>true si le canal est coupé (la roue passe en couleur de coupure).</summary>
    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(nameof(IsMuted), typeof(bool), typeof(LevelControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Pinceau de la piste.</summary>
    public static readonly DependencyProperty TrackBrushProperty = BrushProperty(nameof(TrackBrush));
    /// <summary>Pinceau de la partie remplie.</summary>
    public static readonly DependencyProperty FillBrushProperty = BrushProperty(nameof(FillBrush));
    /// <summary>Pinceau de la partie remplie quand le canal est coupé.</summary>
    public static readonly DependencyProperty MutedBrushProperty = BrushProperty(nameof(MutedBrush));
    /// <summary>Pinceau du texte.</summary>
    public static readonly DependencyProperty TextBrushProperty = BrushProperty(nameof(TextBrush));
    /// <summary>Commande appelée à chaque nouveau niveau choisi par l'utilisateur (paramètre : entier de 0 à 100).</summary>
    public static readonly DependencyProperty ChangeCommandProperty = DependencyProperty.Register(nameof(ChangeCommand), typeof(ICommand), typeof(LevelControl));
    /// <summary>Commande appelée pour couper ou rétablir (toucher le centre de la roue, touche Espace).</summary>
    public static readonly DependencyProperty MuteCommandProperty = DependencyProperty.Register(nameof(MuteCommand), typeof(ICommand), typeof(LevelControl));

    static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(LevelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    static LevelControl()
    {
        MaximumProperty.OverrideMetadata(typeof(LevelControl), new FrameworkPropertyMetadata(100.0));
        FocusableProperty.OverrideMetadata(typeof(LevelControl), new FrameworkPropertyMetadata(true));
        MinWidthProperty.OverrideMetadata(typeof(LevelControl), new FrameworkPropertyMetadata(48.0));
        MinHeightProperty.OverrideMetadata(typeof(LevelControl), new FrameworkPropertyMetadata(48.0));
    }

    readonly WheelGesture _wheel = new();
    bool _active, _rotating, _centerDown, _moved;
    Point _down;
    double _grabOffset;

    /// <summary>Crée le contrôle (pas de 1 %, grand pas de 10 %).</summary>
    public LevelControl()
    {
        SmallChange = 1;
        LargeChange = 10;
        SnapsToDevicePixels = true;
    }

    /// <summary>Forme du contrôle.</summary>
    public LevelShape Shape { get => (LevelShape)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    /// <summary>true si le canal est coupé.</summary>
    public bool IsMuted { get => (bool)GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }
    /// <summary>Pinceau de la piste.</summary>
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    /// <summary>Pinceau de la partie remplie.</summary>
    public Brush? FillBrush { get => (Brush?)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    /// <summary>Pinceau de la partie remplie quand le canal est coupé.</summary>
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    /// <summary>Pinceau du texte.</summary>
    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    /// <summary>Commande de changement de niveau.</summary>
    public ICommand? ChangeCommand { get => (ICommand?)GetValue(ChangeCommandProperty); set => SetValue(ChangeCommandProperty, value); }
    /// <summary>Commande de coupure.</summary>
    public ICommand? MuteCommand { get => (ICommand?)GetValue(MuteCommandProperty); set => SetValue(MuteCommandProperty, value); }

    /// <summary>true tant qu'un doigt ou la souris tient le contrôle.</summary>
    public bool IsInteracting => _active;

    // ---------------------------------------------------------------- geste (doigt, souris)

    /// <summary>Le doigt (ou la souris) se pose en <paramref name="p"/>, en coordonnées du contrôle.</summary>
    public void PointerDown(Point p)
    {
        _active = true;
        _moved = false;
        _down = p;
        _rotating = _centerDown = false;
        switch (Shape)
        {
            case LevelShape.Wheel:
                var (cx, cy, radius) = WheelBox();
                double dx = p.X - cx, dy = p.Y - cy;
                if (Math.Sqrt(dx * dx + dy * dy) <= radius * InnerRatio) _centerDown = true;
                else
                {
                    _rotating = true;
                    _wheel.Begin(WheelGesture.AngleOf(dx, dy), Value);   // la roue garde son niveau : pas de saut
                }
                break;
            default:
                var (start, length, reversed, along) = SliderBox();
                double position = along(p);
                double handle = SliderMath.PositionOf(Value, start, length, reversed);
                // Poignée saisie : elle ne saute pas sous le doigt. Ailleurs : elle va sous le doigt.
                _grabOffset = Math.Abs(position - handle) <= 30 ? handle - position : 0;
                SetByUser(SliderMath.LevelAt(position + _grabOffset, start, length, reversed));
                break;
        }
    }

    /// <summary>Le doigt (ou la souris) bouge.</summary>
    public void PointerMove(Point p)
    {
        if (!_active) return;
        if (!_moved && (p - _down).Length > MovedSlop) _moved = true;
        switch (Shape)
        {
            case LevelShape.Wheel:
                if (!_rotating) break;
                var (cx, cy, radius) = WheelBox();
                double dx = p.X - cx, dy = p.Y - cy;
                if (Math.Sqrt(dx * dx + dy * dy) < radius * MinRadiusRatio) break;   // trop près du centre : l'angle n'a plus de sens
                _wheel.Move(WheelGesture.AngleOf(dx, dy));
                SetByUser(_wheel.Level);
                break;
            default:
                var (start, length, reversed, along) = SliderBox();
                SetByUser(SliderMath.LevelAt(along(p) + _grabOffset, start, length, reversed));
                break;
        }
    }

    /// <summary>Le doigt (ou la souris) se lève : un appui sur le centre de la roue coupe ou rétablit.</summary>
    public void PointerUp(Point p)
    {
        if (!_active) return;
        PointerMove(p);
        bool toggle = _centerDown && !_moved;
        _active = _rotating = _centerDown = false;
        if (toggle && MuteCommand is { } mute && mute.CanExecute(null)) mute.Execute(null);
    }

    /// <summary>Le geste est interrompu (fenêtre masquée, doigt perdu) : rien n'est coupé.</summary>
    public void PointerCancel() => _active = _rotating = _centerDown = false;

    /// <summary>Niveau choisi par l'utilisateur : affiché tout de suite, puis transmis à <see cref="ChangeCommand"/> s'il a changé.</summary>
    internal void SetByUser(double level)
    {
        int percent = (int)Math.Round(Math.Clamp(level, 0, 100));
        if (percent == (int)Math.Round(Value)) return;
        SetCurrentValue(ValueProperty, (double)percent);   // SetCurrentValue : la liaison au niveau du système reste en place
        if (ChangeCommand is { } change && change.CanExecute(percent)) change.Execute(percent);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (CaptureMouse()) PointerDown(e.GetPosition(this));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) PointerMove(e.GetPosition(this));
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        PointerUp(e.GetPosition(this));
        ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        PointerCancel();
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        SetByUser(Value + Math.Sign(e.Delta) * SmallChange * 2);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (HandleKey(e.Key)) e.Handled = true;
    }

    /// <summary>Touche du clavier : flèches ± 1, Pg ± 10, Début 0, Fin 100, Espace ou Entrée coupe. Retourne true si la touche est traitée.</summary>
    internal bool HandleKey(Key key)
    {
        double? next = key switch
        {
            Key.Left or Key.Down => Value - SmallChange,
            Key.Right or Key.Up => Value + SmallChange,
            Key.PageDown => Value - LargeChange,
            Key.PageUp => Value + LargeChange,
            Key.Home => 0,
            Key.End => 100,
            _ => null,
        };
        if (next is { } level) { SetByUser(level); return true; }
        if (key is Key.Space or Key.Enter && MuteCommand is { } mute && mute.CanExecute(null)) { mute.Execute(null); return true; }
        return false;
    }

    // ---------------------------------------------------------------- géométrie

    (double Cx, double Cy, double Radius) WheelBox()
    {
        double pad = 6;
        return (ActualWidth / 2, ActualHeight / 2, Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - pad));
    }

    /// <summary>Piste du curseur : début, longueur, sens (vertical : le haut est le maximum) et projection d'un point sur l'axe.</summary>
    (double Start, double Length, bool Reversed, Func<Point, double> Along) SliderBox()
    {
        const double margin = 18;   // demi-poignée
        if (Shape == LevelShape.VerticalSlider)
        {
            double top = Math.Min(ActualHeight, TextBand) + margin, bottom = ActualHeight - margin;
            return (top, Math.Max(1, bottom - top), true, p => p.Y);
        }
        double left = Math.Min(ActualWidth * 0.35, 64) + margin, right = ActualWidth - margin;
        return (left, Math.Max(1, right - left), false, p => p.X);
    }

    double TextBand => Math.Clamp(ActualHeight * 0.18, 22, 40);

    // ---------------------------------------------------------------- dessin

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        // Fond transparent : toute la surface reçoit le doigt (le test de collision du deck ne voit que ce qui est dessiné)
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ActualWidth < 8 || ActualHeight < 8) return;
        if (Shape == LevelShape.Wheel) DrawWheel(dc); else DrawSlider(dc);
        if (IsKeyboardFocused) dc.DrawRectangle(null, new Pen(TextBrush ?? Brushes.Gray, 1) { DashStyle = DashStyles.Dot }, new Rect(1, 1, ActualWidth - 2, ActualHeight - 2));
    }

    Brush Filled => (IsMuted ? MutedBrush : FillBrush) ?? Brushes.Gray;

    void DrawWheel(DrawingContext dc)
    {
        var (cx, cy, radius) = WheelBox();
        double thickness = Math.Max(8, radius * 0.17);
        double r = radius - thickness / 2;
        var track = new Pen(TrackBrush ?? Brushes.DimGray, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, track, Arc(cx, cy, r, WheelGesture.StartAngle, WheelGesture.StartAngle + WheelGesture.Sweep));
        double level = Math.Clamp(Value, 0, 100);
        if (level > 0)
        {
            var fill = new Pen(Filled, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, fill, Arc(cx, cy, r, WheelGesture.StartAngle, WheelGesture.AngleOfLevel(level)));
        }
        // Aiguille : un point sur la piste, à l'angle du niveau
        double a = WheelGesture.AngleOfLevel(level) * Math.PI / 180;
        dc.DrawEllipse(TextBrush ?? Brushes.White, null, new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a)), thickness * 0.38, thickness * 0.38);

        var big = Text(IsMuted ? "Muet" : ((int)Math.Round(level)).ToString(CultureInfo.CurrentCulture), Math.Max(12, radius * (IsMuted ? 0.3 : 0.5)), FontWeights.SemiBold, IsMuted ? MutedBrush : TextBrush);
        double total = big.Height + (IsMuted ? 0 : radius * 0.12);
        dc.DrawText(big, new Point(cx - big.Width / 2, cy - total / 2 - (IsMuted ? 0 : radius * 0.04)));
        if (!IsMuted)
        {
            var unit = Text("%", Math.Max(9, radius * 0.2), FontWeights.Normal, TextBrush, 0.7);
            dc.DrawText(unit, new Point(cx - unit.Width / 2, cy + big.Height / 2 - radius * 0.02));
        }
    }

    void DrawSlider(DrawingContext dc)
    {
        var (start, length, reversed, _) = SliderBox();
        bool vertical = Shape == LevelShape.VerticalSlider;
        double thick = Math.Clamp((vertical ? ActualWidth : ActualHeight) * 0.18, 10, 18);
        double level = Math.Clamp(Value, 0, 100);
        double handle = SliderMath.PositionOf(level, start, length, reversed);
        double mid = vertical ? ActualWidth / 2 : ActualHeight / 2;
        var track = new Pen(TrackBrush ?? Brushes.DimGray, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var fill = new Pen(Filled, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        Point P(double along) => vertical ? new Point(mid, along) : new Point(along, mid);
        double lo = start, hi = start + length;
        dc.DrawLine(track, P(lo), P(hi));
        if (level > 0) dc.DrawLine(fill, P(reversed ? handle : lo), P(reversed ? hi : handle));
        dc.DrawEllipse(TextBrush ?? Brushes.White, new Pen(Filled, 3), P(handle), 15, 15);

        var text = Text(IsMuted ? "Muet" : ((int)Math.Round(level)).ToString(CultureInfo.CurrentCulture) + " %", vertical ? Math.Clamp(ActualWidth * 0.26, 12, 22) : Math.Clamp(ActualHeight * 0.3, 12, 22),
            FontWeights.SemiBold, IsMuted ? MutedBrush : TextBrush);
        dc.DrawText(text, vertical ? new Point(ActualWidth / 2 - text.Width / 2, Math.Max(0, (TextBand - text.Height) / 2)) : new Point(2, ActualHeight / 2 - text.Height / 2));
    }

    static StreamGeometry Arc(double cx, double cy, double r, double fromDeg, double toDeg)
    {
        static Point At(double cx, double cy, double r, double deg) => new(cx + r * Math.Sin(deg * Math.PI / 180), cy - r * Math.Cos(deg * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(cx, cy, r, fromDeg), false, false);
            ctx.ArcTo(At(cx, cy, r, toDeg), new Size(r, r), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    FormattedText Text(string text, double size, FontWeight weight, Brush? brush, double opacity = 1)
    {
        var b = brush ?? Brushes.White;
        if (opacity < 1) { b = b.Clone(); b.Opacity = opacity; }
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal), size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    // ---------------------------------------------------------------- accessibilité

    /// <inheritdoc/>
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
        if (UIElementAutomationPeer.FromElement(this) is LevelAutomationPeer peer) peer.ValueChanged(oldValue, newValue);
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new LevelAutomationPeer(this);

    /// <summary>Pair d'automatisation : un curseur (plage de 0 à 100) pour les lecteurs d'écran et le pilotage par le clavier ou la voix.</summary>
    sealed class LevelAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
    {
        readonly LevelControl _owner;

        public LevelAutomationPeer(LevelControl owner) : base(owner) { _owner = owner; }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(LevelControl);

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        public void ValueChanged(double oldValue, double newValue) => RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);

        public double Value => _owner.Value;
        public bool IsReadOnly => false;
        public double Maximum => _owner.Maximum;
        public double Minimum => _owner.Minimum;
        public double LargeChange => _owner.LargeChange;
        public double SmallChange => _owner.SmallChange;
        public void SetValue(double value) => _owner.SetByUser(value);
    }
}
