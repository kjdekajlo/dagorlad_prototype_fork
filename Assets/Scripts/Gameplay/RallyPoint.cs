using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;

public class RallyPoint : MonoBehaviour
{
    public Team team;
    Vector3 mousePositionOffset;
    DraggableMovement draggableMovement;
    public OnTrigger onTrigger;
    public GameObject home;

    public List<OurUnit> currentArmy = new();
    public List<GameObject> EnemiesInRange = new();

    public int MAX_UNITS = 30;
    public List<Vector3> targetPositionlist = new();

    //public class FormationPosition
    //{
    //    public Vector3 
    //}

    Animator[] animators;

    private void Start()
    {
        //Debug.Log(transform.parent.TransformPoint(transform.localPosition));
        // for enemies targetPositionlist = GetPositionListAround2(transform.parent.TransformPoint(transform.localPosition), new float[] { 0.3f, 0.6f, 0.9f }, new int[] { 5, 10, 15 });
        //targetPositionlist = GetPositionListAround2(transform.position - new Vector3(0, 0.25f, 0), new float[] { 0.5f, 0.8f, 1.1f }, new int[] { 5, 10, 15 });
        //targetPositionlist.RemoveAt(0);

        //var cicle = transform.GetChild(1).GetComponent<SpriteRenderer>().sprite;
        //foreach(var x in targetPositionlist)
        //{
        //    var attk_pos = new GameObject();

        //    attk_pos.AddComponent<SpriteRenderer>().sprite = cicle;
        //    var colr = Color.white;
        //    colr.a = 0.25f;
        //    attk_pos.GetComponent<SpriteRenderer>().color = colr;
        //    attk_pos.transform.localScale *= 0.5f;// Debug.Log(x.magnitude);

        //    attk_pos.transform.position = x;

        //    attk_pos.transform.parent = transform;
        //    attk_pos.name = "attk_pos";
        //}

        draggableMovement = GetComponent<DraggableMovement>();
        animators = GetComponentsInChildren<Animator>();
        string hostileTag = Teams.Tag(Teams.Opponent(team));
        GetComponentInChildren<OnTrigger>().AddEvent("Enter", hostileTag, (sender, collider) => {
            // Compare by object, not name: enemy names repeat across camps ("Enemy (1)" ...).
            if (!EnemiesInRange.Contains(collider.gameObject))
            {
                EnemiesInRange.Add(collider.gameObject);
                ManageTargets();
            }
        });
        GetComponentInChildren<OnTrigger>().AddEvent("Exit", hostileTag, (sender, collider) => {
            if (EnemiesInRange.Remove(collider.gameObject))
            {
                foreach (var x in currentArmy)
                {
                    if (x != null) x.RemoveAttacker(collider.transform);
                }
                ManageTargets();
            }
        });
        ManageTargets();
    }
    private void Update()
    {
        if (team == Team.Enemy && currentArmy.Count == 0)
        {
            if (home != null) home.GetComponent<Base>().team = Team.Neutral;
            GameManager.Instance.enemies.Remove(this);
            if(GameManager.Instance.enemies.Count == 0)
            {
                Debug.Log("GameWon");
                UIManager.Instance.winWindow.SetActive(true);
            }
            Destroy(gameObject);
        }
    }

    private void OnDestroy() {
        ClearArmy();
    }

    private void ClearArmy() {
        foreach (var unit in currentArmy) {
            if (unit) {
                ResourceManager.Instance.RefundUnitSupply(unit.cost[0]);
                Destroy(unit.gameObject);
            }
        }
    }

    public void NewUnitSpawned(OurUnit unit)
    {
        currentArmy.Add(unit);
        ManageTargets();
        unit.onSpawned += NotifyNewUnitOnLocation;
    }
    public void ManageTargets()
    {
        EnemiesInRange.RemoveAll(x => x == null);

        // Nothing to fight. Units drop targets on their own once they leave the area, and
        // stay where they are afterwards (they only re-form when the flag is moved).
        if (EnemiesInRange.Count == 0) return;

        // Auto-acquire: idle units pick the closest enemy; units already fighting keep their target.
        foreach (OurUnit unit in currentArmy)
        {
            if (unit == null || unit.IsDead) continue;
            unit.Attack(FindClosestEnemy(unit.transform.position));
        }
    }

