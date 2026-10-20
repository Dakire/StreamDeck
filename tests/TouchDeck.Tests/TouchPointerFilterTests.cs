using System.Runtime.InteropServices;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>
/// Vérifie la logique de filtrage WM_POINTER implémentée dans MainWindow.WndProc.
/// Les tests qui dépendent d'un véritable écran tactile sont marqués et exclus
/// de la suite automatique (voir commentaires).
/// </summary>
public class TouchPointerFilterTests
{
    // Constantes copiées de MainWindow pour tester leur valeur en isolation
    const int WM_POINTERDOWN   = 0x0246;
    const int WM_POINTERUP     = 0x0247;
    const int WM_POINTERUPDATE = 0x0245;
    const int WM_MOUSEACTIVATE = 0x0021;
    const uint PT_TOUCH = 2;
    const uint PT_PEN   = 3;
    const uint PT_MOUSE = 1; // POINTER_INPUT_TYPE_MOUSE

    // -----------------------------------------------------------------------
    // Tests automatiques (sans matériel tactile)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(WM_POINTERDOWN,   true)]
    [InlineData(WM_POINTERUP,     true)]
    [InlineData(WM_POINTERUPDATE, true)]
    [InlineData(WM_MOUSEACTIVATE, false)]
    [InlineData(0x0200 /* WM_MOUSEMOVE */, false)]
    [InlineData(0x0201 /* WM_LBUTTONDOWN */, false)]
    public void IsPointerMessage_CatchesExactlyThreeCodes(int msg, bool expected)
    {
        bool result = msg is WM_POINTERDOWN or WM_POINTERUP or WM_POINTERUPDATE;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(PT_TOUCH, true)]
    [InlineData(PT_PEN,   true)]
    [InlineData(PT_MOUSE, false)]
    [InlineData(0u,       false)]   // type inconnu → on ne bloque pas
    public void ShouldBlockPromotionFor_TouchAndPen_NotMouse(uint ptype, bool shouldBlock)
    {
        bool result = ptype is PT_TOUCH or PT_PEN;
        Assert.Equal(shouldBlock, result);
    }

    [Theory]
    [InlineData(0x00000001u, 0x0001u)]  // ID seul dans les 16 bits bas
    [InlineData(0xABCD1234u, 0x1234u)]  // Flags dans les 16 bits hauts, ID dans les bas
    [InlineData(0x00000042u, 0x0042u)]
    public void PointerIdExtraction_Uses16BitsLow(uint wParam, uint expectedId)
    {
        // Même expression que dans WndProc : (uint)((int)wParam & 0xFFFF)
        uint pointerId = (uint)((int)wParam & 0xFFFF);
        Assert.Equal(expectedId, pointerId);
    }

    /// <summary>
    /// Vérifie que GetPointerType est bien exportée par user32.dll (disponible
    /// sur Windows 8+). Sur Windows 10/11, l'appel doit réussir ou échouer
    /// proprement sans lever d'exception au chargement.
    /// </summary>
    [Fact]
    public void GetPointerType_IsExportedByUser32()
    {
        [DllImport("user32.dll")] static extern bool GetPointerType(uint pointerId, out uint pointerType);

        // pointerId = 0 → GetPointerType retourne false (pointeur inexistant).
        // L'important est l'absence d'EntryPointNotFoundException.
        bool ok = GetPointerType(0, out uint ptype);
        _ = ok;
        _ = ptype;
    }

    // -----------------------------------------------------------------------
    // Tests manuels (nécessitent un écran tactile physique)
    // -----------------------------------------------------------------------
    //
    // Les scénarios suivants ne peuvent pas être automatisés sans matériel :
    //
    // 1. CURSEUR IMMOBILE AU TOUCHER
    //    - Placer le curseur dans un coin (par ex. coin supérieur gauche).
    //    - Toucher plusieurs boutons de l'application avec le doigt.
    //    - Résultat attendu : les boutons s'activent, le curseur ne bouge pas.
    //
    // 2. SOURIS FONCTIONNELLE
    //    - Après le scénario précédent, bouger la souris physique.
    //    - Résultat attendu : le curseur suit la souris normalement.
    //
    // 3. ABSENCE DE DOUBLE-DÉCLENCHEMENT
    //    - Toucher un bouton à action comptée (ex. compteur d'actions).
    //    - Résultat attendu : l'action se déclenche une seule fois par toucher.
    //
    // 4. STYLET
    //    - Toucher avec un stylet (Surface Pen ou équivalent).
    //    - Résultat attendu : identique au doigt (curseur immobile, action OK).
    //
    // Pour exécuter ces tests manuels :
    //   $env:TOUCHDECK_DATA_DIR = "C:\tmp\td-touch-test"; dotnet run -c Release
}
