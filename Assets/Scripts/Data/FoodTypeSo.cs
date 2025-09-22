using UnityEngine;

namespace SnakeGame.Configuration
{
    /// <summary>
    /// Type of food with specific properties and effects.
    /// </summary>
    [CreateAssetMenu(fileName = "NewFoodType", menuName = "Snake Game/Food Type")]
    public class FoodTypeSo : ScriptableObject
    {
        [Header("Basic Properties")]
        [Tooltip("Unique identifier for this food type")]
        [SerializeField] private string foodId = "food_basic";
        
        [Tooltip("Display name for this food type")]
        [SerializeField] private string foodName = "Apple";
        
        [Tooltip("Description of what this food does")]
        [TextArea(2, 4)]
        [SerializeField] private string description = "A regular food item that makes the snake grow.";
        
        [Header("Visual Properties")]
        [Tooltip("Prefab to spawn for this food type")]
        [SerializeField] private GameObject prefab;
        
        [Tooltip("Icon for UI display")]
        [SerializeField] private Sprite icon;
        
        [Tooltip("Color tint for the food")]
        [SerializeField] private Color tintColor = Color.white;
        
        [Tooltip("Should the food rotate?")]
        [SerializeField] private bool rotateAnimation = true;
        
        [Tooltip("Rotation speed in degrees per second")]
        [SerializeField] private float rotationSpeed = 90f;
        
        [Tooltip("Should the food bob up and down?")]
        [SerializeField] private bool bobAnimation = true;
        
        [Tooltip("Bobbing amplitude")]
        [SerializeField] private float bobAmplitude = 0.1f;
        
        [Header("Gameplay Effects")]
        [Tooltip("How many points this food gives")]
        [SerializeField] private int scoreValue = 10;
        
        [Tooltip("How many segments to grow (0 = don't grow)")]
        [Range(0, 5)]
        [SerializeField] private int growthAmount = 1;
        
        [Tooltip("Speed multiplier applied when eaten (1 = no change)")]
        [Range(0.5f, 2f)]
        [SerializeField] private float speedMultiplier = 1f;
        
        [Tooltip("Duration of speed effect in seconds (0 = permanent)")]
        [SerializeField] private float speedEffectDuration = 0f;
        
        [Tooltip("Special effect type")]
        [SerializeField] private FoodEffect specialEffect = FoodEffect.None;
        
        [Header("Spawn Properties")]
        [Tooltip("Relative spawn weight (higher = more common)")]
        [Range(0.1f, 10f)]
        [SerializeField] private float spawnWeight = 1f;
        
        [Tooltip("Can this food spawn naturally?")]
        [SerializeField] private bool canSpawnNaturally = true;
        
        [Tooltip("Minimum time before this food type can spawn again")]
        [SerializeField] private float spawnCooldown = 0f;
        
        [Tooltip("How long this food stays before disappearing (0 = forever)")]
        [SerializeField] private float lifetime = 0f;
        
        [Header("Audio")]
        [Tooltip("Sound played when this food is eaten")]
        [SerializeField] private AudioClip eatSound;
        
        [Tooltip("Sound played when this food spawns")]
        [SerializeField] private AudioClip spawnSound;
        
        [Header("Particle Effects")]
        [Tooltip("Particle effect played when eaten")]
        [SerializeField] private GameObject eatParticleEffect;
        
        [Tooltip("Particle effect played when spawning")]
        [SerializeField] private GameObject spawnParticleEffect;
        
        // Public properties for read-only access
        public string FoodId => foodId;
        public string FoodName => foodName;
        public string Description => description;
        public GameObject Prefab => prefab;
        public Sprite Icon => icon;
        public Color TintColor => tintColor;
        public bool RotateAnimation => rotateAnimation;
        public float RotationSpeed => rotationSpeed;
        public bool BobAnimation => bobAnimation;
        public float BobAmplitude => bobAmplitude;
        public int ScoreValue => scoreValue;
        public int GrowthAmount => growthAmount;
        public float SpeedMultiplier => speedMultiplier;
        public float SpeedEffectDuration => speedEffectDuration;
        public FoodEffect SpecialEffect => specialEffect;
        public float SpawnWeight => spawnWeight;
        public bool CanSpawnNaturally => canSpawnNaturally;
        public float SpawnCooldown => spawnCooldown;
        public float Lifetime => lifetime;
        public AudioClip EatSound => eatSound;
        public AudioClip SpawnSound => spawnSound;
        public GameObject EatParticleEffect => eatParticleEffect;
        public GameObject SpawnParticleEffect => spawnParticleEffect;
        
        /// <summary>
        /// Validates that all required fields are properly set
        /// </summary>
        private void ValidateSettings()
        {
            if (string.IsNullOrEmpty(foodId))
            {
                Debug.LogError($"Food type {name} has no ID!");
            }
            
            if (prefab == null)
            {
                Debug.LogError($"Food type {name} has no prefab assigned!");
            }
            
            if (spawnWeight <= 0)
            {
                Debug.LogError($"Food type {name} has invalid spawn weight!");
            }
        }
        
        private void OnValidate()
        {
            ValidateSettings();
        }
    }
    
    /// <summary>
    /// Special effects that food can apply
    /// </summary>
    public enum FoodEffect
    {
        None,
       // Invincibility,
       // GhostMode,
       // ReverseControls,
       // Shrink,
       // DoublePoints,
       // TimeWarp,
       // Shield,
       // Teleport,
       // FoodExplosion
    }
}