using UnityEngine;

public class PlayerIdentity : MonoBehaviour
{
    [SerializeField] int playerId = -1;
    [SerializeField] int teamId;
    [SerializeField] int teamSlot;

    public int PlayerId => playerId;
    public int TeamId => teamId;
    public int TeamSlot => teamSlot;
    public bool IsAssigned => playerId >= 0;

    public void Apply(int newPlayerId, int newTeamId, int newTeamSlot)
    {
        playerId = newPlayerId;
        teamId = Mathf.Clamp(newTeamId, 0, 1);
        teamSlot = Mathf.Clamp(newTeamSlot, 0, 1);
    }

    public static int TeamOfPlayer(int id) => id % 2 == 0 ? 0 : 1;

    public static int SlotOfPlayer(int id)
    {
        return id switch
        {
            0 => 0,
            1 => 0,
            2 => 1,
            3 => 1,
            _ => 0
        };
    }

    public static int PlayerForTeamSlot(int team, int slot)
    {
        team = Mathf.Clamp(team, 0, 1);
        slot = Mathf.Clamp(slot, 0, 1);
        return team == 0
            ? (slot == 0 ? 0 : 2)
            : (slot == 0 ? 1 : 3);
    }
}
