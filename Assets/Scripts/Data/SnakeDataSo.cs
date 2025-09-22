using UnityEngine;

namespace SnakeGame.Data
{
    [CreateAssetMenu(fileName = "SnakeData", menuName = "Snake Game/Snake Data", order = 0)]
    public class SnakeDataSo : ScriptableObject
    {
        [Header("Snake ID")] 
        [SerializeField] 
        private string snakeID = "snake_default";
        
        [SerializeField] 
        private string snakeName = "snake_name";
        
        [Header("Gameplay Params")] 
        [Range(1,5)]
        [SerializeField]
        private int startLength = 3;
        
        [Range(0.1f, 5f)]
        [SerializeField] 
        private float baseSpeed = 1f;
        
        [Range(1f, 3f)]
        [SerializeField] private float boostMultiplier = 1.5f;
        
        [SerializeField]
        private int maxLength = 10;
        
        [SerializeField]
        [Range(0.1f,0.5f)]
        private float speedIncrPerFood = 0.2f;

        [Header("Snake Visual")]
        [SerializeField] 
        private GameObject headPrefab;
        
        [SerializeField]
        private GameObject bodyPrefab;
        
        [Header("AI/Difficulty")]
        [SerializeField]
        private bool useSnakeAI = true;

        public string SnakeID => snakeID;
        public string SnakeName => snakeName;
        public int StartLength => startLength;
        public float BaseSpeed => baseSpeed;
        public int MaxLength => maxLength;
        public float BoostMultiplier => boostMultiplier;
        public float SpeedIncrPerFood => speedIncrPerFood;
        public GameObject HeadPrefab => headPrefab;
        public GameObject BodyPrefab => bodyPrefab;
        public bool UseSnakeAI => useSnakeAI;

        public bool isDataValid()
        {
            if (string.IsNullOrEmpty(SnakeID))
            {
                Debug.LogError("Snake ID is empty");
                return false;
            }
            
            if (headPrefab == null || bodyPrefab == null)
            {
                Debug.LogError($"Snake prefab in {name} is empty");
                return false;
            }
            
            if (startLength > maxLength)
            {
                Debug.LogError($"Snake {name} is too large (max length {maxLength})");
                return false;
            }

            return true;
        }

        public SnakeRuntimeConfig CreateSnakeRuntimConfig()
        {
            return new SnakeRuntimeConfig(this);
        }
    }
}
