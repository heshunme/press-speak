namespace HsAsrDictation.Notifications;

public interface INotificationService
{
    void Info(string title, string message);

    void Warn(string title, string message);

    void Error(string title, string message);
}
