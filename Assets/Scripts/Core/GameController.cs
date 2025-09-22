using System.Collections.Generic;
using SnakeGame.AI;
using UnityEngine;
using SnakeGame.Data;

namespace SnakeGame.Core
{
    public class GameController : MonoBehaviour
    {
        [Header("Game Settings")] 
        [SerializeField]
        private GameSettingsSo gameSettings;

        [Header("Managers (Assign in Inspector)")] 
        [SerializeField]
        private GridManager gridManager;

        [SerializeField] 
        private FoodManager foodManager;

        [Header("Snake Configurations")] 
        [SerializeField]
        private List<SnakeDataSo> snakeConfigs = new();

        [Header("Runtime")] 
        [SerializeField] 
        private List<SnakeMovementController> activeSnakes = new();

        [Header("Grid Visual Parent")] 
        [SerializeField]
        private Transform gridCellsParent;

        [SerializeField] 
        private string gridCellsParentName = "CellHolder";
        
        [SerializeField] 
        private bool rebuildGridOnRestart = false;
        
        private int _aliveSnakesCount;

        private void Awake()
        {
            ValidateReferences();
            InitializeManagers();
        }

        private void Start()
        {
            EnsureGridParent();
            RebuildVisualGrid();
            StartGame();
        }

        private void ValidateReferences()
        {
            // Grid
            if (!gridManager)
            {
                gridManager = FindFirstObjectByType<GridManager>();
                if (!gridManager)
                {
                    Debug.LogWarning("[GameController] GridManager not found in scene — creating one.");
                    gridManager = new GameObject("GridManager").AddComponent<GridManager>();
                }
            }

            // Food
            if (!foodManager)
            {
                foodManager = FindFirstObjectByType<FoodManager>();
                if (!foodManager)
                {
                    Debug.LogWarning("[GameController] FoodManager not found in scene — creating one.");
                    foodManager = new GameObject("FoodManager").AddComponent<FoodManager>();
                }
            }

            // Settings
            if (!gameSettings)
            {
                gameSettings = Resources.Load<GameSettingsSo>("GameSettings");
                if (!gameSettings)
                    Debug.LogError("[GameController] GameSettings not assigned and not found in Resources!");
            }

            // Snakes
            if (snakeConfigs.Count == 0)
            {
                var loaded = Resources.LoadAll<SnakeDataSo>("SnakeConfigs");
                if (loaded != null && loaded.Length > 0) snakeConfigs.AddRange(loaded);
                if (snakeConfigs.Count == 0)
                    Debug.LogError("[GameController] No snake configurations found!");
            }
        }

        private void InitializeManagers()
        {
            if (!gameSettings) return;

            if (gridManager) gridManager.SetGameSettings(gameSettings);
            if (foodManager) foodManager.Init(gridManager, gameSettings);
        }

        private void EnsureGridParent()
        {
            if (!gridManager) return;

            if (!gridCellsParent)
            {
                // Find existing parent by name
                var existing = GameObject.Find(string.IsNullOrWhiteSpace(gridCellsParentName)
                    ? "CellHolder"
                    : gridCellsParentName);
                if (existing)
                {
                    gridCellsParent = existing.transform;
                }
                else
                {
                    // Create new parent GameObject
                    var go = new GameObject(string.IsNullOrWhiteSpace(gridCellsParentName)
                        ? "CellHolder"
                        : gridCellsParentName);
                    gridCellsParent = go.transform;
                }
            }

            gridManager.SetCellsParent(gridCellsParent);
        }

        private void RebuildVisualGrid()
        {
            if (!gridManager) return;

            gridManager.ClearDebugGrid();
            gridManager.BuildDebugGrid();
        }

        // ───────────────────────── Game Flow ─────────────────────────
        private void StartGame()
        {
            if (!gameSettings || snakeConfigs.Count == 0)
            {
                Debug.LogError("[GameController] Cannot start game — missing settings or snake configs.");
                return;
            }
            
            SpawnSnakes();
            Debug.Log($"[GameController] Game started with {activeSnakes.Count} snakes.");
        }

        private void SpawnSnakes()
        {
            activeSnakes.Clear();
            _aliveSnakesCount = 0;

            foreach (var cfg in snakeConfigs)
            {
                if (!cfg || !cfg.isDataValid())
                {
                    Debug.LogWarning($"[GameController] Invalid snake config: {(cfg ? cfg.name : "null")}");
                    continue;
                }

                SpawnSingleSnake(cfg);
            }

            _aliveSnakesCount = activeSnakes.Count;
        }

        private void SpawnSingleSnake(SnakeDataSo config)
        {
            try
            {
                var spawnGridPos = gridManager.GetRandomEmptyPosition();
                var worldPos = gridManager.GridToWorld(spawnGridPos);

                var snakeGO = new GameObject($"Snake_{config.SnakeName}");
                snakeGO.transform.SetParent(transform);
                snakeGO.transform.SetPositionAndRotation(worldPos, Quaternion.identity);

                var controller = snakeGO.AddComponent<SnakeMovementController>();
                controller.SetSnakeData(config);

                // IMPORTANT: inject managers before Start()
                controller.Configure(gridManager, foodManager);

                // AI if needed
                IControlledSnakeAI ai = null;
                if (config.UseSnakeAI)
                {
                    var simpleAi = snakeGO.AddComponent<SnakeSimpleAI>();
                    simpleAi.Configure(gridManager, foodManager, controller);
                    ai = simpleAi;
                }

                controller.AssignAI(ai ?? snakeGO.GetComponent<IControlledSnakeAI>());

                // Subscriptions
                controller.OnDeath += OnSnakeDeath;
                controller.OnEatFood += OnSnakeEatFood;

                activeSnakes.Add(controller);
                Debug.Log($"[GameController] Spawned snake: {config.SnakeName} at {spawnGridPos}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameController] Failed to spawn snake {config.SnakeName}: {e.Message}");
            }
        }

        private void OnSnakeDeath(string reason)
        {
            _aliveSnakesCount--;
            Debug.Log($"[GameController] Snake died: {reason}. Alive: {_aliveSnakesCount}");
            if (_aliveSnakesCount <= 0) GameOver();
        }

        private void OnSnakeEatFood(GridPosition position)
        {
            Debug.Log($"[GameController] Food eaten at {position}");
        }

        private void GameOver()
        {
            Debug.Log("[GameController] GAME OVER!");

            SnakeMovementController winner = null;
            var maxLen = 0;
            foreach (var s in activeSnakes)
                if (s && s.IsAlive && s.Body.Count > maxLen)
                {
                    maxLen = s.Body.Count;
                    winner = s;
                }

            if (winner) Debug.Log($"[GameController] Winner: {winner.SnakeId} with length {maxLen}");

            Invoke(nameof(RestartGame), 3f);
        }

        private void RestartGame()
        {
            foreach (var s in activeSnakes)
                if (s)
                    Destroy(s.gameObject);

            activeSnakes.Clear();

            if (rebuildGridOnRestart)
            {
                gridManager.ClearDebugGrid();
                gridManager.BuildDebugGrid();
            }

            StartGame();
        }
    }
}