    public GameObject FindClosestEnemy(Vector3 unitPosition)
    {
        GameObject closestEnemy = null;
        float closestDistance = float.MaxValue;

        foreach (GameObject enemy in EnemiesInRange)
        {
            if (enemy == null) continue;
            if (enemy.TryGetComponent<StatsManager>(out var stats) && !OurUnit.IsAlive(stats)) continue;

            float distance = Vector3.Distance(unitPosition, enemy.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestEnemy = enemy;
            }
        }

        return closestEnemy;
    }
    public void ON()
    {
        Debug.Log("AAAAABB");
        Camera.main.GetComponent<CameraMovement>().IsLocked = true;
        Camera.main.GetComponent<CameraMovement>().EnableEdgeScrolling();
        mousePositionOffset = gameObject.transform.position - GetMouseWorldPosition();

    }
    public void HOLD()
    {
        PlayPickRallyPointUpAnimation();

        draggableMovement.SetDestination(GetMouseWorldPosition() + mousePositionOffset);
        gameObject.transform.position = GetMouseWorldPosition() + mousePositionOffset;
    }
    public void OFF() {
        Camera.main.GetComponent<CameraMovement>().IsLocked = false;
        Camera.main.GetComponent<CameraMovement>().DisableEdgeScrolling();
        PlayPutRallyPointDownAnimation();
        Camera.main.GetComponent<Animator>().SetTrigger("SmallShake");
        ManageTargets();
        MoveIdleUnitsToFormation();
    }

    public void PlayPutRallyPointDownAnimation() {
        foreach (var a in animators) {
            a.SetBool("HOLD", false);
        }
    }

    public void PlayPickRallyPointUpAnimation() {
        foreach (var a in animators) {
            a.SetBool("HOLD", true);
        }
    }

    private Vector3 GetMouseWorldPosition()
    {
        return Camera.main.ScreenToWorldPoint(Input.mousePosition);
    }

    private List<Vector3> GetPositionListAround2(Vector3 startPosition, float[] ringDistanceArray, int[] ringpositioncountArray)
    {
        Debug.Log("!!" + startPosition);
        List<Vector3> positionlist = new List<Vector3>();
        positionlist.Add(startPosition);
        for (int i = 0; i < ringDistanceArray.Length; i++) {
            positionlist.AddRange(GetPositionlistAround(startPosition, ringDistanceArray[i], ringpositioncountArray[i]));
        }
        return positionlist;
    }
    private List<Vector3> GetPositionlistAround(Vector3 startPosition, float distance, int positionCount)
    {
        List<Vector3> positionlist = new List<Vector3>();
        for (int i = 0; i < positionCount; i++)
        {
            float angle = i * (360f / positionCount);
            Vector3 dir = ApplyRotationToVector(new Vector3(1, 0), angle);
            Vector3 position = startPosition + dir * distance;
            positionlist.Add(position);
        }
        
        return positionlist;
    }
    private Vector3 ApplyRotationToVector(Vector3 vec, float angle)
    { 
        return Quaternion.Euler(0, 0, angle) * vec;
    }

    private void NotifyUnitOnLocation(OurUnit unit, Vector2 location) {
        if (unit && !unit.IsDead && unit.TryGetComponent<UnitMovement>(out var unit_movement)) unit_movement.MoveTo(location);
    }

    private const float FORMATION_RADIUS = 0.75f;

    /// <summary>This unit's spot in the ring formation around the flag.</summary>
    public Vector2 GetFormationPosition(OurUnit unit) {
        int index = currentArmy.IndexOf(unit);
        if (index < 0 || currentArmy.Count == 0) return transform.position;
        float angle = index * (360f / currentArmy.Count);
        return transform.position + ApplyRotationToVector(Vector3.right, angle) * FORMATION_RADIUS;
    }

    /// <summary>Units that aren't fighting walk to their formation spot (used when the flag is moved).</summary>
    private void MoveIdleUnitsToFormation() {
        foreach (var unit in currentArmy) {
            if (unit != null && !unit.IsAttacking) NotifyUnitOnLocation(unit, GetFormationPosition(unit));
        }
    }

    /// <summary>Radius of the flag's area (Point_Range trigger), in world units.</summary>
    public float AreaRadius {
        get {
            var range = transform.Find("Point_Range");
            if (range != null && range.TryGetComponent<CircleCollider2D>(out var col)) return col.radius * range.lossyScale.x;
            return 3f;
        }
    }

    public bool IsInArea(Vector2 position, float slack) =>
        Vector2.Distance(position, transform.position) <= AreaRadius + slack;

    private void NotifyNewUnitOnLocation(OurUnit unit) {
        NotifyUnitOnLocation(unit, GetFormationPosition(unit));
        unit.onSpawned -= NotifyNewUnitOnLocation;
    }
}