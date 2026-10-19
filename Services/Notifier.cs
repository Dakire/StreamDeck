using System;

namespace TouchDeck;

public enum NoticeKind { Info, Success, Warning, Error }

/// <summary>Message discret affiché par l'éditeur (toast) ou par le deck (bandeau d'état). <see cref="Action"/> propose un bouton, typiquement « Annuler ».</summary>
public sealed record Notice(string Message, NoticeKind Kind = NoticeKind.Info, string? ActionText = null, Action? Action = null);

public static class Notifier
{
    public static event Action<Notice>? Posted;

    public static void Post(Notice notice) => Posted?.Invoke(notice);
    public static void Info(string message) => Post(new Notice(message, NoticeKind.Info));
    public static void Success(string message, string? actionText = null, Action? action = null) => Post(new Notice(message, NoticeKind.Success, actionText, action));
    public static void Warning(string message) => Post(new Notice(message, NoticeKind.Warning));
    public static void Error(string message) => Post(new Notice(message, NoticeKind.Error));
}
