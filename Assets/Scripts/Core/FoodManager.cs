using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SnakeGame.Configuration;
using SnakeGame.Data;

namespace SnakeGame.Core
{
    public class FoodManager : MonoBehaviour
    {
        [Serializable]
        private class ActiveFood
        {
            public FoodTypeSo Type;
            public float SpawnTime;
            public GameObject Visual;

            public ActiveFood(FoodTypeSo type, float t)
            {
                Type = type;
                SpawnTime = t;
            }
        }
        
        [Header("Debug")] 
        [SerializeField] 
        private bool debugForceZLayer = true;
        [SerializeField] 
        private int debugZLayer = 0; 

        private GridManager _grid;
        private GameSettingsSo settings;
        private bool _ready;

        private readonly Dictionary<GridPosition, ActiveFood> _active = new();
        private readonly Dictionary<FoodTypeSo, float> _cooldownUntil = new();
        private float _nextNaturalSpawn;

        private readonly List<GridPosition> _foodPositionsCache = new();
        private bool _cacheIsDirty = true;
        
        public void Init(GridManager grid, GameSettingsSo cfg)
        {
            _grid = grid;
            settings = cfg;
            _ready = _grid && settings;
        }

        private void Start()
        {
            if (!_ready)
            {
                Debug.LogError("[FoodManager] Not initialised - call Init(grid, settings) before Start()");
                enabled = false;
                return;
            }

            // Initial fill
            var initial = Mathf.Max(0, settings.MaxFoodCount);
            for (var i = 0; i < initial; i++)
                ForceSpawnFood();

            ScheduleNextNaturalSpawn();

            Debug.Log($"[FoodManager] Ready. Max={settings.MaxFoodCount}, " +
                      $"Types={settings.AvailableFoodTypes?.Length ?? 0}, " +
                      $"Intervals=[{settings.FoodSpawnIntervalMin}; {settings.FoodSpawnIntervalMax}]");

            StartCoroutine(FoodLoop());
        }

        /// <summary>
        /// Check if there's food at specific position
        /// </summary>
        public bool IsFoodAt(in GridPosition p)
        {
            return _active.ContainsKey(p);
        }
        
        /// <summary>
        /// Find nearest food to a given position (optimized)
        /// </summary>
        public GridPosition? FindNearestFood(GridPosition from)
        {
            if (_active.Count == 0) return null;

            GridPosition? nearest = null;
            var minDistance = int.MaxValue;

            // Faster than grid scan
            foreach (var kvp in _active)
            {
                var distance = from.ManhattanDistance(kvp.Key);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = kvp.Key;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Consume food at position and return its type
        /// </summary>
        public FoodTypeSo EatFood(in GridPosition p)
        {
            if (!_active.TryGetValue(p, out var food)) return null;
            DespawnFood(p, food.Type);
            return food.Type;
        }
        
        private IEnumerator FoodLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (isActiveAndEnabled)
            {
                TickLifetimeDespawn();
                if (Time.time >= _nextNaturalSpawn && _active.Count < settings.MaxFoodCount)
                {
                    TrySpawnFood();
                    ScheduleNextNaturalSpawn();
                }

                yield return wait;
            }
        }

        // ======================== Cache Management ========================

        private void RefreshCache()
        {
            _foodPositionsCache.Clear();
            _foodPositionsCache.AddRange(_active.Keys);
            _cacheIsDirty = false;
        }

        private void MarkCacheDirty()
        {
            _cacheIsDirty = true;
        }

        // ======================== Spawning ========================

        private void TrySpawnFood()
        {
            var type = PickRandomFoodType();
            if (!type) return;

            var pos = GetRandomFreeCellAvoidingActive();
            SpawnFoodInternal(pos, type);
        }

        private void ForceSpawnFood()
        {
            var pool = settings.AvailableFoodTypes?
                .Where(t => t && t.CanSpawnNaturally).ToArray();
            if (pool == null || pool.Length == 0) return;

            var type = pool[UnityEngine.Random.Range(0, pool.Length)];
            var pos = GetRandomFreeCellAvoidingActive();
            SpawnFoodInternal(pos, type);
        }

        private GridPosition GetRandomFreeCellAvoidingActive(int maxTries = 200)
        {
            for (var i = 0; i < maxTries; i++)
            {
                var p = _grid.GetRandomEmptyPosition();
                if (debugForceZLayer)
                    p = new GridPosition(p.X, p.Y, Mathf.Clamp(debugZLayer, 0, _grid.GridSize - 1));
                if (!_active.ContainsKey(p)) return p;
            }

            // Fallback: ensure we return something not already used
            GridPosition probe;
            do
            {
                probe = _grid.GetRandomEmptyPosition();
                if (debugForceZLayer)
                    probe = new GridPosition(probe.X, probe.Y, Mathf.Clamp(debugZLayer, 0, _grid.GridSize - 1));
            } while (_active.ContainsKey(probe));

            return probe;
        }

        private void SpawnFoodInternal(in GridPosition pos, FoodTypeSo type)
        {
            if (_active.ContainsKey(pos)) return;

            _active.Add(pos, new ActiveFood(type, Time.time));
            MarkCacheDirty();
            _cooldownUntil[type] = Time.time + Mathf.Max(0f, type.SpawnCooldown);
            
            _grid.PeekCell(pos)?.SetFood(type.FoodId);

            // Create visual
            GameObject visual = null;
            if (type.Prefab)
            {
                visual = Instantiate(type.Prefab, _grid.GridToWorld(pos), Quaternion.identity, transform);
            }
            else
            {
                // Fallback debug visual
                visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.transform.SetParent(transform, false);
                visual.transform.position = _grid.GridToWorld(pos);
                visual.transform.localScale = Vector3.one * Mathf.Max(0.25f, _grid.CellSize * 0.5f);
                visual.name = "DEBUG_FoodSphere";
            }

            // Ensure visibility
            if (visual && !visual.GetComponentInChildren<Renderer>())
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "DEBUG_FoodSphereChild";
                s.transform.SetParent(visual.transform, false);
                s.transform.localScale = Vector3.one * Mathf.Max(0.2f, _grid.CellSize * 0.4f);
            }

            _active[pos].Visual = visual;

            // Effects
            if (type.SpawnSound)
                AudioSource.PlayClipAtPoint(type.SpawnSound, _grid.GridToWorld(pos));
            if (type.SpawnParticleEffect)
                Instantiate(type.SpawnParticleEffect, _grid.GridToWorld(pos), Quaternion.identity);

            Debug.Log($"[FoodManager] Spawned '{type.FoodName}' at {pos}");
            OnFoodSpawned?.Invoke(pos, type);
        }

