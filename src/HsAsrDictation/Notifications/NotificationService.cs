using System.Windows.Forms;

namespace HsAsrDictation.Notifications;

public sealed class NotificationService
{
    public event EventHandler<NotificationMessage>? NotificationRaised;

    public void Info(string title, string message) =>
        NotificationRaised?.Invoke(this, new NotificationMessage(title, message, ToolTipIcon.Info));

    public void Warn(string title, string message) =>
        NotificationRaised?.Invoke(this, new NotificationMessage(title, message, ToolTipIcon.Warning));

    public void Error(string title, string message) =>
        NotificationRaised?.Invoke(this, new NotificationMessage(title, message, ToolTipIcon.Error));
}

public sealed record NotificationMessage(string Title, string Message, ToolTipIcon Icon);
