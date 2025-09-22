using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SnakeGame.Core;
using SnakeGame.AI;
using SnakeGame.Utils;
using SnakeGame.Data;

namespace SnakeGame.Core
{
    public class SnakeMovementController : MonoBehaviour
    {
        // ---------- Serialized config ----------
        [Header("Dependencies")] [SerializeField]
        private GridManager grid;

        [SerializeField] private FoodManager food;

        [Header("Snake Data (SO)")] [SerializeField]
        private SnakeDataSo snakeData;

        [Header("Visuals (optional)")] [SerializeField]
        private Transform headPrefab;

        [SerializeField] 
        private Transform bodyPrefab;
        [SerializeField] 
        private Material headMaterialOverride;
        [SerializeField] 
        private Material bodyMaterialOverride;
        [SerializeField] 
        private Transform snakeContainer;

        [Header("Execution")] [SerializeField] private bool useInternalLoop = true;

        private readonly List<GridPosition> bodyPositions = new();
        private readonly List<Transform> bodySegments = new();
        
        private static bool IsOpposite(in GridPosition a, in GridPosition b)
        {
            return a.X == -b.X && a.Y == -b.Y && a.Z == -b.Z;
        }

        private GridPosition currentDirection;
        private float currentSpeed = 2f;
        private int growthPending = 0;
        private bool isAlive = true;
        private Coroutine moveCoroutine;

        private IControlledSnakeAI ai;
        private bool initialised;

        // Registry so snakes can find each other (used for ad-hoc collision checks in internal mode)
        private static readonly Dictionary<string, SnakeMovementController> Registry = new();

        // ---------- Public API ----------
        public bool IsAlive => isAlive;

        public string SnakeId => snakeData != null && !string.IsNullOrEmpty(snakeData.SnakeID)
            ? snakeData.SnakeID
            : $"Snake_{GetInstanceID()}";

        public IReadOnlyList<GridPosition> Body => bodyPositions;
        public GridPosition HeadPosition => bodyPositions.Count > 0 ? bodyPositions[0] : default;
        public int Length => bodyPositions.Count;
        public GridPosition CurrentDirection => currentDirection;

        public event Action<GridPosition> OnMove;
        public event Action<GridPosition> OnEatFood;
        public event Action<string> OnDeath;

        // ----------------------------- Setup -----------------------------
        public void Configure(GridManager gridManager, FoodManager foodManager)
        {
            grid = gridManager;
            food = foodManager;
        }

        public void SetSnakeData(SnakeDataSo data)
        {
            snakeData = data;
        }

        public void SetAI(IControlledSnakeAI controllerAI)
        {
            ai = controllerAI;
        }

        public void AssignAI(IControlledSnakeAI controllerAI)
        {
            SetAI(controllerAI);
            // compatibility alias
        }

        private void Awake()
        {
            if (!snakeContainer)
            {
                var go = new GameObject("SnakeBody");
                go.transform.SetParent(transform, false);
                snakeContainer = go.transform;
            }
        }

        private void Start()
        {
            if (!ValidateSetup())
            {
                Debug.LogError($"[{name}] SnakeMovementController: missing GridManager or SnakeDataSo.");
                isAlive = false;
                enabled = false;
                return;
            }

            // Resolve prefabs from SO if not explicitly provided
            ResolvePrefabsFromSnakeData();

            // Register for cross-snake queries
            Registry[SnakeId] = this;

            InitializeSnake();

            if (useInternalLoop)
                moveCoroutine = StartCoroutine(MoveLoop());
        }

        private bool ValidateSetup()
        {
            if (grid == null || snakeData == null || food == null) return false;
            currentSpeed = Mathf.Max(0.1f, snakeData.BaseSpeed);
            initialised = true;
            return true;
        }

