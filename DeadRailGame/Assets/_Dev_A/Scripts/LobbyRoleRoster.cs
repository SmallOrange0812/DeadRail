using System;
using Fusion;
using UnityEngine;

[Flags]
public enum TrainRole
{
    None = 0,
    Driver = 1,
    Searchlight = 2,
    Gunner = 4,
    Radar = 8
}

// Lives on the persistent runner so the host can use the lobby assignment during spawning.
public sealed class LobbyRoleRoster : MonoBehaviour
{
    private const int MaximumSeats = 4;
    private readonly PlayerRef[] owners = new PlayerRef[MaximumSeats];
    internal int Revision { get; private set; }

    internal static int SeatForCard(int card, int limit) =>
        limit == 2 ? (card == 1 || card == 4 ? 1 : 2) : card;

    internal static TrainRole RolesForSeat(int seat, int limit) => (seat, limit) switch
    {
        (1, 2) => TrainRole.Driver | TrainRole.Radar,
        (2, 2) => TrainRole.Searchlight | TrainRole.Gunner,
        (1, 4) => TrainRole.Driver,
        (2, 4) => TrainRole.Searchlight,
        (3, 4) => TrainRole.Gunner,
        (4, 4) => TrainRole.Radar,
        _ => TrainRole.None
    };

    internal static string RoleDisplayName(TrainRole roles) => roles switch
    {
        TrainRole.Driver => nameof(TrainRole.Driver),
        TrainRole.Searchlight => nameof(TrainRole.Searchlight),
        TrainRole.Gunner => nameof(TrainRole.Gunner),
        TrainRole.Radar => nameof(TrainRole.Radar),
        (TrainRole.Driver | TrainRole.Radar) => nameof(TrainRole.Driver) + " / " + nameof(TrainRole.Radar),
        (TrainRole.Searchlight | TrainRole.Gunner) => nameof(TrainRole.Searchlight) + " / " + nameof(TrainRole.Gunner),
        _ => null
    };

    internal PlayerRef OwnerOfSeat(int seat) =>
        seat >= 1 && seat <= owners.Length ? owners[seat - 1] : PlayerRef.None;

    internal int SeatOf(PlayerRef player) => player.IsRealPlayer ? Array.IndexOf(owners, player) + 1 : 0;

    internal bool Reconcile(PlayerRef[] players, PlayerRef host, int limit)
    {
        int previous = Revision;
        for (int i = 0; i < owners.Length; i++)
        {
            if (owners[i] == PlayerRef.None || (i < limit && Array.IndexOf(players, owners[i]) >= 0)) continue;
            owners[i] = PlayerRef.None;
            Revision++;
        }
        if (Array.IndexOf(players, host) >= 0) AssignVacantSeat(host, limit);
        foreach (PlayerRef player in players) AssignVacantSeat(player, limit);
        return previous != Revision;
    }

    private void AssignVacantSeat(PlayerRef player, int limit)
    {
        if (SeatOf(player) != 0) return;
        for (int seat = 1; seat <= limit; seat++)
            if (TryMove(player, seat, limit)) return;
    }

    internal bool TryMove(PlayerRef player, int seat, int limit)
    {
        if (!player.IsRealPlayer || seat < 1 || seat > limit || seat > owners.Length ||
            OwnerOfSeat(seat) != PlayerRef.None) return false;
        int from = SeatOf(player);
        if (from != 0) owners[from - 1] = PlayerRef.None;
        owners[seat - 1] = player;
        Revision++;
        return true;
    }

    internal byte[] Serialize()
    {
        var data = new int[MaximumSeats + 1];
        data[0] = Revision;
        for (int i = 0; i < owners.Length; i++) data[i + 1] = owners[i].RawEncoded;
        var bytes = new byte[data.Length * sizeof(int)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    internal void ApplySnapshot(byte[] bytes)
    {
        if (bytes.Length != (MaximumSeats + 1) * sizeof(int)) return;
        var data = new int[MaximumSeats + 1];
        Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
        if (data[0] <= Revision) return;
        for (int i = 0; i < owners.Length; i++) owners[i] = PlayerRef.FromEncoded(data[i + 1]);
        Revision = data[0];
    }
}
