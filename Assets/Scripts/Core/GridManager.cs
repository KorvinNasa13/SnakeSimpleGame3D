using System;
using System.Collections.Generic;
using UnityEngine;
using SnakeGame.Data;

namespace SnakeGame.Core
{
    public class GridManager : MonoBehaviour
    {
        private enum CellKind
        {
            Face,
            Edge
        }

        private enum VisualMode
        {
            Shell6Faces,
            FullVoxels
        }
        
        // ---------- Grid settings ----------
        [Header("Grid (local or from SO)")] 
        [SerializeField] 
        [Min(1)]
        private int gridSize = 20;

        [SerializeField] 
        [Min(0.01f)] 
        private float cellSize = 1f;

        [SerializeField] 
        [Min(0f)] 
        private float cellGap = 0f; // extra spacing between cell centers
        
        [SerializeField] 
        private Vector3 gridOrigin = Vector3.zero;
        
        [Header("Debug / Visual (optional)")] [SerializeField]
        private bool buildDebugGridOnStart = true;

        [SerializeField] private VisualMode visualMode = VisualMode.Shell6Faces;
        [SerializeField] private int visualizeZLayer = 0;

        [Tooltip("Parent for all visual cells (set by GameController).")] [SerializeField]
        private Transform cellsParent;

        [Tooltip("Optional prefab for a single cell (cube). If empty, a primitive Cube will be used.")] [SerializeField]
        private GameObject debugCellPrefab;

        [Header("Materials (shared, no instancing)")]
        [Tooltip("Shared material for face/inner cells (make it semi-transparent in the asset).")]
        [SerializeField]
        private Material cellMaterial;

        [Tooltip("Shared material for only the 12 edges + 8 corners (make it distinct/opaque in the asset).")]
        [SerializeField]
        private Material edgeMaterial;

        [Header("Gizmos")] 
        [SerializeField] 
        private bool drawGizmos = false;
        
        public int GridSize => gridSize;
        public float CellSize => cellSize;
        
        private readonly Dictionary<GridPosition, CellData> _cells = new();
        
        private void Awake()
        {
            _cells.Clear();
        }

        private void Start()
        {
            if (buildDebugGridOnStart)
                BuildDebugGrid();
        }

        public void SetGameSettings(GameSettingsSo settings)
        {
            if (!settings)
            {
                Debug.LogError("GridManager.SetGameSettings: settings is null");
                return;
            }
            
            gridSize = Mathf.Max(1, settings.GridSize);
            cellSize = Mathf.Max(0.01f, settings.CellSize);

            _cells.Clear();
        }

        public void SetCellsParent(Transform parent)
        {
            cellsParent = parent;
        }

        public bool IsValidPosition(in GridPosition pos)
        {
            return pos.IsInBounds(gridSize);
        }

        /// <summary>
        /// Convert grid coords to world coords using (cellSize + cellGap) spacing and origin offset.
        /// </summary>
        public Vector3 GridToWorld(in GridPosition pos)
        {
            var step = cellSize + cellGap;
            return gridOrigin + new Vector3(pos.X * step, pos.Y * step, pos.Z * step);
        }

        public bool IsOccupied(in GridPosition pos)
        {
            return _cells.TryGetValue(pos, out var c) && !c.IsEmpty;
        }

        public bool IsOccupiedByOtherSnake(in GridPosition pos, string snakeId)
        {
            return _cells.TryGetValue(pos, out var cell) &&
                   cell.State is CellState.SnakeBody or CellState.SnakeHead &&
                   cell.OccupantId != snakeId;
        }

        public void SetCellOccupied(in GridPosition pos, string snakeId, bool isHead)
        {
            var cell = GetOrCreateCell(pos);
            cell.SetSnakeOccupied(snakeId, isHead);
        }

        public void ClearCell(in GridPosition pos)
        {
            if (_cells.TryGetValue(pos, out var cell))
                cell.Clear();
        }

        public GridPosition GetRandomEmptyPosition(int maxAttempts = 128)
        {
            for (var i = 0; i < maxAttempts; i++)
            {
                var p = new GridPosition(UnityEngine.Random.Range(0, gridSize),
                    UnityEngine.Random.Range(0, gridSize),
                    UnityEngine.Random.Range(0, gridSize));
                if (!IsOccupied(p)) return p;
            }

            for (var x = 0; x < gridSize; x++)
            for (var y = 0; y < gridSize; y++)
            for (var z = 0; z < gridSize; z++)
            {
                var p = new GridPosition(x, y, z);
                if (!IsOccupied(p)) return p;
            }

            throw new InvalidOperationException("Grid is completely full – cannot find empty position.");
        }

