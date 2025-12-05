using System;
using System.Collections.Generic;
using UnityEngine;
using SnakeGame.Data;

namespace SnakeGame.Core
{
    public class GridManager : MonoBehaviour
    {        
        private const int MAX_SPAWN_ATTEMPTS = 50;
        
        private enum CellKind
        {
            Face,
            Edge
        }

        private enum VisualMode
        {
            GizmosOnly,
            Shell6Faces,
            FullVoxels
        }

        [Header("Grid Configuration")]
        [SerializeField]
        [Min(1)]
        private int gridSize = 20;

        [SerializeField]
        [Min(0.01f)]
        private float cellSize = 1f;

        [SerializeField]
        [Min(0f)]
        private float cellGap = 0f;

        [SerializeField]
        private Vector3 gridOrigin = Vector3.zero;

        [Header("Debug & Visualization")]
        [SerializeField]
        private bool buildDebugGridOnStart = true;

        [SerializeField]
        private VisualMode visualMode = VisualMode.GizmosOnly;

        [Tooltip("Parent for all visual cells.")]
        [SerializeField]
        private Transform cellsParent;

        [SerializeField]
        private GameObject debugCellPrefab;

        [Header("Materials")]
        [SerializeField]
        private Material cellMaterial;

        [SerializeField]
        private Material edgeMaterial;

        [Header("Gizmos")]
        [SerializeField]
        private bool drawGizmos = true;

        [SerializeField]
        private Color gizmoColor = new(0, 1, 0, 0.3f);

        public int GridSize => gridSize;
        public float CellSize => cellSize;
        public float TotalCellStep => cellSize + cellGap;

        private CellData[] _cells;
        private bool _isInitialized;

        private void Awake()
        {
            InitializeGridStorage();
        }

        private void Start()
        {
            if (buildDebugGridOnStart)
            {
                BuildDebugGrid();
            }
        }

        private void InitializeGridStorage()
        {
            if (_isInitialized && _cells != null && _cells.Length == gridSize * gridSize * gridSize)
            {
                return;
            }

            var totalCells = gridSize * gridSize * gridSize;
            _cells = new CellData[totalCells];

            // Pre-allocate all cell objects to avoid runtime allocation
            for (var x = 0; x < gridSize; x++)
            for (var y = 0; y < gridSize; y++)
            for (var z = 0; z < gridSize; z++)
            {
                var index = GetFlatIndex(x, y, z);
                _cells[index] = new CellData(new GridPosition(x, y, z));
            }

            _isInitialized = true;
            Debug.Log($"[GridManager] Initialized grid {gridSize}x{gridSize}x{gridSize} ({totalCells} cells).");
        }

        public void SetGameSettings(GameSettingsSo settings)
        {
            if (!settings)
            {
                Debug.LogError("[GridManager] Settings is null");

                return;
            }

            // Only re-init if size changes to save performance
            if (gridSize != settings.GridSize)
            {
                gridSize = Mathf.Max(1, settings.GridSize);
                _cells = null;
                _isInitialized = false;
            }

            cellSize = Mathf.Max(0.01f, settings.CellSize);

            if (!_isInitialized)
            {
                InitializeGridStorage();
            } else
            {
                ClearAllCells();
            }
        }

        public void SetCellsParent(Transform parent)
        {
            cellsParent = parent;
        }

        // ---------- Core Coordinate Logic (O(1)) ----------
        public bool IsValidPosition(in GridPosition pos)
        {
            return pos.X >= 0 && pos.X < gridSize && pos.Y >= 0 && pos.Y < gridSize && pos.Z >= 0 && pos.Z < gridSize;
        }

        private int GetFlatIndex(int x, int y, int z)
        {
            return x + y * gridSize + z * gridSize * gridSize;
        }

        public Vector3 GridToWorld(in GridPosition pos)
        {
            var step = cellSize + cellGap;

            return gridOrigin + new Vector3(pos.X * step, pos.Y * step, pos.Z * step);
        }

        // ---------- Cell State Management ----------
        public bool IsOccupied(in GridPosition pos)
        {
            if (!IsValidPosition(pos))
            {
                return true;
            }

            return !_cells[GetFlatIndex(pos.X, pos.Y, pos.Z)].IsEmpty;
        }

