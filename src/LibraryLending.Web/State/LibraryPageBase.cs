using Microsoft.AspNetCore.Components;
using LibraryLending.Application;
using LibraryLending.Domain;
namespace LibraryLending.Web.State;

public abstract class LibraryPageBase : ComponentBase, IDisposable
{
    [Inject] protected ILibraryService Library { get; set; } = default!;
    [Inject] protected CurrentActor CurrentActor { get; set; } = default!;
    [Inject] protected AppState State { get; set; } = default!;
    protected Actor User { get; private set; } = new("", false, null);
    protected bool Loading { get; set; } = true;
    protected bool Busy { get; set; }
    protected string? LoadError { get; private set; }
    protected override async Task OnInitializedAsync()
    {
        User = await CurrentActor.GetAsync();
        State.DataInvalidated += OnInvalidated;
        await ReloadAsync();
    }
    protected abstract Task LoadAsync();
    protected async Task ReloadAsync()
    {
        try { LoadError = null; await LoadAsync(); }
        catch { LoadError = "We couldn’t load these records. Please refresh and try again."; }
        finally { Loading = false; }
    }
    protected async Task<bool> RunCommand(Func<Task<Result<int>>> operation, string success)
    {
        if (Busy) return false;
        Busy = true;
        try
        {
            var result = await operation();
            if (!result.IsSuccess) { State.ShowToast(result.Error, true); return false; }
            State.ShowToast(success);
            State.Invalidate();
            await ReloadAsync();
            return true;
        }
        catch { State.ShowToast("The operation could not be completed. Please refresh and try again.", true); return false; }
        finally { Busy = false; }
    }
    private void OnInvalidated() => _ = InvokeAsync(async () => { if (!Busy) { await ReloadAsync(); StateHasChanged(); } });
    public void Dispose() { State.DataInvalidated -= OnInvalidated; GC.SuppressFinalize(this); }
}