        private void ResolvePrefabsFromSnakeData()
        {
            // Try to pick head/body prefabs from SO (supports either Transform or GameObject fields).
            if (snakeData == null) return;
            var t = snakeData.GetType();

            if (headPrefab == null)
            {
                var hp = t.GetProperty("HeadPrefab");
                if (hp != null)
                {
                    var val = hp.GetValue(snakeData);
                    if (val is Transform ht) headPrefab = ht;
                    else if (val is GameObject hg) headPrefab = hg.transform;
                }
            }

            if (bodyPrefab == null)
            {
                var bp = t.GetProperty("BodyPrefab");
                if (bp != null)
                {
                    var val = bp.GetValue(snakeData);
                    if (val is Transform bt) bodyPrefab = bt;
                    else if (val is GameObject bg) bodyPrefab = bg.transform;
                }
            }
        }

        private void InitializeSnake()
        {
            bodyPositions.Clear();

            // Pick a valid start head and a direction that fits initial length
            var start = grid.GetRandomEmptyPosition();
            var dirs = GridPosition.Directions3D;
            var chosen = GridPosition.Right;
            var initLen = Mathf.Max(1, snakeData.StartLength);

            var placed = false;
            foreach (var d in dirs)
                if (TryBuildInitialBody(start, d, initLen))
                {
                    chosen = d;
                    placed = true;
                    break;
                }

            if (!placed)
            {
                bodyPositions.Add(start); // fallback to head only
                initLen = 1;
            }

            currentDirection = chosen;

            CreateOrSyncVisualSegments();
            UpdateGridOccupancy();

            Debug.Log($"[{SnakeId}] Spawned at {HeadPosition} len={Length} dir={currentDirection}");
        }

        private bool TryBuildInitialBody(in GridPosition headStart, in GridPosition dir, int len)
        {
            var pos = headStart;
            var tmp = new List<GridPosition>(len);
            for (var i = 0; i < len; i++)
            {
                if (!grid.IsValidPosition(pos) || grid.IsOccupied(pos)) return false;
                tmp.Add(pos);
                pos -= dir; // extend backwards
            }

            bodyPositions.AddRange(tmp);
            return true;
        }

        // ----------------------------- Internal loop (optional) -----------------------------
        private IEnumerator MoveLoop()
        {
            var wait = new WaitForSeconds(Mathf.Max(0.02f, 1f / currentSpeed));
            while (isAlive)
            {
                var dir = ComputeNextDirection();
                StepWithInternalCollision(dir);
                wait = new WaitForSeconds(Mathf.Max(0.02f, 1f / currentSpeed));
                yield return wait;
            }
        }

        /// <summary>
        /// Compute direction for the next tick (AI if available, else keep current).
        /// </summary>
        private GridPosition ComputeNextDirection()
        {
            // 2.1 First, ask AI if present
            if (ai != null)
                try
                {
                    var next = ai.GetNextMove(this);
                    if (next != GridPosition.Zero)
                        return next;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[{SnakeId}] AI GetNextMove threw: {ex.Message}");
                }

            // 2.2 No AI or AI had no preference → go greedily toward nearest food (if any)
            var target = food?.FindNearestFood(HeadPosition); // O(#food), no grid scan
            if (target.HasValue)
            {
                var dir = ChooseSafeStepToward(HeadPosition, target.Value);
                if (dir != GridPosition.Zero)
                    return dir;
            }

            // 2.3 Fallback: keep current direction or pick any safe direction
            if (currentDirection != GridPosition.Zero)
                return currentDirection;

            var any = PickAnySafeDirection();
            return any != GridPosition.Zero ? any : GridPosition.Right;
        }

