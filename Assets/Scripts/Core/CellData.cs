using System;
using UnityEngine;

namespace SnakeGame.Core
{
    /// <summary>
    /// Represents the state and data of a single cell in the game grid.
    /// This is a pure data class with no Unity dependencies (except for serialization).
    /// </summary>
    [Serializable]
    public class CellData
    {
        // Private fields with SerializeField for Unity Inspector
        [SerializeField] private GridPosition position;
        [SerializeField] private CellState state;
        [SerializeField] private string occupantId;
        [SerializeField] private float stateChangeTime;
        
        /// <summary>
        /// Position of this cell in the grid
        /// </summary>
        public GridPosition Position => position;
        
        /// <summary>
        /// Current state of the cell
        /// </summary>
        public CellState State => state;
        
        /// <summary>
        /// ID of the entity occupying this cell (snake ID or food ID)
        /// </summary>
        public string OccupantId => occupantId;
        
        /// <summary>
        /// Time when the state last changed (for animations/effects)
        /// </summary>
        public float StateChangeTime => stateChangeTime;
        
        /// <summary>
        /// Quick check if cell is empty
        /// </summary>
        public bool IsEmpty => state == CellState.Empty;
        
        /// <summary>
        /// Quick check if cell can be entered by a snake
        /// </summary>
        public bool IsWalkable => state == CellState.Empty || state == CellState.Food;
        
        /// <summary>
        /// Constructor for creating cell data
        /// </summary>
        public CellData(GridPosition position)
        {
            this.position = position;
            this.state = CellState.Empty;
            this.occupantId = string.Empty;
            this.stateChangeTime = Time.time;
        }
        
        /// <summary>
        /// Constructor with initial state
        /// </summary>
        public CellData(GridPosition position, CellState initialState, string occupantId = null)
        {
            this.position = position;
            this.state = initialState;
            this.occupantId = occupantId ?? string.Empty;
            this.stateChangeTime = Time.time;
        }
        
        /// <summary>
        /// Sets the cell as occupied by a snake segment
        /// </summary>
        /// <param name="snakeId">ID of the snake occupying this cell</param>
        /// <param name="isHead">Whether this is the head segment</param>
        public void SetSnakeOccupied(string snakeId, bool isHead = false)
        {
            if (string.IsNullOrEmpty(snakeId))
                throw new ArgumentNullException(nameof(snakeId));
            
            state = isHead ? CellState.SnakeHead : CellState.SnakeBody;
            occupantId = snakeId;
            stateChangeTime = Time.time;
        }
        
        /// <summary>
        /// Sets the cell as containing food
        /// </summary>
        /// <param name="foodId">ID of the food type in this cell</param>
        public void SetFood(string foodId)
        {
            if (string.IsNullOrEmpty(foodId))
                throw new ArgumentNullException(nameof(foodId));
            
            state = CellState.Food;
            occupantId = foodId;
            stateChangeTime = Time.time;
        }
        
        /// <summary>
        /// Clears the cell, making it empty
        /// </summary>
        public void Clear()
        {
            state = CellState.Empty;
            occupantId = string.Empty;
            stateChangeTime = Time.time;
        }
        
        /// <summary>
        /// Sets the cell as a wall/obstacle
        /// </summary>
        public void SetWall()
        {
            state = CellState.Wall;
            occupantId = string.Empty;
            stateChangeTime = Time.time;
        }
        
        /// <summary>
        /// Sets the cell as a special/power-up cell
        /// </summary>
        /// <param name="powerUpId">ID of the power-up</param>
        public void SetPowerUp(string powerUpId)
        {
            if (string.IsNullOrEmpty(powerUpId))
                throw new ArgumentNullException(nameof(powerUpId));
            
            state = CellState.PowerUp;
            occupantId = powerUpId;
            stateChangeTime = Time.time;
        }
        
        /// <summary>
        /// Updates the cell state
        /// </summary>
        public void UpdateState(CellState newState, string newOccupantId = null)
        {
            // Only update if state actually changes
            if (state != newState || occupantId != newOccupantId)
            {
                state = newState;
                occupantId = newOccupantId ?? string.Empty;
                stateChangeTime = Time.time;
            }
        }
        
        /// <summary>
        /// Checks if this cell is occupied by a specific snake
        /// </summary>
        public bool IsOccupiedBy(string snakeId)
        {
            return !string.IsNullOrEmpty(snakeId) && 
                   occupantId == snakeId && 
                   (state == CellState.SnakeHead || state == CellState.SnakeBody);
        }
        
        /// <summary>
        /// Gets time since the state changed
        /// </summary>
        public float TimeSinceStateChange()
        {
            return Time.time - stateChangeTime;
        }
        
        /// <summary>
        /// Creates a deep copy of this cell data
        /// </summary>
        public CellData Clone()
        {
            return new CellData(position, state, occupantId)
            {
                stateChangeTime = this.stateChangeTime
            };
        }
        
        /// <summary>
        /// String representation for debugging
        /// </summary>
        public override string ToString()
        {
            return $"Cell[{position}]: {state}" + 
                   (string.IsNullOrEmpty(occupantId) ? "" : $" ({occupantId})");
        }
    }
    
    /// <summary>
    /// Cell states
    /// </summary>
    public enum CellState
    {
        Empty = 0,
        Food = 1,
        SnakeHead = 2,
        SnakeBody = 3,
        Wall = 4,
        PowerUp = 5,
        Portal = 6,
        Hazard = 7
    }
}