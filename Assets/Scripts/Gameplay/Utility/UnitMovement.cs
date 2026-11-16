using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Thin wrapper around NavMeshAgent for 2D units.
///
/// Three modes:
///  - Idle:    walk to the last point given via MoveTo(), then stand.
///  - Follow:  keep chasing a moving Transform (e.g. an attack slot on an enemy).
///             While HoldPosition is true the destination is not refreshed.
///  - Stopped: agent halted (in melee range, dead, ...).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class UnitMovement : MonoBehaviour
{
    public enum Mode { Idle, Follow, Stopped }

    [SerializeField] private Mode mode = Mode.Idle;
    public Mode CurrentMode => mode;

    /// <summary>Set from the MoveSpeed stat by StatsManager. Not applied to the agent yet (agent speed is fixed).</summary>
    public float Speed;

    /// <summary>While following, freeze destination updates (used during an attack swing).</summary>
    public bool HoldPosition;

    public Transform FollowTarget { get; private set; }

    // Agent tuning
    private const float AGENT_SPEED = 1f;
    private const float AGENT_ACCELERATION = 6f;
    private const float AGENT_STOPPING_DISTANCE = 0.2f;
    private const int AGENT_AVOIDANCE_PRIORITY = 50;

    // Path update throttling (reduces jitter from re-pathing every tick)
    private const float REPATH_MIN_DISTANCE = 0.3f;
    private const float REPATH_MIN_INTERVAL = 0.15f;

    private NavMeshAgent agent;
    private Vector2 lastDestination;
    private float lastDestinationTime = float.NegativeInfinity;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.updateRotation = false;
            agent.updateUpAxis = false;
            agent.angularSpeed = 0f;
            agent.speed = AGENT_SPEED;
            agent.acceleration = AGENT_ACCELERATION;
            agent.stoppingDistance = AGENT_STOPPING_DISTANCE;
            agent.avoidancePriority = AGENT_AVOIDANCE_PRIORITY;
        }
        lastDestination = transform.position;
    }

    private void FixedUpdate()
    {
        if (mode != Mode.Follow || HoldPosition) return;

        if (FollowTarget == null)
        {
            // Target vanished (died / destroyed) - stand where we are.
            mode = Mode.Idle;
            return;
        }

        SetDestination(FollowTarget.position, force: false);
    }

    /// <summary>Walk to a fixed point and stand there.</summary>
    public void MoveTo(Vector2 position)
    {
        mode = Mode.Idle;
        FollowTarget = null;
        SetDestination(position, force: false);
    }

    /// <summary>Continuously chase a moving target.</summary>
    public void Follow(Transform target)
    {
        if (target == null) { MoveTo(transform.position); return; }

        bool newTarget = mode != Mode.Follow || FollowTarget != target;
        mode = Mode.Follow;
        FollowTarget = target;
        SetDestination(target.position, force: newTarget);
    }

    /// <summary>Halt immediately.</summary>
    public void Stop()
    {
        mode = Mode.Stopped;
        FollowTarget = null;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
    }

    private void SetDestination(Vector2 destination, bool force)
    {
        if (agent == null || !agent.isOnNavMesh) return;

        // Throttle only while already moving; a stopped agent must always be restarted.
        if (!force && !agent.isStopped
            && Vector2.Distance(lastDestination, destination) < REPATH_MIN_DISTANCE
            && Time.time - lastDestinationTime < REPATH_MIN_INTERVAL)
        {
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(destination);
        lastDestination = destination;
        lastDestinationTime = Time.time;
    }
}
