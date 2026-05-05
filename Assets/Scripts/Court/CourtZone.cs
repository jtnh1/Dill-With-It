using UnityEngine;

public enum ZoneID
{
    Zone_A_Kitchen,
    Zone_A_Serve_Left,
    Zone_A_Serve_Right,
    Zone_A_Left,
    Zone_A_Right,
    Zone_B_Kitchen,
    Zone_B_Serve_Left,
    Zone_B_Serve_Right,
    Zone_B_Left,
    Zone_B_Right,
    OutOfBounds
}
[RequireComponent(typeof(BoxCollider))]
public class CourtZone : MonoBehaviour
{
    public ZoneID zoneID;

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    public ZoneID GetZone() => zoneID;
}
