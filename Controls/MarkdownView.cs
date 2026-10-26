using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace StreamDak.Controls;

/// <summary>
/// Affiche les blocs d'un Markdown simple (<see cref="SimpleMarkdown"/>) : titres, paragraphes, listes, code, gras, italique, liens. Du texte, rien d'autre : pas de HTML, pas
/// d'image, les liens montrent leur adresse en infobulle et ne s'ouvrent jamais d'eux-mêmes. Les couleurs suivent le thème (ressources <c>Deck*</c>).
/// </summary>
public sealed class MarkdownView : StackPanel
{
    /// <summary>Blocs à afficher.</summary>
    public static readonly DependencyProperty BlocksProperty = DependencyProperty.Register(
        nameof(Blocks), typeof(IReadOnlyList<MarkdownBlock>), typeof(MarkdownView), new PropertyMetadata(null, (d, _) => ((MarkdownView)d).Rebuild()));

    /// <summary>Blocs à afficher.</summary>
    public IReadOnlyList<MarkdownBlock>? Blocks
    {
        get => (IReadOnlyList<MarkdownBlock>?)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    void Rebuild()
    {
        Children.Clear();
        foreach (var block in Blocks ?? Enumerable.Empty<MarkdownBlock>()) Children.Add(Render(block));
    }

    static UIElement Render(MarkdownBlock block)
    {
        switch (block.Kind)
        {
            case MarkdownBlockKind.Rule:
                var rule = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
                rule.SetResourceReference(Border.BackgroundProperty, "DeckLine");
                return rule;
            case MarkdownBlockKind.CodeBlock:
                var code = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 6), Child = Text(block, 13) };
                code.SetResourceReference(Border.BackgroundProperty, "DeckSurface");
                ((TextBlock)code.Child).FontFamily = new FontFamily("Cascadia Mono, Consolas");
                return code;
            case MarkdownBlockKind.Heading:
                var heading = Text(block, block.Level switch { 1 => 20, 2 => 17, _ => 15 });
                heading.FontWeight = FontWeights.SemiBold;
                heading.Margin = new Thickness(0, block.Level == 1 ? 6 : 10, 0, 4);
                return heading;
            case MarkdownBlockKind.ListItem:
                var row = new Grid { Margin = new Thickness(block.Level * 18, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var marker = new TextBlock { Text = block.Marker, FontSize = 14 };
                marker.SetResourceReference(TextBlock.ForegroundProperty, "DeckDim");
                var item = Text(block, 14);
                Grid.SetColumn(item, 1);
                row.Children.Add(marker);
                row.Children.Add(item);
                return row;
            default:
                var paragraph = Text(block, 14);
                paragraph.Margin = new Thickness(0, 2, 0, 6);
                return paragraph;
        }
    }

    static TextBlock Text(MarkdownBlock block, double size)
    {
        var text = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "DeckText");
        foreach (var part in block.Runs)
        {
            var run = new Run(part.Text);
            if (part.Bold) run.FontWeight = FontWeights.SemiBold;
            if (part.Italic) run.FontStyle = FontStyles.Italic;
            if (part.Code) run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            if (part.LinkTarget != null)
            {
                run.TextDecorations = TextDecorations.Underline;
                run.SetResourceReference(TextElement.ForegroundProperty, "DeckAccentText");
                run.ToolTip = part.LinkTarget;
            }
            text.Inlines.Add(run);
        }
        return text;
    }
}
