using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StreamDak.Controls;

/// <summary>Un contact du doigt destiné à une page web, tel que Windows l'a donné au deck (pixels physiques, écran).</summary>
/// <param name="Id">Identifiant du pointeur.</param>
/// <param name="FrameId">Trame d'entrée (contacts simultanés).</param>
/// <param name="Flags">Drapeaux <c>POINTER_FLAG_*</c> d'origine (nouveau, en contact, premier, annulé…).</param>
/// <param name="Screen">Position, pixels physiques de l'écran.</param>
/// <param name="HimetricX">Position d'origine en centièmes de millimètre sur le périphérique.</param>
/// <param name="HimetricY">Idem, ordonnée.</param>
/// <param name="Time">Horodatage du message.</param>
/// <param name="Contact">Zone de contact du doigt (pixels écran), ou vide.</param>
/// <param name="Pressure">Pression (0 à 1024), 0 si inconnue.</param>
/// <param name="Orientation">Orientation du contact (degrés), 0 si inconnue.</param>
/// <param name="TouchMask">Champs valides de la zone de contact, de l'orientation et de la pression (<c>TOUCH_MASK_*</c>).</param>
/// <param name="DeviceRect">Surface du périphérique (himetric), ou vide.</param>
/// <param name="DisplayRect">Écran associé (pixels), ou vide.</param>
public readonly record struct WebTouchPoint(uint Id, uint FrameId, uint Flags, Point Screen, int HimetricX, int HimetricY, uint Time,
    Int32Rect Contact = default, uint Pressure = 0, uint Orientation = 0, uint TouchMask = 0, Int32Rect DeviceRect = default, Int32Rect DisplayRect = default);

/// <summary>
/// Cadre d'une page web hébergé par composition (ADR 0036) : la page est dessinée par WPF, sans fenêtre Win32 enfant. Le doigt arrive donc à la fenêtre du deck,
/// qui retire ses <c>WM_POINTER</c> du traitement par défaut de Windows (pas de souris synthétique, le curseur ne bouge pas, ADR 0013) et les transmet à la
/// page comme un vrai toucher (<c>SendPointerInput</c>) : défilement, pincement, appui et appui long sont ceux de Chromium.
/// <para>
/// Le contrôle de base transmet lui aussi le toucher qu'il reçoit de WPF ; ce chemin est coupé tant que le deck transmet lui-même, sinon chaque contact
/// arriverait deux fois. La souris physique garde le chemin du contrôle (survol, curseur de la page) ; une souris fabriquée à partir du toucher est ignorée.
/// </para>
/// </summary>
public sealed class DeckWebView : WebView2CompositionControl
{
    /// <summary>Envoi d'un pointeur au navigateur : membre non public du contrôle (même méthode que ses propres gestionnaires de toucher).</summary>
    static readonly MethodInfo? SendPointer = typeof(WebView2CompositionControl).GetMethod("SendPointerInput", BindingFlags.Instance | BindingFlags.NonPublic,
        null, new[] { typeof(CoreWebView2PointerEventKind), typeof(CoreWebView2PointerInfo) }, null);

    const uint PT_TOUCH = 2;
    const uint POINTER_FLAG_CANCELED = 0x8000;

    /// <summary>true si le deck peut transmettre le toucher lui-même ; sinon (version du contrôle sans ce membre), le contrôle garde son propre chemin.</summary>
    public static bool CanForwardTouch => SendPointer != null;

    /// <summary>Nombre de contacts transmis à la page (diagnostic et tests).</summary>
    public int ForwardedTouches { get; private set; }

