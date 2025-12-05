using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // Used only for initial config (rarely)
using UnityEngine;
using SnakeGame.Configuration;
using SnakeGame.Data;

namespace SnakeGame.Core
{
    public class FoodManager : MonoBehaviour
    {
        // Simple class to track active food data
        [Serializable]
        private class ActiveFood
        {
            public FoodTypeSo Type;
            public float SpawnTime;
            public GameObject Visual;
            public GridPosition Position;
        }

        [Header("Debug")]
        [SerializeField]
        private bool debugForceZLayer = true;

        [SerializeField]
        private int debugZLayer = 0;

        [Header("Audio")]
        [SerializeField]
        private AudioSource sfxSource;

        private GridManager _grid;
        private GameSettingsSo _settings;
        private bool _isInitialized;

        // Main storage: Dictionary for O(1) lookups by position
        private readonly Dictionary<GridPosition, ActiveFood> _activeFoods = new();

        // Secondary storage: List for allocation-free iteration (FindNearest)
        private readonly List<ActiveFood> _activeFoodList = new();

        // Object Pool: Maps Prefab -> Stack of inactive instances
        private readonly Dictionary<GameObject, Stack<GameObject>> _visualPool = new();

        // Track cooldowns per food type
        private readonly Dictionary<FoodTypeSo, float> _cooldowns = new();

        private float _nextNaturalSpawnTime;

        private FoodTypeSo[] _cachedSpawnableTypes;

        private void Awake()
        {
            if (!sfxSource)
            {
                // Create a dedicated audio source if none assigned
                sfxSource = gameObject.AddComponent<AudioSource>();
            }
        }

        public void Init(GridManager grid, GameSettingsSo cfg)
        {
            _grid = grid;
            _settings = cfg;
            _isInitialized = _grid && _settings;

            if (_settings && _settings.AvailableFoodTypes != null)
            {
                var list = new List<FoodTypeSo>();

                foreach (var t in _settings.AvailableFoodTypes)
                {
                    if (t && t.CanSpawnNaturally)
                    {
                        list.Add(t);
                    }
                }

                _cachedSpawnableTypes = list.ToArray();
            }
        }

        private void Start()
        {
            if (!_isInitialized)
            {
                Debug.LogError("[FoodManager] Not initialized. Call Init() first.");
                enabled = false;

                return;
            }

            // Spawn initial batch
            var initialCount = Mathf.Max(0, _settings.MaxFoodCount);

            for (var i = 0; i < initialCount; i++)
            {
                ForceSpawnFood();
            }

            ScheduleNextNaturalSpawn();
            StartCoroutine(FoodLoop());
        }

        // ----------------------------- Public API -----------------------------

        public bool IsFoodAt(in GridPosition p)
        {
            return _activeFoods.ContainsKey(p);
        }

        /// <summary>
        /// Optimized nearest neighbor search (No GC allocation).
        /// </summary>
        public GridPosition? FindNearestFood(GridPosition from)
        {
            if (_activeFoodList.Count == 0)
            {
                return null;
            }

            ActiveFood nearest = null;
            var minDst = int.MaxValue;

            // Iterate over List instead of Dictionary to avoid enumerator allocation
            for (var i = 0; i < _activeFoodList.Count; i++)
            {
                var food = _activeFoodList[i];
                var dst = from.ManhattanDistance(food.Position);

                if (dst < minDst)
                {
                    minDst = dst;
                    nearest = food;
                }
            }

            return nearest?.Position;
        }

        public FoodTypeSo EatFood(in GridPosition p)
        {
            if (!_activeFoods.TryGetValue(p, out var food))
            {
                return null;
            }

            DespawnFood(food);

            return food.Type;
        }

        // ----------------------------- Loop -----------------------------

        private IEnumerator FoodLoop()
        {
            var wait = new WaitForSeconds(0.5f); // Check every 0.5s instead of every frame

            while (enabled)
            {
                yield return wait;

                // 1. Despawn old food
                CheckLifetimeDespawn();

                // 2. Spawn new food
                if (Time.time >= _nextNaturalSpawnTime && _activeFoods.Count < _settings.MaxFoodCount)
                {
                    TrySpawnFood();
                    ScheduleNextNaturalSpawn();
                }
            }
        }

        // ----------------------------- Spawning Logic -----------------------------

        private void TrySpawnFood()
        {
            var type = PickRandomFoodType();

            if (!type)
            {
                return;
            }

            var pos = GetRandomFreePosition();

            if (pos.HasValue)
            {
                SpawnFoodInternal(pos.Value, type);
            }
        }

        private void ForceSpawnFood()
        {
            if (_cachedSpawnableTypes == null || _cachedSpawnableTypes.Length == 0)
            {
                return;
            }

            var type = _cachedSpawnableTypes[UnityEngine.Random.Range(0, _cachedSpawnableTypes.Length)];
            var pos = GetRandomFreePosition();

            if (pos.HasValue)
            {
                SpawnFoodInternal(pos.Value, type);
            }
        }

        private GridPosition? GetRandomFreePosition()
        {
            for (var i = 0; i < 20; i++)
            {
                try
                {
                    var p = _grid.GetRandomEmptyPosition();

                    if (debugForceZLayer)
                    {
                        p = new GridPosition(p.X, p.Y, Mathf.Clamp(debugZLayer, 0, _grid.GridSize - 1));
                    }

                    if (!_activeFoods.ContainsKey(p))
                    {
                        return p;
                    }
                } catch (InvalidOperationException)
                {
                    return null;
                }
            }

            return null;
        }

