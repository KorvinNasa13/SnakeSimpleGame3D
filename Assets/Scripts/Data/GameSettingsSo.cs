using System;
using SnakeGame.Configuration;
using UnityEngine;

namespace SnakeGame.Data
{
    /// <summary>
    /// Global game settings.
    /// </summary>
    [CreateAssetMenu(fileName = "GameSettings", menuName = "Snake Game/Game Settings")]
    public class GameSettingsSo : ScriptableObject
    {
        [Header("Grid Configuration")]
        [Tooltip("Size of the game grid in each dimension (creates a cube)")]
        [Range(5, 50)]
        [SerializeField]
        private int gridSize = 10;

        [Tooltip("Size of individual cell in world units")] [Range(0.5f, 2f)] [SerializeField]
        private float cellSize = 1f;

        [Tooltip("Should the grid wrap around edges (torus topology)?")] [SerializeField]
        private bool wrapAroundEdges = false;

        [Header("Food Settings")]
        [Tooltip("Maximum number of food items that can exist simultaneously")]
        [Range(1, 20)]
        [SerializeField]
        private int maxFoodCount = 5;

        [Tooltip("Minimum time between food spawns (seconds)")] [Range(0.5f, 10f)] [SerializeField]
        private float foodSpawnIntervalMin = 2f;

        [Tooltip("Maximum time between food spawns (seconds)")] [Range(1f, 20f)] [SerializeField]
        private float foodSpawnIntervalMax = 5f;

        [Tooltip("Different types of food with their properties")] [SerializeField]
        private FoodTypeSo[] availableFoodTypes;
        
        [Tooltip("Should grid lines be visible?")] [SerializeField]
        private bool showGridLines = true;

        [Tooltip("Opacity of grid lines (0 = invisible, 1 = fully visible)")] [Range(0f, 1f)] [SerializeField]
        private float gridLineOpacity = 0.3f;

        [Header("Gameplay Rules")] [Tooltip("Game mode type")] [SerializeField]
        private GameMode gameMode = GameMode.Classic;

        [Tooltip("Time limit for timed modes (seconds, 0 = no limit)")] [SerializeField]
        private float timeLimit = 0f;

        [Tooltip("Score needed to win (0 = no limit)")] [SerializeField]
        private int scoreToWin = 0;

        [Tooltip("Should snakes die when hitting walls?")] [SerializeField]
        private bool wallsKillSnake = true;

        [Tooltip("Should snakes die when hitting other snakes?")] [SerializeField]
        private bool snakeCollisionKills = true;

        [Header("Difficulty Settings")]
        [Tooltip("How much speed increases with each food eaten")]
        [Range(0f, 0.2f)]
        [SerializeField]
        private float speedIncreasePerFood = 0.05f;

        [Tooltip("Maximum speed multiplier")] [Range(1f, 5f)] [SerializeField]
        private float maxSpeedMultiplier = 2f;

        [Header("Multiplayer Settings")]
        [Tooltip("Maximum number of players/snakes in one game")]
        [Range(1, 8)]
        [SerializeField]
        private int maxPlayers = 4;

        [Tooltip("Should dead players respawn?")] [SerializeField]
        private bool allowRespawn = false;

        [Tooltip("Respawn delay in seconds")] [SerializeField]
        private float respawnDelay = 3f;

        [Header("Audio Settings")] [Tooltip("Sound effect for eating food")] [SerializeField]
        private AudioClip eatFoodSound;

        [Tooltip("Sound effect for snake death")] [SerializeField]
        private AudioClip deathSound;

        [Tooltip("Background music for gameplay")] [SerializeField]
        private AudioClip backgroundMusic;

        // Public properties for read-only access
        public int GridSize => gridSize;
        public float CellSize => cellSize;
        public bool WrapAroundEdges => wrapAroundEdges;
        public int MaxFoodCount => maxFoodCount;
        public float FoodSpawnIntervalMin => foodSpawnIntervalMin;
        public float FoodSpawnIntervalMax => foodSpawnIntervalMax;
        public FoodTypeSo[] AvailableFoodTypes => availableFoodTypes;
        public bool ShowGridLines => showGridLines;
        public float GridLineOpacity => gridLineOpacity;
        public GameMode GameMode => gameMode;
        public float TimeLimit => timeLimit;
        public int ScoreToWin => scoreToWin;
        public bool WallsKillSnake => wallsKillSnake;
        public bool SnakeCollisionKills => snakeCollisionKills;
        public float SpeedIncreasePerFood => speedIncreasePerFood;
        public float MaxSpeedMultiplier => maxSpeedMultiplier;
        public int MaxPlayers => maxPlayers;
        public bool AllowRespawn => allowRespawn;
        public float RespawnDelay => respawnDelay;
        public AudioClip EatFoodSound => eatFoodSound;
        public AudioClip DeathSound => deathSound;
        public AudioClip BackgroundMusic => backgroundMusic;

        /// <summary>
        /// Validates settings to ensure they make sense
        /// </summary>
        private void ValidateSettings()
        {
            // Error only if there is NO prefab at all: neither global, nor any per-type prefab
            bool anyPerTypePrefab = availableFoodTypes != null &&
                                    Array.Exists(availableFoodTypes, t => t && t.Prefab);

            if (!anyPerTypePrefab)
            {
                Debug.LogError("Food prefab is not assigned in GameSettings and no FoodType has a prefab either. Assign at least one.");
            }
        }

        /// <summary>
        /// Called when the ScriptableObject is loaded
        /// </summary>
        private void OnValidate()
        {
            ValidateSettings();
        }
    }

    /// <summary>
    /// Game modes
    /// </summary>
    public enum GameMode
    {
        Classic,
        // TimedChallenge,
        // Battle,
        // Survival,
        // Mission
    }
}