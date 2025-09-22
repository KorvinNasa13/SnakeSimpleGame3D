using System;
using UnityEngine;

namespace SnakeGame.Data
{
    /// <summary>
    /// Runtime data for a snake that can be modified during gameplay.
    /// This is a mutable copy of SnakeConfigSO data.
    /// </summary>
    [Serializable]
    public class SnakeRuntimeConfig
    {
        private string _snakeId;
        private string _snakeName;
        private int _currentLength;
        private float _currentSpeed;
        private bool _isAlive;
        private int _score;
        
        // Cached from the original ScriptableObject
        private readonly SnakeDataSo _originalSnakeData;
        
        // Events for state changes
        public event Action<int> OnScoreChanged;
        public event Action<float> OnSpeedChanged;
        public event Action OnDeath;
        
        /// <summary>
        /// Constructor that creates a runtime config from a ScriptableObject
        /// </summary>
        /// <param name="config">The original ScriptableObject configuration</param>
        public SnakeRuntimeConfig(SnakeDataSo data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            
            _originalSnakeData = data;
            
            // Copy initial values from ScriptableObject
            _snakeId = data.SnakeID;
            _snakeName = data.SnakeName;
            _currentLength = data.StartLength;
            _currentSpeed = data.BaseSpeed;
            _isAlive = true;
            _score = 0;
        }
        
        // Properties with public getters
        public string SnakeId => _snakeId;
        public string SnakeName => _snakeName;
        public int CurrentLength => _currentLength;
        public float CurrentSpeed => _currentSpeed;
        public bool IsAlive => _isAlive;
        public int Score => _score;
        
        // Reference to original config for accessing unchangeable data
        public SnakeDataSo OriginalData =>  _originalSnakeData;
        
        /// <summary>
        /// Increases the snake's length by one segment
        /// </summary>
        public void Grow()
        {
            if (!_isAlive) return;
            
            if (_currentLength < _originalSnakeData.MaxLength)
            {
                _currentLength++;
                AddScore(5); 
            }
        }
        
        /// <summary>
        /// Adds points to the snake's score
        /// </summary>
        /// <param name="points">Number of points to add</param>
        public void AddScore(int points)
        {
            if (!_isAlive) return;
            
            _score += points;
            OnScoreChanged?.Invoke(_score);
        }
        
        /// <summary>
        /// Modifies the snake's speed by a multiplier
        /// </summary>
        /// <param name="multiplier">Speed multiplier (1.0 = normal speed)</param>
        public void ModifySpeed(float multiplier)
        {
            if (!_isAlive) return;
            
            _currentSpeed = _originalSnakeData.BaseSpeed * multiplier;
            _currentSpeed = Mathf.Clamp(_currentSpeed, 0.1f, 10f); // Clamp to reasonable values
            OnSpeedChanged?.Invoke(_currentSpeed);
        }
        
        /// <summary>
        /// Activates temporary speed boost
        /// </summary>
        /// <param name="duration">How long the boost lasts in seconds</param>
        public void ActivateBoost(float duration)
        {
            if (!_isAlive) return;
            
            ModifySpeed(_originalSnakeData.BoostMultiplier);
            
            // Schedule return to normal speed
            // Note: In real implementation, this would use a coroutine or timer system
        }
        
        /// <summary>
        /// Marks the snake as dead and triggers death event
        /// </summary>
        public void Kill()
        {
            if (!_isAlive) return;
            
            _isAlive = false;
            _currentSpeed = 0;
            OnDeath?.Invoke();
        }
        
        /// <summary>
        /// Resets the runtime config to initial state
        /// </summary>
        public void Reset()
        {
            _snakeId = _originalSnakeData.SnakeID;
            _snakeName = _originalSnakeData.SnakeName;
            _currentLength = _originalSnakeData.StartLength;
            _currentSpeed = _originalSnakeData.BaseSpeed;
            _isAlive = true;
            _score = 0;
        }
        
        /// Changes the snake's name
        public void SetName(string newName)
        {
            if (!string.IsNullOrEmpty(newName))
            {
                _snakeName = newName;
            }
        }
        
        /// Gets a string representation of current stats
        public override string ToString()
        {
            return $"Snake: {_snakeName} | Length: {_currentLength} | Score: {_score} | Alive: {_isAlive}";
        }
    }
}