    /// <summary>
    /// Transmet un contact du doigt à la page : position ramenée au coin haut gauche du cadre (pixels physiques), drapeaux d'origine. Un contact annulé par Windows
    /// (geste système, rejet de paume) est transmis comme un relâchement annulé. Faux si la page n'est pas prête ou si l'envoi échoue.
    /// </summary>
    public bool Forward(int message, WebTouchPoint p)
    {
        if (SendPointer == null || CoreWebView2 is not { } core || PresentationSource.FromVisual(this) == null) return false;
        var kind = message switch
        {
            0x0246 => CoreWebView2PointerEventKind.Down,
            0x0247 => CoreWebView2PointerEventKind.Up,
            _ => (p.Flags & POINTER_FLAG_CANCELED) != 0 ? CoreWebView2PointerEventKind.Up : CoreWebView2PointerEventKind.Update,
        };
        try
        {
            var origin = PointToScreen(new Point(0, 0));
            int ox = (int)Math.Round(origin.X), oy = (int)Math.Round(origin.Y);
            var at = new System.Drawing.Point((int)Math.Round(p.Screen.X) - ox, (int)Math.Round(p.Screen.Y) - oy);
            var info = core.Environment.CreateCoreWebView2PointerInfo();
            info.PointerKind = PT_TOUCH;   // un stylet est transmis comme un doigt : défilement et appui, pas de sélection à la plume
            info.PointerId = p.Id;
            info.FrameId = p.FrameId;
            info.PointerFlags = p.Flags;
            info.PixelLocation = at;
            info.PixelLocationRaw = at;
            info.HimetricLocation = new System.Drawing.Point(p.HimetricX, p.HimetricY);
            info.HimetricLocationRaw = info.HimetricLocation;
            info.Time = p.Time;
            info.HistoryCount = 1;
            var device = p.DeviceRect.Width > 0 ? p.DeviceRect : new Int32Rect(0, 0, 1, 1);
            var display = p.DisplayRect.Width > 0 ? p.DisplayRect : new Int32Rect(0, 0, 1, 1);
            info.PointerDeviceRect = new System.Drawing.Rectangle(device.X, device.Y, device.Width, device.Height);
            info.DisplayRect = new System.Drawing.Rectangle(display.X, display.Y, display.Width, display.Height);
            var contact = p.Contact.Width > 0 ? p.Contact : new Int32Rect((int)Math.Round(p.Screen.X) - 2, (int)Math.Round(p.Screen.Y) - 2, 4, 4);
            info.TouchContact = new System.Drawing.Rectangle(contact.X - ox, contact.Y - oy, contact.Width, contact.Height);
            info.TouchContactRaw = info.TouchContact;
            info.TouchMask = p.TouchMask;
            info.TouchPressure = p.Pressure;
            info.TouchOrientation = p.Orientation;
            SendPointer.Invoke(this, new object[] { kind, info });
            ForwardedTouches++;
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn("Page web : toucher non transmis (" + (ex.InnerException ?? ex).Message + ")");
            return false;
        }
    }

    // Le deck transmet le toucher lui-même (avec les vraies données de Windows) : le chemin WPF du contrôle doublerait chaque contact
    /// <inheritdoc/>
    protected override void OnTouchDown(TouchEventArgs e) { if (CanForwardTouch) e.Handled = true; else base.OnTouchDown(e); }
    /// <inheritdoc/>
    protected override void OnTouchMove(TouchEventArgs e) { if (CanForwardTouch) e.Handled = true; else base.OnTouchMove(e); }
    /// <inheritdoc/>
    protected override void OnTouchUp(TouchEventArgs e) { if (CanForwardTouch) e.Handled = true; else base.OnTouchUp(e); }

    // Souris fabriquée par WPF à partir du doigt ou du stylet : jamais envoyée à la page (seule la vraie souris l'est)
    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e) { if (e.StylusDevice != null) e.Handled = true; else base.OnMouseMove(e); }
    /// <inheritdoc/>
    protected override void OnMouseDown(MouseButtonEventArgs e) { if (e.StylusDevice != null) e.Handled = true; else base.OnMouseDown(e); }
    /// <inheritdoc/>
    protected override void OnMouseUp(MouseButtonEventArgs e) { if (e.StylusDevice != null) e.Handled = true; else base.OnMouseUp(e); }
}
