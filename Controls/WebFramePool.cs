using System;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Wpf;
using StreamDak.ViewModels;

namespace StreamDak.Controls;

/// <summary>
/// Un cadre web : le WebView2, l'adresse saisie par l'utilisateur et la tuile qui l'affiche en ce moment (null quand il est rangé).
/// Il survit à la vue qui le montre : les gestionnaires d'événements passent par <see cref="Vm"/>, jamais par une vue.
/// </summary>
sealed class WebFrame
{
    /// <summary>Contrôle WebView2 (créé une fois, déplacé d'une vue à l'autre).</summary>
    public required WebView2 Web { get; init; }
    /// <summary>Adresse configurée : base des autorisations (caméra, micro, notifications).</summary>
    public required Uri Source { get; init; }
    /// <summary>Tuile qui affiche le cadre, ou null s'il est rangé.</summary>
    public WebWidgetViewModel? Vm { get; set; }
}

/// <summary>
/// Cadres rangés : quand une tuile web quitte l'écran (autre page du deck), son cadre n'est pas détruit mais rangé ici, page chargée ; il est
/// repris tel quel au retour (pas de rechargement, la connexion et la saisie en cours sont gardées) et ses notifications continuent d'arriver.
/// Chaque cadre rangé coûte un processus de navigateur : au-delà de <see cref="MaxParked"/>, le plus ancien est libéré.
/// </summary>
static class WebFramePool
{
    /// <summary>Nombre maximal de cadres rangés.</summary>
    public const int MaxParked = 6;

    static readonly LinkedList<(string Key, WebFrame Frame)> Parked = new();

    /// <summary>Nombre de cadres rangés (tests).</summary>
    public static int Count => Parked.Count;

    /// <summary>Reprend le cadre rangé sous cette clé, ou null.</summary>
    public static WebFrame? Take(string key)
    {
        for (var node = Parked.First; node != null; node = node.Next)
            if (node.Value.Key == key)
            {
                Parked.Remove(node);
                return node.Value.Frame;
            }
        return null;
    }

    /// <summary>Range un cadre ; le plus ancien est libéré si la limite est dépassée. Une clé déjà rangée est remplacée (ancien cadre libéré).</summary>
    public static void Park(string key, WebFrame frame)
    {
        if (Take(key) is { } previous && !ReferenceEquals(previous, frame)) Release(previous);
        Parked.AddLast((key, frame));
        while (Parked.Count > MaxParked)
        {
            var oldest = Parked.First!.Value.Frame;
            Parked.RemoveFirst();
            Release(oldest);
        }
    }

    /// <summary>Libère tous les cadres rangés (fermeture de l'application ou de la fenêtre).</summary>
    public static void Clear()
    {
        foreach (var (_, frame) in Parked) Release(frame);
        Parked.Clear();
    }

    /// <summary>Libère les processus du navigateur d'un cadre.</summary>
    public static void Release(WebFrame frame)
    {
        // Le contrôle peut être déjà libéré ou encore en cours d'initialisation : libérer un cadre ne doit jamais faire échouer l'interface
        try { frame.Web.Dispose(); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn("Libération d'un cadre web : " + ex.Message);
        }
    }
}