        /// <summary>
        /// Choose a single-cell step that reduces Manhattan distance to target,
        /// preferring the axis with the largest absolute delta. Avoids reversing,
        /// out-of-bounds, and immediate self-collision.
        /// </summary>
        private GridPosition ChooseSafeStepToward(in GridPosition from, in GridPosition to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var dz = to.Z - from.Z;

            // Build axis candidates, ordered by |delta| descending (greedy)
            var candidates = new List<(int mag, GridPosition step)>(3);
            if (dx != 0) candidates.Add((Math.Abs(dx), new GridPosition(MathUtils.SignInt(dx), 0, 0)));
            if (dy != 0) candidates.Add((Math.Abs(dy), new GridPosition(0, MathUtils.SignInt(dy), 0)));
            if (dz != 0) candidates.Add((Math.Abs(dz), new GridPosition(0, 0, MathUtils.SignInt(dz))));
            candidates.Sort((a, b) => b.mag.CompareTo(a.mag)); // descending

            // Try greedy axes first
            foreach (var c in candidates)
            {
                var step = c.step;
                if (IsStepSafe(step))
                    return step;
            }

            // If greedy axes are blocked, try any safe direction
            return PickAnySafeDirection();
        }

        /// <summary>
        /// A direction is safe if the immediate next head cell is valid and not our body (ignoring tail that moves).
        /// Prevents 180° reversal.
        /// </summary>
        private bool IsStepSafe(in GridPosition step)
        {
            if (step == GridPosition.Zero) return false;

            // Don't reverse into our own neck
            if (IsOpposite(step, currentDirection) && bodyPositions.Count > 1)
                return false;

            var next = HeadPosition + step;

            // Bounds check
            if (!grid.IsValidPosition(next))
                return false;

            // Immediate self-collision (ignore last tail cell which may move)
            var limit = Mathf.Max(0, bodyPositions.Count - 1);
            for (var i = 0; i < limit; i++)
                if (bodyPositions[i].Equals(next))
                    return false;

            return true;
        }

        /// <summary>
        /// Try any of the 6 cardinal directions that are safe.
        /// </summary>
        private GridPosition PickAnySafeDirection()
        {
            var dirs = GridPosition.Directions3D; // {±X, ±Y, ±Z}
            // Try to keep current tendency first
            for (var pass = 0; pass < 2; pass++)
                foreach (var d in dirs)
                {
                    // First pass prefers not changing direction too much
                    if (pass == 0 && currentDirection != GridPosition.Zero && d != currentDirection) continue;
                    if (IsStepSafe(d)) return d;
                }

            // Nothing found
            return GridPosition.Zero;
        }

        /// <summary>
        /// Apply a move that has already been conflict-resolved externally (no inter-snake checks here).
        /// </summary>
        public void ApplyResolvedMove(in GridPosition direction)
        {
            if (!isAlive) return;

            var newHead = HeadPosition + direction;

            // Bounds + self check remain
            if (!grid.IsValidPosition(newHead) || SelfCollision(newHead))
            {
                Death($"Invalid move into {newHead}");
                return;
            }

            ClearGridOccupancy();

            bodyPositions.Insert(0, newHead);
            if (growthPending > 0) growthPending--;
            else if (bodyPositions.Count > 0) bodyPositions.RemoveAt(bodyPositions.Count - 1);

            UpdateGridOccupancy();
            CheckFoodCollision(newHead);
            CreateOrSyncVisualSegments(); // maintain head/body visuals (first segment is head)
            UpdateVisuals();

            currentDirection = direction;
            OnMove?.Invoke(newHead);
        }

