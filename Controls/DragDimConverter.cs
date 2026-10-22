using System;
using System.Globalization;
using System.Windows.Data;

namespace TouchDeck.Controls;

/// <summary>Opacité d'une touche : atténuée tant qu'on la glisse.</summary>
public sealed class DragDimConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 0.35 : 1.0;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
