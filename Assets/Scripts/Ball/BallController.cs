using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
{
    [Header("Physics")]
    public float bounciness = 0.75f;
    public float drag = 0.05f;
    public float CurrentSpeed => rb.linearVelocity.magnitude;

    private Rigidbody rb;
    private int lastHitByPlayer = 0;
    private ZoneID currentZone = ZoneID.OutOfBounds;
    private bool isLive = false;

    public event Action<Vector3> ResetPositionApplied;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = drag;
        rb.isKinematic = true; // frozen in place until first ApplyForce call

        PhysicsMaterial pm = new PhysicsMaterial("BallMat");
        pm.bounciness = bounciness;
        pm.bounceCombine = PhysicsMaterialCombine.Maximum;
        pm.frictionCombine = PhysicsMaterialCombine.Minimum;
        GetComponent<SphereCollider>().material = pm;
    }

    // Called by SwingExecutor. First call wakes gravity; subsequent calls redirect the ball.
    public void ApplyForce(Vector3 force)
    {
        if (!isLive) MakeLive();
        rb.linearVelocity = Vector3.zero;
        rb.AddForce(force, ForceMode.VelocityChange);
    }

    // Teleport ball to position and freeze it until next hit. Called by PickleballRulesEngine on reset.
    public void ResetToPosition(Vector3 position)
    {
        Debug.Log($"[BallController] ResetToPosition {position} | kinematic={rb.isKinematic}");
        isLive = false;
        rb.isKinematic = true;
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        transform.position = position;
        Physics.SyncTransforms();
        ResetPositionApplied?.Invoke(position);

        currentZone    = ZoneID.OutOfBounds;
        lastHitByPlayer = 0;
    }

    void MakeLive()
    {
        isLive = true;
        rb.isKinematic = false;
    }

    public Vector3 GetVelocity() => rb.linearVelocity;
    public void SetLastHitBy(int playerIndex) => lastHitByPlayer = playerIndex;

    private void OnCollisionEnter(Collision col)
    {
        if (!isLive) return;
        if (col.gameObject.CompareTag("Court")){
            PickleballRulesEngine.Instance?.OnBallBounced(currentZone, lastHitByPlayer);
            Debug.Log("[OnCollisionEnter] called - Court collision detected");
        }
        else if (col.gameObject.CompareTag("Out Of Bounds"))
            PickleballRulesEngine.Instance?.OnBallBounced(ZoneID.OutOfBounds, lastHitByPlayer);
        else if (col.gameObject.CompareTag("Net"))
            PickleballRulesEngine.Instance?.OnBallHitNet(lastHitByPlayer);
    }

    private void OnTriggerEnter(Collider other)
    {
        CourtZone zone = other.GetComponent<CourtZone>();
        if (zone != null) currentZone = zone.zoneID;
    }

    private void OnTriggerExit(Collider other)
    {
        CourtZone zone = other.GetComponent<CourtZone>();
        if (zone != null && zone.zoneID == currentZone) currentZone = ZoneID.OutOfBounds;
    }
}
