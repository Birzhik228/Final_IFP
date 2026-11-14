using LibraryLending.Application;
namespace LibraryLending.Web.State;

/// <summary>Circuit-scoped presentation state. Authentication and lending rules remain server enforced.</summary>
public sealed class AppState
{
    private readonly Dictionary<string, object> _cache = new();
    public Actor? CurrentUser { get; private set; }
    public IReadOnlyList<NotificationDto> Notifications { get; private set; } = [];
    public string? Toast { get; private set; }
    public bool ToastIsError { get; private set; }
    public bool DarkMode { get; set; }
    public bool LiveConnected { get; private set; }
    public event Action? Changed;
    public event Action? DataInvalidated;
    public void SetActor(Actor? actor) { CurrentUser = actor; Changed?.Invoke(); }
    public void SetNotifications(IReadOnlyList<NotificationDto> value) { Notifications = value; Changed?.Invoke(); }
    public void SetLiveConnected(bool online) { LiveConnected = online; Changed?.Invoke(); }
    public bool TryGet<T>(string key, out T? value) { if (_cache.TryGetValue(key, out var cached) && cached is T typed) { value = typed; return true; } value = default; return false; }
    public void Cache<T>(string key, T value) where T : notnull { if (_cache.Count >= 20) _cache.Clear(); _cache[key] = value; }
    public void Invalidate() { _cache.Clear(); DataInvalidated?.Invoke(); Changed?.Invoke(); }
    public void ShowToast(string message, bool error = false) { Toast = message; ToastIsError = error; Changed?.Invoke(); }
    public void DismissToast() { Toast = null; Changed?.Invoke(); }
}
