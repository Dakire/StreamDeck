using System;
using System.Collections.Generic;
using System.Linq;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

/// <summary>Description de menu partagée par l'éditeur et le deck : les actions du catalogue, une rubrique par catégorie.</summary>
public static class CatalogMenu
{
    /// <summary>Une entrée par catégorie (actions natives, applications cibles, intégrations) ; <paramref name="pick"/> reçoit l'action choisie.</summary>
    public static List<MenuItemSpec> Categories(Action<CatalogAction> pick)
    {
        var list = new List<MenuItemSpec>();
        foreach (var cat in ActionCatalog.Categories)
        {
            var children = ActionCatalog.All.Where(a => a.Category == cat)
                .Select(a => new MenuItemSpec(a.Name, () => pick(a), IconCatalog.TryResolve(a.Icon, out var s) ? s : SymbolRegular.Apps24))
                .ToList();
            IconCatalog.TryResolve(ActionCatalog.CategoryIcons.GetValueOrDefault(cat, "Apps24"), out var ci);
            list.Add(new MenuItemSpec(cat, Icon: ci, Children: children));
        }
        return list;
    }
}
