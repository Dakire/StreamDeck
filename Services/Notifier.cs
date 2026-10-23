using System;

namespace StreamDak;

/// <summary>Nature d'un message à l'utilisateur (couleur, icône, annonce d'accessibilité).</summary>
public enum NoticeKind
{
    /// <summary>Information neutre.</summary>
    Info,
    /// <summary>Opération réussie.</summary>
    Success,
    /// <summary>Opération faite, mais à vérifier.</summary>
    Warning,
    /// <summary>Opération impossible.</summary>
    Error,
}

/// <summary>Message discret affiché par l'éditeur (toast) ou par le deck (bandeau d'état). <see cref="Action"/> propose un bouton, typiquement « Annuler ».</summary>
public sealed record Notice(string Message, NoticeKind Kind = NoticeKind.Info, string? ActionText = null, Action? Action = null);

/// <summary>Messages à l'utilisateur : l'éditeur les affiche en toast, le deck dans sa barre d'état.</summary>
public static class Notifier
{
    /// <summary>Chaque message publié.</summary>
    public static event Action<Notice>? Posted;

    /// <summary>Publie un message.</summary>
    public static void Post(Notice notice) => Posted?.Invoke(notice);
    /// <summary>Information neutre.</summary>
    public static void Info(string message) => Post(new Notice(message, NoticeKind.Info));
    /// <summary>Confirmation, avec un bouton facultatif (souvent « Annuler »).</summary>
    public static void Success(string message, string? actionText = null, Action? action = null) => Post(new Notice(message, NoticeKind.Success, actionText, action));
    /// <summary>Avertissement (action faite, mais à vérifier).</summary>
    public static void Warning(string message) => Post(new Notice(message, NoticeKind.Warning));
    /// <summary>Erreur : l'action demandée n'a pas été faite.</summary>
    public static void Error(string message) => Post(new Notice(message, NoticeKind.Error));
}
