using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StreamDak.Controls;

/// <summary>Visible si la largeur donnée atteint le seuil passé en paramètre (en unités indépendantes de la résolution), replié sinon : un libellé qui ne tient pas disparaît.</summary>
public sealed class MinWidthToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width && double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var min) && width < min
            ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible si le nombre est supérieur à zéro, replié sinon (section sans élément masquée).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}