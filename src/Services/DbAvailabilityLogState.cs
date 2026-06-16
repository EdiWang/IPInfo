namespace IPInfo.Services;

public sealed class DbAvailabilityLogState
{
    private int _unavailableLogged;

    public bool TryMarkUnavailable()
    {
        return Interlocked.Exchange(ref _unavailableLogged, 1) == 0;
    }

    public void MarkAvailable()
    {
        Interlocked.Exchange(ref _unavailableLogged, 0);
    }
}