        /// <summary>
        /// Internal mode only: does inter-snake resolution heuristically. For perfectly fair outcomes, prefer external tick.
        /// </summary>
        private void StepWithInternalCollision(in GridPosition direction)
        {
            if (!isAlive) return;
            var newHead = HeadPosition + direction;

            // Bounds + self collision
            if (!grid.IsValidPosition(newHead) || SelfCollision(newHead))
            {
                Death($"Invalid move into {newHead}");
                return;
            }

            // Heuristic inter-snake checks BEFORE clearing our occupancy
            var cell = grid.PeekCell(newHead);
            if (cell != null && !string.IsNullOrEmpty(cell.OccupantId) && cell.OccupantId != SnakeId)
            {
                // Body -> attacker dies
                if (cell.State == CellState.SnakeBody)
                {
                    Death($"Hit other snake's BODY at {newHead}");
                    return;
                }

                // Head-on (into existing head cell)
                if (cell.State == CellState.SnakeHead && TryResolveHeadOn(cell.OccupantId, newHead))
                    return; // resolution killed us; otherwise continue
            }

            // Extra: detect "same target" and "swap" head-on even if the other cleared its cell earlier
            foreach (var kv in Registry)
            {
                var other = kv.Value;
                if (other == null || !other.isAlive || other == this) continue;

                var otherNextDir = other.ComputeNextDirection(); // best-effort prediction
                var otherPlanned = other.HeadPosition + otherNextDir;

                // Same target cell head-on
                if (otherPlanned.Equals(newHead))
                    if (ResolveMutualHeadOn(other))
                        return;

                // Swap head-on (A -> B.head, B -> A.head)
                if (other.HeadPosition.Equals(newHead) && otherPlanned.Equals(HeadPosition))
                    if (ResolveMutualHeadOn(other))
                        return;
            }

            // Move and update
            ClearGridOccupancy();

            bodyPositions.Insert(0, newHead);
            if (growthPending > 0) growthPending--;
            else if (bodyPositions.Count > 0) bodyPositions.RemoveAt(bodyPositions.Count - 1);

            UpdateGridOccupancy();
            CheckFoodCollision(newHead);
            CreateOrSyncVisualSegments();
            UpdateVisuals();

            currentDirection = direction;
            OnMove?.Invoke(newHead);
        }

        // Try to resolve when we step into a cell marked as other snake's head.
        private bool TryResolveHeadOn(string otherId, in GridPosition newHead)
        {
            if (!Registry.TryGetValue(otherId, out var other) || other == null || !other.isAlive)
                return false;

            var myLen = Length;
            var otherLen = other.Length;

            if (myLen > otherLen)
            {
                other.Kill($"Head-on vs longer {SnakeId} at {newHead}");
                return false; // we survive
            }
            else if (myLen < otherLen)
            {
                Death($"Head-on vs longer {other.SnakeId} at {newHead}");
                return true;
            }
            else
            {
                other.Kill($"Head-on equal size vs {SnakeId} at {newHead}");
                Death($"Head-on equal size vs {other.SnakeId} at {newHead}");
                return true;
            }
        }

        // Resolve head-on when both attempt same cell or swap heads (best-effort in internal mode).
        private bool ResolveMutualHeadOn(SnakeMovementController other)
        {
            if (other == null || !other.isAlive) return false;

            // To avoid double-processing from both snakes, the lexicographically "smaller" id resolves.
            if (string.CompareOrdinal(SnakeId, other.SnakeId) > 0) return false;

            var myLen = Length;
            var otherLen = other.Length;

            if (myLen > otherLen)
            {
                other.Kill($"Head-on (mutual) vs longer {SnakeId}");
                return false;
            }
            else if (myLen < otherLen)
            {
                Death($"Head-on (mutual) vs longer {other.SnakeId}");
                return true;
            }
            else
            {
                other.Kill($"Head-on (mutual) equal vs {SnakeId}");
                Death($"Head-on (mutual) equal vs {other.SnakeId}");
                return true;
            }
        }

        private bool SelfCollision(in GridPosition p)
        {
            var limit = Mathf.Max(0, bodyPositions.Count - 1);
            for (var i = 0; i < limit; i++)
                if (bodyPositions[i].Equals(p))
                    return true;
            return false;
        }

        // ----------------------------- Grid/food/visuals -----------------------------
        private void UpdateGridOccupancy()
        {
            for (var i = 0; i < bodyPositions.Count; i++)
            {
                var isHead = i == 0;
                grid.SetCellOccupied(bodyPositions[i], SnakeId, isHead);
            }
        }