        private void DespawnFood(in GridPosition pos, FoodTypeSo type)
        {
            if (!_active.Remove(pos, out var af)) return;

            MarkCacheDirty(); // Mark cache for refresh

            if (af.Visual) Destroy(af.Visual);
            _grid.ClearCell(pos);

            // Effects
            if (type.EatSound)
                AudioSource.PlayClipAtPoint(type.EatSound, _grid.GridToWorld(pos));
            if (type.EatParticleEffect)
                Instantiate(type.EatParticleEffect, _grid.GridToWorld(pos), Quaternion.identity);

            OnFoodRemoved?.Invoke(pos, type);
            Debug.Log($"[FoodManager] Despawn '{type.FoodName}' at {pos}");
        }

        private void TickLifetimeDespawn()
        {
            if (_active.Count == 0) return;

            var toRemove = ListPool<GridPosition>.Get();
            foreach (var kv in _active)
            {
                var life = kv.Value.Type.Lifetime;
                if (life > 0f && Time.time - kv.Value.SpawnTime >= life)
                    toRemove.Add(kv.Key);
            }

            foreach (var p in toRemove)
                DespawnFood(p, _active[p].Type);
            ListPool<GridPosition>.Release(toRemove);
        }

        private FoodTypeSo PickRandomFoodType()
        {
            if (settings.AvailableFoodTypes == null || settings.AvailableFoodTypes.Length == 0)
                return null;

            var candidates = new List<FoodTypeSo>();
            var total = 0f;

            foreach (var t in settings.AvailableFoodTypes)
            {
                if (!t || !t.CanSpawnNaturally) continue;
                if (_cooldownUntil.TryGetValue(t, out var until) && Time.time < until) continue;

                candidates.Add(t);
                total += Mathf.Max(0.0001f, t.SpawnWeight);
            }

            if (candidates.Count == 0) return null;

            var pick = UnityEngine.Random.Range(0f, total);
            foreach (var t in candidates)
            {
                pick -= Mathf.Max(0.0001f, t.SpawnWeight);
                if (pick <= 0f) return t;
            }

            return candidates[^1];
        }

        private void ScheduleNextNaturalSpawn()
        {
            _nextNaturalSpawn = Time.time + UnityEngine.Random.Range(
                settings.FoodSpawnIntervalMin,
                settings.FoodSpawnIntervalMax);
        }

        // ======================== Events ========================

        public event Action<GridPosition, FoodTypeSo> OnFoodSpawned;
        public event Action<GridPosition, FoodTypeSo> OnFoodRemoved;
    }

    // Simple pooled list to avoid GC allocation
    internal static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new();

        public static List<T> Get()
        {
            return Pool.Count > 0 ? Pool.Pop() : new List<T>();
        }

        public static void Release(List<T> list)
        {
            list.Clear();
            Pool.Push(list);
        }
    }
}