        private void SpawnFoodInternal(GridPosition pos, FoodTypeSo type)
        {
            if (_activeFoods.ContainsKey(pos))
            {
                return;
            }

            // 1. Get Visual from Pool
            var visual = GetPooledVisual(type.Prefab, pos);

            // 2. Create Data
            var activeFood = new ActiveFood
            {
                Type = type,
                SpawnTime = Time.time,
                Visual = visual,
                Position = pos
            };

            // 3. Register
            _activeFoods.Add(pos, activeFood);
            _activeFoodList.Add(activeFood); // Sync list
            _cooldowns[type] = Time.time + type.SpawnCooldown;

            // 4. Update Grid
            // Ideally, we shouldn't use "SetFood" if it changes cell state to blocked.
            // But if your game logic requires the cell to be marked as Food:
            _grid.PeekCell(pos)?.SetFood(type.FoodId);

            // 5. Effects
            if (type.SpawnSound && sfxSource)
            {
                sfxSource.PlayOneShot(type.SpawnSound);
            }

            if (type.SpawnParticleEffect)
            {
                Instantiate(type.SpawnParticleEffect, _grid.GridToWorld(pos), Quaternion.identity);
            }

            OnFoodSpawned?.Invoke(pos, type);
        }

        private void DespawnFood(ActiveFood food)
        {
            // 1. Unregister
            _activeFoods.Remove(food.Position);
            FastRemoveFood(food);

            // 2. Return Visual to Pool
            ReturnPooledVisual(food.Visual, food.Type.Prefab);

            // 3. Update Grid
            _grid.ClearCell(food.Position);

            // 4. Effects
            if (food.Type.EatSound && sfxSource)
            {
                sfxSource.PlayOneShot(food.Type.EatSound);
            }

            if (food.Type.EatParticleEffect)
            {
                Instantiate(food.Type.EatParticleEffect, _grid.GridToWorld(food.Position), Quaternion.identity);
            }

            OnFoodRemoved?.Invoke(food.Position, food.Type);
        }

        private void FastRemoveFood(ActiveFood food)
        {
            var index = _activeFoodList.IndexOf(food);

            if (index < 0)
            {
                return;
            }

            var lastIndex = _activeFoodList.Count - 1;

            if (index != lastIndex)
            {
                _activeFoodList[index] = _activeFoodList[lastIndex];
            }

            _activeFoodList.RemoveAt(lastIndex);
        }

        private void CheckLifetimeDespawn()
        {
            if (_activeFoodList.Count == 0)
            {
                return;
            }

            // Iterate backwards to safely remove
            for (var i = _activeFoodList.Count - 1; i >= 0; i--)
            {
                var food = _activeFoodList[i];

                if (food.Type.Lifetime > 0 && Time.time - food.SpawnTime > food.Type.Lifetime)
                {
                    DespawnFood(food);
                }
            }
        }

        // ----------------------------- Object Pooling -----------------------------
        private GameObject GetPooledVisual(GameObject prefab, GridPosition pos)
        {
            GameObject instance;

            // Ensure pool exists
            if (!_visualPool.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<GameObject>();
                _visualPool[prefab] = stack;
            }

            // Get or Create
            if (stack.Count > 0)
            {
                instance = stack.Pop();
                instance.SetActive(true);
            } else
            {
                instance = Instantiate(prefab, transform);
            }

            // Position it
            instance.transform.position = _grid.GridToWorld(pos);
            instance.transform.rotation = Quaternion.identity;

            // Fix scale (reset potentially modified scale from previous use)
            instance.transform.localScale = Vector3.one * Mathf.Max(0.25f, _grid.CellSize * 0.7f);

            return instance;
        }

        private void ReturnPooledVisual(GameObject instance, GameObject prefabKey)
        {
            if (!instance)
            {
                return;
            }

            if (!prefabKey)
            {
                Destroy(instance);

                return;
            }

            instance.SetActive(false);

            if (!_visualPool.TryGetValue(prefabKey, out var stack))
            {
                stack = new Stack<GameObject>();
                _visualPool[prefabKey] = stack;
            }

            stack.Push(instance);
        }

        // ----------------------------- Utils -----------------------------
        private FoodTypeSo PickRandomFoodType()
        {
            if (_cachedSpawnableTypes == null || _cachedSpawnableTypes.Length == 0)
            {
                return null;
            }

            var totalWeight = 0f;

            for (var i = 0; i < _cachedSpawnableTypes.Length; i++)
            {
                var t = _cachedSpawnableTypes[i];

                if (_cooldowns.TryGetValue(t, out var unlockTime) && Time.time < unlockTime)
                {
                    continue;
                }

                totalWeight += t.SpawnWeight;
            }

            if (totalWeight <= 0)
            {
                return null;
            }

            var randomPoint = UnityEngine.Random.Range(0f, totalWeight);

            for (var i = 0; i < _cachedSpawnableTypes.Length; i++)
            {
                var t = _cachedSpawnableTypes[i];

                if (_cooldowns.TryGetValue(t, out var unlockTime) && Time.time < unlockTime)
                {
                    continue;
                }

                if (randomPoint < t.SpawnWeight)
                {
                    return t;
                }

                randomPoint -= t.SpawnWeight;
            }

            return _cachedSpawnableTypes[_cachedSpawnableTypes.Length - 1];
        }

        private void ScheduleNextNaturalSpawn()
        {
            var delay = UnityEngine.Random.Range(_settings.FoodSpawnIntervalMin, _settings.FoodSpawnIntervalMax);
            _nextNaturalSpawnTime = Time.time + delay;
        }

        // ----------------------------- Events -----------------------------

        public event Action<GridPosition, FoodTypeSo> OnFoodSpawned;
        public event Action<GridPosition, FoodTypeSo> OnFoodRemoved;
    }
}