        private void ClearGridOccupancy()
        {
            for (var i = 0; i < bodyPositions.Count; i++)
                grid.ClearCell(bodyPositions[i]);
        }

        private void CheckFoodCollision(in GridPosition head)
        {
            if (food == null) return;
            if (!food.IsFoodAt(head)) return;

            var type = food.EatFood(head);
            if (type != null)
            {
                growthPending += 1; // simple rule: grow by 1
                OnEatFood?.Invoke(head);
            }
        }

        private void CreateOrSyncVisualSegments()
        {
            // Ensure we have transforms matching body count
            while (bodySegments.Count < bodyPositions.Count)
            {
                var index = bodySegments.Count;
                var prefab = index == 0 ? headPrefab ? headPrefab : bodyPrefab ? bodyPrefab : null
                    : bodyPrefab ? bodyPrefab
                    : headPrefab ? headPrefab : null;

                Transform t;
                if (prefab != null)
                {
                    t = Instantiate(prefab, snakeContainer);
                }
                else
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    var col = go.GetComponent<Collider>();
                    if (col) Destroy(col);
                    t = go.transform;
                    t.SetParent(snakeContainer, false);
                }

                t.name = index == 0 ? "Head" : $"Body_{index}";
                bodySegments.Add(t);
            }

            // Remove extra transforms if body shrank
            for (var i = bodySegments.Count - 1; i >= bodyPositions.Count; i--)
            {
                if (bodySegments[i]) Destroy(bodySegments[i].gameObject);
                bodySegments.RemoveAt(i);
            }

            // Apply materials (head/body) if overrides are provided
            for (var i = 0; i < bodySegments.Count; i++)
            {
                var t = bodySegments[i];
                if (!t) continue;

                if (i == 0 && headMaterialOverride)
                    TryApplySharedMaterial(t.gameObject, headMaterialOverride);
                else if (i > 0 && bodyMaterialOverride)
                    TryApplySharedMaterial(t.gameObject, bodyMaterialOverride);
            }
        }

        private void TryApplySharedMaterial(GameObject go, Material mat)
        {
            if (!go || !mat) return;
            var mr = go.GetComponentInChildren<Renderer>();
            if (mr) mr.sharedMaterial = mat; // use shared to avoid instantiation/allocs
        }

        private void UpdateVisuals()
        {
            for (var i = 0; i < bodyPositions.Count && i < bodySegments.Count; i++)
            {
                var seg = bodySegments[i];
                if (!seg) continue;

                seg.position = grid.GridToWorld(bodyPositions[i]);

                if (i == 0 && currentDirection != GridPosition.Zero)
                {
                    var dirVector = new Vector3(currentDirection.X, currentDirection.Y, currentDirection.Z);
                    if (dirVector != Vector3.zero) seg.rotation = Quaternion.LookRotation(dirVector);
                }
            }
        }

        // ----------------------------- Death/cleanup -----------------------------
        private void Kill(string reason)
        {
            Death(reason);
        }

        private void Death(string reason)
        {
            if (!isAlive) return;
            isAlive = false;

            Debug.Log($"[{SnakeId}] Death: {reason}");

            if (moveCoroutine != null) StopCoroutine(moveCoroutine);
            ClearGridOccupancy();
            OnDeath?.Invoke(reason);

            StartCoroutine(DeathAnimation());
        }

        private IEnumerator DeathAnimation()
        {
            for (var i = bodySegments.Count - 1; i >= 0; i--)
            {
                if (bodySegments[i]) bodySegments[i].gameObject.SetActive(false);
                yield return new WaitForSeconds(0.05f);
            }

            Destroy(gameObject, 0.5f);
        }

        private void OnDestroy()
        {
            Registry.Remove(SnakeId);
            if (grid != null && isAlive) ClearGridOccupancy();
        }
    }
}