        public bool IsOccupiedByOtherSnake(in GridPosition pos, string snakeId)
        {
            if (!IsValidPosition(pos))
            {
                return false;
            }

            var cell = _cells[GetFlatIndex(pos.X, pos.Y, pos.Z)];

            return (cell.State == CellState.SnakeBody || cell.State == CellState.SnakeHead)
                && cell.OccupantId != snakeId;
        }

        public void SetCellOccupied(in GridPosition pos, string snakeId, bool isHead)
        {
            if (!IsValidPosition(pos))
            {
                return;
            }

            _cells[GetFlatIndex(pos.X, pos.Y, pos.Z)].SetSnakeOccupied(snakeId, isHead);
        }

        public void ClearCell(in GridPosition pos)
        {
            if (!IsValidPosition(pos))
            {
                return;
            }

            _cells[GetFlatIndex(pos.X, pos.Y, pos.Z)].Clear();
        }

        private void ClearAllCells()
        {
            if (_cells == null)
            {
                return;
            }

            for (var i = 0; i < _cells.Length; i++)
            {
                _cells[i].Clear();
            }
        }

        public CellData PeekCell(in GridPosition pos)
        {
            if (!IsValidPosition(pos))
            {
                return null;
            }

            return _cells[GetFlatIndex(pos.X, pos.Y, pos.Z)];
        }
        
        /// <summary>
        /// Tries to find a sequence of empty cells for spawning a snake.
        /// Returns true if found, and outputs the list of positions (Head to Tail).
        /// </summary>
        public bool TryFindFreeSnakePath(int length, out List<GridPosition> path)
        {
            path = new List<GridPosition>();
    
            // Try random attempts to find a valid start + direction
            for (int i = 0; i < MAX_SPAWN_ATTEMPTS; i++)
            {
                // 1. Pick random head
                GridPosition headPos;
                try 
                { 
                    headPos = GetRandomEmptyPosition(); 
                } 
                catch 
                { 
                    return false; // Grid full
                }

                // 2. Pick random direction for the body to extend BEHIND the head
                // Note: If snake moves RIGHT, the body extends LEFT.
                var moveDir = GridPosition.Directions3D[UnityEngine.Random.Range(0, GridPosition.Directions3D.Length)];
                var bodyDir = new GridPosition(-moveDir.X, -moveDir.Y, -moveDir.Z); // Opposite of movement

                path.Clear();
                path.Add(headPos);

                bool pathValid = true;
                var current = headPos;

                // Check if full length fits
                for (int j = 1; j < length; j++)
                {
                    current += bodyDir;
                    
                    if (!IsValidPosition(current) || !PeekCell(current).IsEmpty)
                    {
                        pathValid = false;
                        break;
                    }
                    path.Add(current);
                }

                if (pathValid)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- Optimized Random Selection ----------
        /// <summary>
        /// Finds an empty position efficiently.
        /// Uses random sampling first, then falls back to linear scan if grid is crowded.
        /// </summary>
        public GridPosition GetRandomEmptyPosition(int maxRandomAttempts = 30)
        {
            if (!_isInitialized)
            {
                InitializeGridStorage();
            }

            // 1. Fast Path: Random sampling (O(1))
            for (var i = 0; i < maxRandomAttempts; i++)
            {
                var randIndex = UnityEngine.Random.Range(0, _cells.Length);

                if (_cells[randIndex].IsEmpty)
                {
                    return _cells[randIndex].Position;
                }
            }

            // 2. Slow Path: Linear Scan (O(N))
            // Guaranteed to find a spot if one exists.
            // Start at random offset to avoid biasing towards (0,0,0)
            var startOffset = UnityEngine.Random.Range(0, _cells.Length);

            for (var i = 0; i < _cells.Length; i++)
            {
                var index = (startOffset + i) % _cells.Length;

                if (_cells[index].IsEmpty)
                {
                    return _cells[index].Position;
                }
            }

            throw new InvalidOperationException("[GridManager] Grid is completely full!");
        }

        // ---------- Visuals (Safety Checks Added) ----------
        public void BuildDebugGrid()
        {
            ClearDebugGrid();

            // Safety check: Don't spawn GameObjects for massive grids
            if (visualMode == VisualMode.GizmosOnly || gridSize > 15)
            {
                if (gridSize > 15 && visualMode != VisualMode.GizmosOnly)
                {
                    Debug.LogWarning(
                        "[GridManager] Grid is too large for GameObject visualization. Switching to Gizmos.");
                }

                return;
            }

            // Ensure parent
            if (!cellsParent)
            {
                var holder = GameObject.Find("CellHolder");
                cellsParent = holder ? holder.transform : new GameObject("CellHolder").transform;
            }

            // Resolve prefab
            var prefabToUse = debugCellPrefab;

            if (!prefabToUse)
            {
                prefabToUse = CreateTemporaryCubePrefab();
            }

            var scale = Mathf.Max(0.01f, cellSize * 0.95f); // 0.95 to show small gap visually

            for (var x = 0; x < gridSize; x++)
            for (var y = 0; y < gridSize; y++)
            for (var z = 0; z < gridSize; z++)
            {
                // Skip inner cells if in Shell mode
                if (visualMode == VisualMode.Shell6Faces)
                {
                    var isEdge = x == 0 || x == gridSize - 1 || y == 0 || y == gridSize - 1 || z == 0
                        || z == gridSize - 1;

                    if (!isEdge)
                    {
                        continue;
                    }
                }

                var pos = new GridPosition(x, y, z);
                CreateCellGO(prefabToUse, pos, scale);
            }

            // Cleanup temp prefab
            if (debugCellPrefab == null && prefabToUse != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(prefabToUse);
                } else
                {
                    DestroyImmediate(prefabToUse);
                }
            }
        }

        private GameObject CreateTemporaryCubePrefab()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);

