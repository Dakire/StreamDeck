using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StreamDak;

/// <summary>Processus d'un même programme lancés par StreamDak (par exemple les processus <c>msedgewebview2</c> des pages web et des badges).</summary>
/// <param name="Count">Nombre de processus.</param>
/// <param name="WorkingSetBytes">Mémoire de travail totale.</param>
public sealed record ProcessUsage(int Count, long WorkingSetBytes);

/// <summary>État d'une intégration pour le diagnostic.</summary>
/// <param name="Name">Nom affiché.</param>
/// <param name="Enabled">Activée dans les paramètres.</param>
/// <param name="State">Connectée, en erreur, déconnectée…</param>
/// <param name="Error">Dernier message d'erreur, ou null.</param>
public sealed record IntegrationStatus(string Name, bool Enabled, string State, string? Error);

/// <summary>Données d'un diagnostic.</summary>
public sealed class DiagnosticInput
{
    /// <summary>Ligne de version (application, .NET, Windows).</summary>
    public string Version { get; init; } = "";
    /// <summary>Mémoire de travail de l'application.</summary>
    public long WorkingSetBytes { get; init; }
    /// <summary>Processus WebView2 lancés par l'application.</summary>
    public ProcessUsage WebView { get; init; } = new(0, 0);
    /// <summary>Durée depuis le lancement.</summary>
    public TimeSpan Uptime { get; init; }
    /// <summary>Mode économie actif.</summary>
    public bool EconomyMode { get; init; }
    /// <summary>Pauses du thread d'interface.</summary>
    public UiPauseSummary Pauses { get; init; } = new(0, 0, 0, 0, Array.Empty<UiPause>());
    /// <summary>Intégrations et leur état.</summary>
    public IReadOnlyList<IntegrationStatus> Integrations { get; init; } = Array.Empty<IntegrationStatus>();
    /// <summary>Derniers avertissements et erreurs du journal.</summary>
    public IReadOnlyList<string> RecentLog { get; init; } = Array.Empty<string>();
    /// <summary>Nombre de profils, de pages et de boutons.</summary>
    public (int Profiles, int Pages, int Buttons) Content { get; init; }
}

/// <summary>
/// Texte de diagnostic à coller dans un message : version, mémoire, pauses de l'interface, intégrations, derniers avertissements.
/// Le texte entier passe par <see cref="LogRedactor"/> ; aucun réglage, aucun secret, aucun titre de bouton n'y figure.
/// </summary>
public static class DiagnosticReport
{
    /// <summary>Construit le texte.</summary>
    public static string Build(DiagnosticInput d)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("StreamDak : diagnostic");
        sb.AppendLine(d.Version);
        sb.AppendLine(inv, $"En marche depuis {FormatUptime(d.Uptime)} · mode économie : {(d.EconomyMode ? "oui" : "non")}");
        sb.AppendLine(inv, $"Contenu : {d.Content.Profiles} profil(s), {d.Content.Pages} page(s), {d.Content.Buttons} bouton(s)");
        sb.AppendLine(inv, $"Mémoire : application {Mb(d.WorkingSetBytes)} Mo · WebView2 {d.WebView.Count} processus, {Mb(d.WebView.WorkingSetBytes)} Mo · total {Mb(d.WorkingSetBytes + d.WebView.WorkingSetBytes)} Mo");
        var p = d.Pauses;
        sb.AppendLine(inv, $"Interface : {p.Count} pause(s) de {UiWatchdogThresholdMs} ms ou plus, la plus longue {p.MaxMilliseconds} ms, {p.LongTasks} tâche(s) longue(s)");
        foreach (var pause in p.Recent.TakeLast(10)) sb.AppendLine(inv, $"  {pause.At:HH:mm:ss} {pause.Milliseconds} ms ({pause.Context})");
        sb.AppendLine("Intégrations :");
        foreach (var i in d.Integrations.Where(i => i.Enabled))
            sb.AppendLine(inv, $"  {i.Name} : {i.State}{(string.IsNullOrEmpty(i.Error) ? "" : " : " + i.Error)}");
        if (!d.Integrations.Any(i => i.Enabled)) sb.AppendLine("  aucune activée");
        sb.AppendLine("Derniers avertissements :");
        foreach (var line in d.RecentLog.TakeLast(20)) sb.AppendLine("  " + line);
        if (d.RecentLog.Count == 0) sb.AppendLine("  aucun");
        return LogRedactor.Redact(sb.ToString().TrimEnd());

        string Mb(long bytes) => (bytes / 1048576.0).ToString("0", inv);
    }

    /// <summary>Seuil des pauses comptées (reflète le chien de garde).</summary>
    public const int UiWatchdogThresholdMs = 200;

    static string FormatUptime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{(int)t.TotalMinutes} min {t.Seconds} s";
}