        public CellData PeekCell(in GridPosition pos)
        {
            _cells.TryGetValue(pos, out var cell);
            return cell;
        }

        // ---------- Internal ----------
        private CellData GetOrCreateCell(in GridPosition pos)
        {
            if (!_cells.TryGetValue(pos, out var cell))
            {
                cell = new CellData(pos);
                _cells.Add(pos, cell);
            }

            return cell;
        }

        // ---------- Visualization building ----------
        public void BuildDebugGrid()
        {
            ClearDebugGrid();

            // ensure parent
            if (!cellsParent)
            {
                var holder = GameObject.Find("CellHolder");
                cellsParent = holder ? holder.transform : new GameObject("CellHolder").transform;
            }

            // resolve prefab
            var prefab = debugCellPrefab;
            GameObject temp = null;
            if (!prefab)
            {
                temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = temp.GetComponent<Collider>();
                if (col)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }

                temp.name = "_DebugCellPrefab_TMP";
                prefab = temp;
            }

            // slightly smaller than the cell spacing to avoid z-fighting
            var scale = Mathf.Max(0.01f, cellSize * 0.99f);

            switch (visualMode)
            {
                case VisualMode.Shell6Faces:
                {
                    for (var x = 0; x < gridSize; x++)
                    for (var y = 0; y < gridSize; y++)
                    for (var z = 0; z < gridSize; z++)
                    {
                        // skip inner
                        if (!(x == 0 || x == gridSize - 1 ||
                              y == 0 || y == gridSize - 1 ||
                              z == 0 || z == gridSize - 1))
                            continue;

                        // how many boundaries does this cell touch?
                        var boundaries =
                            (x == 0 || x == gridSize - 1 ? 1 : 0) +
                            (y == 0 || y == gridSize - 1 ? 1 : 0) +
                            (z == 0 || z == gridSize - 1 ? 1 : 0);

                        var kind = boundaries >= 2 ? CellKind.Edge : CellKind.Face;
                        CreateCellGO(prefab, new GridPosition(x, y, z), kind, scale);
                    }

                    break;
                }

                case VisualMode.FullVoxels:
                {
                    for (var x = 0; x < gridSize; x++)
                    for (var y = 0; y < gridSize; y++)
                    for (var z = 0; z < gridSize; z++)
                    {
                        var boundaries =
                            (x == 0 || x == gridSize - 1 ? 1 : 0) +
                            (y == 0 || y == gridSize - 1 ? 1 : 0) +
                            (z == 0 || z == gridSize - 1 ? 1 : 0);

                        var kind = boundaries >= 2 ? CellKind.Edge : CellKind.Face; // inner also treated as Face
                        CreateCellGO(prefab, new GridPosition(x, y, z), kind, scale);
                    }

                    break;
                }
            }

            // cleanup temporary prefab
            if (temp)
            {
                if (Application.isPlaying) Destroy(temp);
                else DestroyImmediate(temp);
            }
        }

        public void ClearDebugGrid()
        {
            if (!cellsParent) return;
            for (var i = cellsParent.childCount - 1; i >= 0; i--)
            {
                var c = cellsParent.GetChild(i);
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
        }

        /// <summary>
        /// Create visual cell and assign a shared material.
        /// </summary>
        private void CreateCellGO(GameObject prefab, in GridPosition pos, CellKind kind, float scale)
        {
            var go = Instantiate(prefab, GridToWorld(pos), Quaternion.identity, cellsParent);
            go.name = $"Cell_{pos.X}_{pos.Y}_{pos.Z}";
            go.transform.localScale = new Vector3(scale, scale, scale);

            var mr = go.GetComponentInChildren<MeshRenderer>();
            if (!mr) return;

            // Use shared materials only (no Instantiate).
            if (kind == CellKind.Edge && edgeMaterial)
                mr.sharedMaterial = edgeMaterial;
            else if (cellMaterial)
                mr.sharedMaterial = cellMaterial;
        }
    }
}