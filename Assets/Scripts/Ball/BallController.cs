using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
{
    [Header("Physics")]
    public float bounciness = 0.75f;
    public float drag = 0.05f;
    public float CurrentSpeed => rb.linearVelocity.magnitude;

    [Header("Net")]
    [Tooltip("Velocity multiplier applied immediately after hitting the net so the ball dies instead of bouncing high.")]
    [Range(0f, 1f)]
    public float netVelocityDamping = 0.15f;
    [Tooltip("Maximum upward velocity allowed after a net collision.")]
    public float netMaxUpwardVelocity = 0.5f;
    [Tooltip("Prevents stacked court/OOB colliders from reporting the same physical bounce more than once.")]
    public float minBounceReportInterval = 0.1f;

    private Rigidbody rb;
    private SphereCollider ballCollider;
    private int lastHitByPlayer = 0;
    private ZoneID currentZone = ZoneID.OutOfBounds;
    private bool isLive = false;
    private readonly HashSet<Collider> ignoredPlayerColliders = new HashSet<Collider>();
    private readonly HashSet<CourtZone> overlappingZones = new HashSet<CourtZone>();
    private float lastBounceReportTime = -999f;
    private int lastBounceReportFrame = -1;

    public event Action<Vector3> ResetPositionApplied;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ballCollider = GetComponent<SphereCollider>();
        rb.linearDamping = drag;
        rb.isKinematic = true; // frozen in place until first ApplyForce call

        PhysicsMaterial pm = new PhysicsMaterial("BallMat");
        pm.bounciness = bounciness;
        pm.bounceCombine = PhysicsMaterialCombine.Maximum;
        pm.frictionCombine = PhysicsMaterialCombine.Minimum;
        ballCollider.material = pm;
    }

    private IEnumerator Start()
    {
        while (true)
        {
            RefreshIgnoredPlayerCollisions();
            yield return new WaitForSeconds(0.5f);
        }
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
        overlappingZones.Clear();
        ResetBounceDebounce();
    }

    void MakeLive()
    {
        isLive = true;
        rb.isKinematic = false;
    }

    public Vector3 GetVelocity() => rb.linearVelocity;
    public void SetLastHitBy(int playerIndex)
    {
        lastHitByPlayer = playerIndex;
        ResetBounceDebounce();
    }

    private void OnCollisionEnter(Collision col)
    {
        if (!isLive) return;

        if (col.gameObject.CompareTag("Court"))
        {
            ReportBounce(ResolveLandingZone(col), col.gameObject.tag);
        }
        else if (col.gameObject.CompareTag("Out Of Bounds"))
        {
            ReportBounce(ResolveLandingZone(col), col.gameObject.tag);
        }
        else if (col.gameObject.CompareTag("Net"))
        {
            DampenNetBounce();
            PickleballRulesEngine.Instance?.OnBallHitNet(lastHitByPlayer);
        }
    }

    void DampenNetBounce()
    {
        Vector3 velocity = rb.linearVelocity * netVelocityDamping;
        velocity.y = Mathf.Min(velocity.y, netMaxUpwardVelocity);
        rb.linearVelocity = velocity;
        rb.angularVelocity *= netVelocityDamping;
    }

    ZoneID ResolveLandingZone(Collision collision)
    {
        Vector3 sample = transform.position;
        if (collision != null && collision.contactCount > 0)
            sample = collision.GetContact(0).point;

        ZoneID zone = PickBestZone(Physics.OverlapSphere(sample, 0.3f, ~0, QueryTriggerInteraction.Collide));
        if (zone != ZoneID.OutOfBounds)
            return zone;

        float radius = ballCollider != null ? ballCollider.radius + 0.1f : 0.35f;
        zone = PickBestZone(Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Collide));
        if (zone != ZoneID.OutOfBounds)
            return zone;

        currentZone = PickBestZone(overlappingZones);
        return currentZone;
    }

    ZoneID PickBestZone(Collider[] overlaps)
    {
        ZoneID bestZone = ZoneID.OutOfBounds;
        float bestVolume = float.PositiveInfinity;
        bool sawOutOfBounds = false;

        foreach (Collider overlap in overlaps)
        {
            CourtZone zone = overlap.GetComponent<CourtZone>();
            if (zone == null) continue;

            if (zone.zoneID == ZoneID.OutOfBounds)
            {
                sawOutOfBounds = true;
                continue;
            }

            float volume = ZoneVolume(zone);
            if (volume >= bestVolume) continue;

            bestVolume = volume;
            bestZone = zone.zoneID;
        }

        currentZone = bestZone != ZoneID.OutOfBounds || sawOutOfBounds ? bestZone : currentZone;
        return currentZone;
    }

    ZoneID PickBestZone(HashSet<CourtZone> zones)
    {
        ZoneID bestZone = ZoneID.OutOfBounds;
        float bestVolume = float.PositiveInfinity;
        bool sawOutOfBounds = false;

        foreach (CourtZone zone in zones)
        {
            if (zone == null) continue;

            if (zone.zoneID == ZoneID.OutOfBounds)
            {
                sawOutOfBounds = true;
                continue;
            }

            float volume = ZoneVolume(zone);
            if (volume >= bestVolume) continue;

            bestVolume = volume;
            bestZone = zone.zoneID;
        }

        return bestZone != ZoneID.OutOfBounds || sawOutOfBounds ? bestZone : ZoneID.OutOfBounds;
    }

    float ZoneVolume(CourtZone zone)
    {
        Collider col = zone.GetComponent<Collider>();
        if (col == null) return float.PositiveInfinity;
        Vector3 size = col.bounds.size;
        return size.x * size.y * size.z;
    }

    void ReportBounce(ZoneID zone, string sourceTag)
    {
        if (ShouldIgnoreDuplicateBounce(zone, sourceTag))
            return;

        lastBounceReportTime = Time.time;
        lastBounceReportFrame = Time.frameCount;
        PickleballRulesEngine.Instance?.OnBallBounced(zone, lastHitByPlayer);
        Debug.Log($"[BallBounce] source={sourceTag} | zone={zone} | lastHit={lastHitByPlayer}");
    }

    bool ShouldIgnoreDuplicateBounce(ZoneID zone, string sourceTag)
    {
        bool sameFrame = Time.frameCount == lastBounceReportFrame;
        bool tooSoon = Time.time - lastBounceReportTime < minBounceReportInterval;
        if (!sameFrame && !tooSoon) return false;

        Debug.Log($"[BallBounce] Ignored duplicate bounce | source={sourceTag} | zone={zone} | lastHit={lastHitByPlayer}");
        return true;
    }

    void ResetBounceDebounce()
    {
        lastBounceReportTime = -999f;
        lastBounceReportFrame = -1;
    }

    void RefreshIgnoredPlayerCollisions()
    {
        if (ballCollider == null) return;

        foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            IgnorePlayerCollision(player.GetComponent<CharacterController>());

        foreach (AIBot ai in FindObjectsByType<AIBot>(FindObjectsSortMode.None))
            IgnorePlayerCollision(ai.GetComponent<CharacterController>());
    }

    void IgnorePlayerCollision(Collider playerCollider)
    {
        if (playerCollider == null || ignoredPlayerColliders.Contains(playerCollider)) return;

        Physics.IgnoreCollision(ballCollider, playerCollider, true);
        ignoredPlayerColliders.Add(playerCollider);
    }

    private void OnTriggerEnter(Collider other)
    {
        CourtZone zone = other.GetComponent<CourtZone>();
        if (zone == null) return;

        overlappingZones.Add(zone);
        currentZone = PickBestZone(overlappingZones);
    }

    private void OnTriggerExit(Collider other)
    {
        CourtZone zone = other.GetComponent<CourtZone>();
        if (zone == null) return;

        overlappingZones.Remove(zone);
        currentZone = PickBestZone(overlappingZones);
    }
}
