using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SnakeGame.AI;
using SnakeGame.Data;

namespace SnakeGame.Core
{
    public class SnakeMovementController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField]
        private GridManager grid;

        [SerializeField]
        private FoodManager food;

        [SerializeField]
        private SnakeDataSo snakeData;

        [Header("Visuals")]
        [SerializeField]
        private Transform headPrefab;

        [SerializeField]
        private Transform bodyPrefab;

        [Tooltip("If empty, will try to take material from HeadPrefab")]
        [SerializeField]
        private Material headMaterialOverride;

        [Tooltip("If empty, will try to take material from BodyPrefab")]
        [SerializeField]
        private Material bodyMaterialOverride;

        [SerializeField]
        private Transform snakeContainer;

        [Header("Execution")]
        [SerializeField]
        private bool useInternalLoop = true;

        private readonly List<GridPosition> _bodyPositions = new();
        private readonly List<Transform> _bodyTransforms = new();

        private GridPosition _currentDirection;
        private float _currentSpeed = 2f;
        private int _growthPending = 0;
        private bool _isAlive = true;
        private Coroutine _moveCoroutine;
        private IControlledSnakeAI _ai;

        private Material _runtimeHeadMat;
        private Material _runtimeBodyMat;

        private WaitForSeconds _cachedWait;
        private float _lastCachedSpeed = -1f;

        private static readonly Dictionary<string, SnakeMovementController> Registry = new();

        private readonly Stack<Transform> _segmentPool = new();

        public bool IsAlive => _isAlive;
        public string SnakeId => snakeData ? snakeData.SnakeID : name;
        public IReadOnlyList<GridPosition> Body => _bodyPositions;

        // Head is now at index 0
        public GridPosition HeadPosition => _bodyPositions.Count > 0 ? _bodyPositions[0] : default;
        private int Length => _bodyPositions.Count;
        public GridPosition CurrentDirection => _currentDirection;

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

        public void AssignAI(IControlledSnakeAI controllerAI)
        {
            _ai = controllerAI;
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
                return;
            }

            Registry[SnakeId] = this;

            // Load Prefabs AND Materials
            ResolveVisualsFromData();
            InitializeSnake();

            if (useInternalLoop)
            {
                _moveCoroutine = StartCoroutine(MoveLoop());
            }
        }

        private bool ValidateSetup()
        {
            if (!grid || !food || !snakeData)
            {
                Debug.LogError($"[{name}] Missing dependencies!");
                _isAlive = false;
                enabled = false;

                return false;
            }

            _currentSpeed = Mathf.Max(0.1f, snakeData.BaseSpeed);

            return true;
        }

        private void ResolveVisualsFromData()
        {
            if (!snakeData)
            {
                return;
            }

            // 1. Resolve Prefabs
            if (!headPrefab && snakeData.HeadPrefab)
            {
                headPrefab = snakeData.HeadPrefab.transform;
            }

            if (!bodyPrefab && snakeData.BodyPrefab)
            {
                bodyPrefab = snakeData.BodyPrefab.transform;
            }

            // 2. Resolve Materials
            _runtimeHeadMat = headMaterialOverride;

            if (!_runtimeHeadMat && headPrefab)
            {
                var r = headPrefab.GetComponentInChildren<Renderer>();

                if (r)
                {
                    _runtimeHeadMat = r.sharedMaterial;
                }
            }

            _runtimeBodyMat = bodyMaterialOverride;

            if (!_runtimeBodyMat && bodyPrefab)
            {
                var r = bodyPrefab.GetComponentInChildren<Renderer>();

                if (r)
                {
                    _runtimeBodyMat = r.sharedMaterial;
                }
            }
        }

        private void InitializeSnake()
        {
            CleanupVisuals();
            _bodyPositions.Clear();

            foreach (Transform child in snakeContainer)
            {
                if (child.gameObject.activeSelf)
                {
                    Destroy(child.gameObject);
                }
            }

            // CHANGED: Try to find a path for the FULL length immediately
            var startLen = snakeData.StartLength;

            if (grid.TryFindFreeSnakePath(startLen, out var spawnPath))
            {
                // spawnPath[0] is Head, spawnPath[Last] is Tail
                _currentDirection = GridPosition.Right; // Default, will update below

                // Calculate initial direction based on head vs body[1]
                if (spawnPath.Count > 1)
                {
                    var dir = spawnPath[0] - spawnPath[1];

                    if (dir != GridPosition.Zero)
                    {
                        _currentDirection = dir;
                    }
                }

                // Create segments
                for (var i = 0; i < spawnPath.Count; i++)
                {
                    var pos = spawnPath[i];
                    var isHead = i == 0;
                    _bodyPositions.Add(pos);
                    CreateVisualSegment(pos, isHead);
                    grid.SetCellOccupied(pos, SnakeId, isHead);
                }

                _growthPending = 0; // Already fully grown
                Debug.Log($"[{SnakeId}] Spawned fully grown at {HeadPosition}, Length: {startLen}");
            } else
            {
                // Fallback: If no space for full body, spawn just head (old logic)
                Debug.LogWarning($"[{SnakeId}] No space for full spawn. Spawning head only.");
                var startPos = grid.GetRandomEmptyPosition();
                _bodyPositions.Add(startPos);
                _currentDirection = GridPosition.Right;
                CreateVisualSegment(startPos, true);
                grid.SetCellOccupied(startPos, SnakeId, true);
                _growthPending = Mathf.Max(0, startLen - 1);
            }
        }

        // ----------------------------- Loop -----------------------------
        private IEnumerator MoveLoop()
        {
            while (_isAlive)
            {
                if (_cachedWait == null || !Mathf.Approximately(_lastCachedSpeed, _currentSpeed))
                {
                    _lastCachedSpeed = _currentSpeed;
                    var delay = Mathf.Max(0.02f, 1f / _currentSpeed);
                    _cachedWait = new WaitForSeconds(delay);
                }

                yield return _cachedWait;

                // 1. Determine where we WANT to go
                var desiredMove = ComputeDesiredDirection();
                var nextPos = HeadPosition + desiredMove;

                // 2. Check what is at that position
                if (!grid.IsValidPosition(nextPos))
                {
                    Kill("Hit Wall");
                }
                // Check for Food explicitly BEFORE checking IsOccupied.
                // Grid.IsOccupied returns true for Food, so this check must come first.
                else if (food != null && food.IsFoodAt(nextPos))
                {
                    PerformMove(desiredMove); // Safe to move (eat)
                } else if (grid.IsOccupied(nextPos))
                {
                    // It is occupied by a snake (self or other) or an obstacle.
                    // Special Exception: Chasing own tail is valid if we aren't growing
                    var isMyTail = nextPos.Equals(_bodyPositions[_bodyPositions.Count - 1]);

                    if (isMyTail && _growthPending <= 0)
                    {
                        PerformMove(desiredMove);
                    } else
                    {
                        // The intended path is blocked. Try to find a free adjacent cell.
                        var evasionDir = FindEvasionDirection();

                        if (evasionDir != GridPosition.Zero)
                        {
                            Debug.LogWarning($"[{SnakeId}] Dodge triggered! Avoiding collision.");
                            PerformMove(evasionDir);
                        } else
                        {
                            // No escape route found. Death.
                            Kill("Crashed into Snake/Obstacle");
                        }
                    }
                } else
                {
                    // Cell is completely empty
                    PerformMove(desiredMove);
                }
            }
        }

        private GridPosition ComputeDesiredDirection()
        {
            var intendedDir = GridPosition.Zero;

            if (_ai != null)
            {
                try
                {
                    intendedDir = _ai.GetNextMove(this);
                } catch (Exception e)
                {
                    Debug.LogError($"[{SnakeId}] AI Critical Error: {e}");
                }
            }

            // If AI returns Zero (confused) or no AI, keep going straight
            if (intendedDir == GridPosition.Zero)
            {
                intendedDir = _currentDirection;
            }

            // Prevent 180 turn (Suicide backwards) - this is a fundamental movement rule, not obstacle avoidance
            if (IsOpposite(intendedDir, _currentDirection) && Length > 1)
            {
                return _currentDirection;
            }

            return intendedDir;
        }

        private GridPosition ComputeSafeDirection(GridPosition desired)
        {
            if (IsMoveSafe(HeadPosition + desired))
            {
                return desired;
            }

            // Panic Mode: Try other directions
            foreach (var dir in GridPosition.Directions3D)
            {
                if (IsOpposite(dir, _currentDirection) && Length > 1)
                {
                    continue;
                }

                if (IsMoveSafe(HeadPosition + dir))
                {
                    return dir;
                }
            }

            return GridPosition.Zero;
        }

        private bool IsMoveSafe(GridPosition targetPos)
        {
            if (!grid.IsValidPosition(targetPos))
            {
                return false;
            }

            // Grid.IsOccupied returns true for food, which caused the snake to avoid it.
            if (food != null && food.IsFoodAt(targetPos))
            {
                return true;
            }

            if (grid.IsOccupied(targetPos))
            {
                // Safe to move into own tail if not growing (tail will move away)
                // List: Tail is at Count - 1
                var tailPos = _bodyPositions[_bodyPositions.Count - 1];
                var isMyTail = targetPos.Equals(tailPos);

                if (isMyTail && _growthPending <= 0)
                {
                    return true;
                }

                return false;
            }

            return true;
        }

        // ----------------------------- Movement -----------------------------
        private void PerformMove(GridPosition direction)
        {
            if (!_isAlive)
            {
                return;
            }

            var newHeadPos = HeadPosition + direction;
            var oldHeadPos = HeadPosition;
            var tailRemoved = false;
            GridPosition tailPos = default;

            // Logic Update
            if (_growthPending > 0)
            {
                _growthPending--;
            } else
            {
                // List: Tail is at the end
                var tailIndex = _bodyPositions.Count - 1;
                tailPos = _bodyPositions[tailIndex];
                _bodyPositions.RemoveAt(tailIndex); // Remove Tail

                if (!tailPos.Equals(newHeadPos))
                {
                    grid.ClearCell(tailPos);
                }

                tailRemoved = true;
            }

            CheckFood(newHeadPos);

            // List: Head is at 0
            _bodyPositions.Insert(0, newHeadPos);
            _currentDirection = direction;

            // Grid Update
            grid.SetCellOccupied(newHeadPos, SnakeId, true);

            if (_bodyPositions.Count > 1)
            {
                // The old head is now a body part
                grid.SetCellOccupied(oldHeadPos, SnakeId, false);
            }

            // Visual Update
            UpdateVisualsEfficiently(newHeadPos, tailRemoved);
            OnMove?.Invoke(newHeadPos);
        }

        /// <summary>
        /// Scans all directions to find a safe spot to move to immediately.
        /// Used when the primary path is blocked.
        /// </summary>
        private GridPosition FindEvasionDirection()
        {
            foreach (var dir in GridPosition.Directions3D)
            {
                // Don't turn 180 degrees into our own neck
                if (IsOpposite(dir, _currentDirection) && Length > 1)
                {
                    continue;
                }

                var targetPos = HeadPosition + dir;

                // Check 1: Is inside grid?
                if (!grid.IsValidPosition(targetPos))
                {
                    continue;
                }

                // Check 2: Is Food? (Safe)
                if (food != null && food.IsFoodAt(targetPos))
                {
                    return dir;
                }

                // Check 3: Is Empty? (Safe)
                // Use !IsOccupied here. Since we already checked Food above, 
                // IsOccupied = true means it's a snake body or wall.
                if (!grid.IsOccupied(targetPos))
                {
                    return dir;
                }
            }

            return GridPosition.Zero;
        }

        private void UpdateVisualsEfficiently(GridPosition newHeadPos, bool recycleTail)
        {
            // Case A: Growing -> Create new Head visual
            if (!recycleTail)
            {
                if (_bodyTransforms.Count > 0)
                {
                    SetVisualData(_bodyTransforms[0], false); // Old head (0) becomes Body
                }

                CreateVisualSegment(newHeadPos, true); // New Head at 0
            }
            // Case B: Moving -> Recycle Tail to become new Head
            else
            {
                if (_bodyTransforms.Count == 0)
                {
                    return;
                }

                // List: Tail is at Count - 1
                var tailIndex = _bodyTransforms.Count - 1;
                var recycledSegment = _bodyTransforms[tailIndex];
                _bodyTransforms.RemoveAt(tailIndex);

                // Old head (0) becomes Body
                if (_bodyTransforms.Count > 0)
                {
                    SetVisualData(_bodyTransforms[0], false);
                }

                // Setup Recycled Segment as new Head
                recycledSegment.position = grid.GridToWorld(newHeadPos);
                recycledSegment.rotation = Quaternion.identity;
                SetVisualData(recycledSegment, true);

                // Insert at 0 (Head)
                _bodyTransforms.Insert(0, recycledSegment);
            }

            // Rotate Head (Index 0)
            if (_bodyTransforms.Count > 0)
            {
                var headT = _bodyTransforms[0];
                var dirVec = new Vector3(_currentDirection.X, _currentDirection.Y, _currentDirection.Z);

                if (dirVec != Vector3.zero)
                {
                    headT.rotation = Quaternion.LookRotation(dirVec);
                }
            }
        }

        private void CreateVisualSegment(GridPosition pos, bool isHead)
        {
            Transform instance;
            var prefab = isHead ? headPrefab ? headPrefab : bodyPrefab : bodyPrefab;

            if (!isHead && _segmentPool.Count > 0)
            {
                instance = _segmentPool.Pop();
                instance.gameObject.SetActive(true);
                instance.position = grid.GridToWorld(pos);
                instance.rotation = Quaternion.identity;

                if (instance.parent != snakeContainer)
                {
                    instance.SetParent(snakeContainer);
                }
            } else
            {
                if (!prefab)
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube);

                    if (Application.isPlaying)
                    {
                        Destroy(p.GetComponent<Collider>());
                    }

                    prefab = p.transform;
                    p.transform.SetParent(snakeContainer);
                    instance = p.transform;
                } else
                {
                    instance = Instantiate(prefab, grid.GridToWorld(pos), Quaternion.identity, snakeContainer);
                }
            }

            SetVisualData(instance, isHead);

            if (isHead)
            {
                _bodyTransforms.Insert(0, instance);
            } else
            {
                _bodyTransforms.Add(instance);
            }
        }

        private void SetVisualData(Transform t, bool isHead)
        {
            t.name = isHead ? "Head" : "Body";
            t.localScale = Vector3.one * (isHead ? 1.0f : 0.95f);
            var targetMat = isHead ? _runtimeHeadMat : _runtimeBodyMat;

            if (targetMat)
            {
                var r = t.GetComponentInChildren<Renderer>();

                if (r)
                {
                    r.sharedMaterial = targetMat;
                }
            }
        }

        private void CheckFood(GridPosition pos)
        {
            if (food && food.IsFoodAt(pos))
            {
                food.EatFood(pos);
                _growthPending++;
                OnEatFood?.Invoke(pos);
            }
        }

        public void Kill(string reason)
        {
            if (!_isAlive)
            {
                return;
            }

            _isAlive = false;
            Debug.Log($"[{SnakeId}] Died: {reason}");

            if (_moveCoroutine != null)
            {
                StopCoroutine(_moveCoroutine);
            }

            CleanupAndUnsubscribe();
            OnDeath?.Invoke(reason);
            Destroy(gameObject);
        }

        private void ReturnSegmentToPool(Transform t)
        {
            if (!t)
            {
                return;
            }

            t.gameObject.SetActive(false);
            _segmentPool.Push(t);
        }

        private void CleanupVisuals()
        {
            foreach (var t in _bodyTransforms)
            {
                if (t)
                {
                    if (t.name == "Head")
                    {
                        Destroy(t.gameObject);
                    } else
                    {
                        ReturnSegmentToPool(t);
                    }
                }
            }

            _bodyTransforms.Clear();
        }

        private void CleanupAndUnsubscribe()
        {
            if (grid)
            {
                foreach (var pos in _bodyPositions)
                {
                    grid.ClearCell(pos);
                }
            }

            OnMove = null;
            OnEatFood = null;
            OnDeath = null;
        }

        private static bool IsOpposite(GridPosition a, GridPosition b)
        {
            return a.X == -b.X && a.Y == -b.Y && a.Z == -b.Z;
        }

        private void OnDestroy()
        {
            if (Registry.ContainsKey(SnakeId))
            {
                Registry.Remove(SnakeId);
            }

            if (_isAlive)
            {
                CleanupAndUnsubscribe();
            }
        }
    }
}