            if (Application.isPlaying)
            {
                Destroy(temp.GetComponent<Collider>());
            } else
            {
                DestroyImmediate(temp.GetComponent<Collider>());
            }

            temp.name = "_TempCell";

            return temp;
        }

        private void CreateCellGO(GameObject prefab, in GridPosition pos, float scale)
        {
            var go = Instantiate(prefab, GridToWorld(pos), Quaternion.identity, cellsParent);
            
#if UNITY_EDITOR
            go.name = $"C_{pos.X}_{pos.Y}_{pos.Z}";
#endif
            
            go.transform.localScale = Vector3.one * scale;

            // Optional: Set distinct material for edges
            var isOuterEdge = (pos.X == 0 || pos.X == gridSize - 1) && (pos.Y == 0 || pos.Y == gridSize - 1)
                && (pos.Z == 0 || pos.Z == gridSize - 1);
            var mr = go.GetComponentInChildren<MeshRenderer>();

            if (mr)
            {
                if (isOuterEdge && edgeMaterial)
                {
                    mr.sharedMaterial = edgeMaterial;
                } else if (cellMaterial)
                {
                    mr.sharedMaterial = cellMaterial;
                }
            }
        }

        public void ClearDebugGrid()
        {
            if (!cellsParent)
            {
                return;
            }

            var childCount = cellsParent.childCount;

            for (var i = childCount - 1; i >= 0; i--)
            {
                var c = cellsParent.GetChild(i);

                if (Application.isPlaying)
                {
                    Destroy(c.gameObject);
                } else
                {
                    DestroyImmediate(c.gameObject);
                }
            }
        }

        // ---------- Debug Gizmos (Efficient) ----------
        private void OnDrawGizmos()
        {
            if (!drawGizmos)
            {
                return;
            }

            Gizmos.color = gizmoColor;
            var totalSize = gridSize * (cellSize + cellGap);
            var center = gridOrigin + Vector3.one * (totalSize * 0.5f - (cellSize + cellGap) * 0.5f);
            Gizmos.DrawWireCube(center, Vector3.one * totalSize);

            if (gridSize <= 10)
            {
                var drawSize = cellSize * 0.9f;

                for (var x = 0; x < gridSize; x++)
                for (var y = 0; y < gridSize; y++)
                for (var z = 0; z < gridSize; z++)
                {
                    var pos = GridToWorld(new GridPosition(x, y, z));
                    Gizmos.DrawWireCube(pos, Vector3.one * drawSize);
                }
            }
        }
    }
}