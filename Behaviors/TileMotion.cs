using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace StreamDak.Behaviors;

/// <summary>
/// Animations des touches pendant un glissé : soulèvement (échelle et ombre) de la touche tenue, écartement des voisines de la case visée. Purement visuel :
/// sans animations (<see cref="Motion.Enabled"/> faux) les valeurs sont posées tout de suite, et rien n'attend la fin d'une animation.
/// </summary>
public static class TileMotion
{
    /// <summary>Échelle d'une touche soulevée.</summary>
    public const double LiftScale = 1.08;
    static readonly Duration Quick = new(TimeSpan.FromMilliseconds(160));

    /// <summary>Propriété attachée : la touche est soulevée (échelle et ombre).</summary>
    public static readonly DependencyProperty LiftedProperty =
        DependencyProperty.RegisterAttached("Lifted", typeof(bool), typeof(TileMotion), new PropertyMetadata(false, OnLiftedChanged));
    /// <summary>Propriété attachée : écartement horizontal (DIP) demandé par un glissé voisin.</summary>
    public static readonly DependencyProperty SpreadXProperty =
        DependencyProperty.RegisterAttached("SpreadX", typeof(double), typeof(TileMotion), new PropertyMetadata(0.0, (d, e) => OnSpreadChanged(d, TranslateTransform.XProperty, (double)e.NewValue)));
    /// <summary>Propriété attachée : écartement vertical (DIP).</summary>
    public static readonly DependencyProperty SpreadYProperty =
        DependencyProperty.RegisterAttached("SpreadY", typeof(double), typeof(TileMotion), new PropertyMetadata(0.0, (d, e) => OnSpreadChanged(d, TranslateTransform.YProperty, (double)e.NewValue)));

    /// <summary>Lit <see cref="LiftedProperty"/>.</summary>
    public static bool GetLifted(DependencyObject d) => (bool)d.GetValue(LiftedProperty);
    /// <summary>Écrit <see cref="LiftedProperty"/>.</summary>
    public static void SetLifted(DependencyObject d, bool value) => d.SetValue(LiftedProperty, value);
    /// <summary>Lit <see cref="SpreadXProperty"/>.</summary>
    public static double GetSpreadX(DependencyObject d) => (double)d.GetValue(SpreadXProperty);
    /// <summary>Écrit <see cref="SpreadXProperty"/>.</summary>
    public static void SetSpreadX(DependencyObject d, double value) => d.SetValue(SpreadXProperty, value);
    /// <summary>Lit <see cref="SpreadYProperty"/>.</summary>
    public static double GetSpreadY(DependencyObject d) => (double)d.GetValue(SpreadYProperty);
    /// <summary>Écrit <see cref="SpreadYProperty"/>.</summary>
    public static void SetSpreadY(DependencyObject d, double value) => d.SetValue(SpreadYProperty, value);

    static IEasingFunction Ease => new CubicEase { EasingMode = EasingMode.EaseOut };
    static IEasingFunction Spring => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };

    static void OnLiftedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        if ((bool)e.NewValue)
        {
            var scale = new ScaleTransform(1, 1);
            var shadow = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 8, Opacity = 0, Color = Colors.Black };
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = scale;
            el.Effect = shadow;
            if (!Motion.Enabled) { scale.ScaleX = scale.ScaleY = LiftScale; shadow.Opacity = 0.55; return; }
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, LiftScale, Quick) { EasingFunction = Spring });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, LiftScale, Quick) { EasingFunction = Spring });
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 0.55, Quick));
            return;
        }
        // Reposé : retour à l'échelle 1 puis nettoyage (si la touche n'a pas été soulevée de nouveau entre-temps)
        if (el.RenderTransform is not ScaleTransform back) return;
        void Clear()
        {
            if (GetLifted(el)) return;
            el.ClearValue(UIElement.RenderTransformProperty);
            el.ClearValue(UIElement.EffectProperty);
        }
        if (!Motion.Enabled) { Clear(); return; }
        var shrink = new DoubleAnimation(LiftScale, 1, new Duration(TimeSpan.FromMilliseconds(140))) { EasingFunction = Ease };
        shrink.Completed += (_, _) => Clear();
        back.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        back.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(LiftScale, 1, new Duration(TimeSpan.FromMilliseconds(140))) { EasingFunction = Ease });
        if (el.Effect is DropShadowEffect fx) fx.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(fx.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(140))));
    }

    static void OnSpreadChanged(DependencyObject d, DependencyProperty axis, double value)
    {
        if (d is not FrameworkElement el) return;
        if (el.RenderTransform is not TranslateTransform move || move.IsFrozen)
        {
            if (value == 0) return;                      // rien à écarter, rien à créer
            move = new TranslateTransform();
            el.RenderTransform = move;
        }
        if (!Motion.Enabled) { move.BeginAnimation(axis, null); move.SetValue(axis, value); return; }
        move.BeginAnimation(axis, new DoubleAnimation(value, Quick) { EasingFunction = Ease });
    }
}
