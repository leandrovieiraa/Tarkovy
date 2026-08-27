using Tarkovy.Models;

namespace Tarkovy.Services;

public sealed class RaidSession
{
    public RaidStatus Status { get; private set; } = RaidStatus.Watching;
    public MapDefinition? CurrentMap { get; private set; }
    public PlayerFix? LastPosition { get; private set; }

    public event Action? Changed;

    public void SetWatching()
    {
        Status = RaidStatus.Watching;
        Changed?.Invoke();
    }

    public void SetMap(MapDefinition map)
    {
        CurrentMap = map;
        if (Status == RaidStatus.Idle || Status == RaidStatus.Watching)
            Status = RaidStatus.Loading;
        Changed?.Invoke();
    }

    public void SetRaidStarted()
    {
        Status = RaidStatus.InRaid;
        Changed?.Invoke();
    }

    public void SetRaidEnded()
    {
        Status = RaidStatus.Watching;
        LastPosition = null;
        Changed?.Invoke();
    }

    public void SetPosition(PlayerFix fix)
    {
        LastPosition = fix;
        if (Status != RaidStatus.InRaid)
            Status = RaidStatus.InRaid;
        Changed?.Invoke();
    }

    public string StatusLabel => Status switch
    {
        RaidStatus.InRaid => Loc.T("Status.InRaid"),
        RaidStatus.Loading => Loc.T("Status.Loading"),
        RaidStatus.Watching => Loc.T("Status.Watching"),
        _ => Loc.T("Status.Idle")
    };
}
