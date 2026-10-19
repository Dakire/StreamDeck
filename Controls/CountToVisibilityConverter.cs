using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TouchDeck.Controls;

/// <summary>Visible si le nombre est supérieur à zéro, replié sinon (section sans élément masquée).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}