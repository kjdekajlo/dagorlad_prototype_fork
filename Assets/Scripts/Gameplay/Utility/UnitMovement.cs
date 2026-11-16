using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Thin wrapper around NavMeshAgent for 2D units.
///
/// Three modes:
///  - Idle:    walk to the last point given via MoveTo(), then stand.
///  - Follow:  keep chasing a moving Transform (an enemy or an attack slot around it).
///  - Stopped: halted on the spot (fighting, dead, ...). Stopped units get a higher
///             avoidance priority so moving units walk around them instead of shoving them.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class UnitMovement : MonoBehaviour
{
    public enum Mode { Idle, Follow, Stopped }

    [SerializeField] private Mode mode = Mode.Idle;
    public Mode CurrentMode => mode;

    /// <summary>Set from the MoveSpeed stat by StatsManager. Not applied to the agent yet (agent speed is fixed).</summary>
    public float Speed;

    public Transform FollowTarget { get; private set; }

    // Agent tuning
    private const float AGENT_SPEED = 1f;
    private const float AGENT_ACCELERATION = 6f;
    private const float AGENT_STOPPING_DISTANCE = 0.05f;
    private const int AVOIDANCE_PRIORITY_MOVING = 50;
    private const int AVOIDANCE_PRIORITY_STOPPED = 30; // lower = more important; movers go around

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
            agent.avoidancePriority = AVOIDANCE_PRIORITY_MOVING;
        }
        lastDestination = transform.position;
    }

    private void FixedUpdate()
    {
        if (mode != Mode.Follow) return;

        if (FollowTarget == null)
        {
            // Target vanished (died / destroyed) - stand where we are.
            Stop();
            return;
        }

        SetDestination(FollowTarget.position, force: false);
    }

    /// <summary>Walk to a fixed point and stand there.</summary>
    public void MoveTo(Vector2 position)
    {
        bool wasStopped = mode == Mode.Stopped;
        mode = Mode.Idle;
        FollowTarget = null;
        SetDestination(position, force: wasStopped);
    }

    /// <summary>Continuously chase a moving target.</summary>
    public void Follow(Transform target)
    {
        if (target == null) { Stop(); return; }

        bool newTarget = mode != Mode.Follow || FollowTarget != target;
        mode = Mode.Follow;
        FollowTarget = target;
        SetDestination(target.position, force: newTarget);
    }

    /// <summary>Halt immediately on the spot.</summary>
    public void Stop()
    {
        mode = Mode.Stopped;
        FollowTarget = null;
        if (agent == null || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.ResetPath();
        agent.avoidancePriority = AVOIDANCE_PRIORITY_STOPPED;
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
        agent.avoidancePriority = AVOIDANCE_PRIORITY_MOVING;
        agent.SetDestination(destination);
        lastDestination = destination;
        lastDestinationTime = Time.time;
    }
}
