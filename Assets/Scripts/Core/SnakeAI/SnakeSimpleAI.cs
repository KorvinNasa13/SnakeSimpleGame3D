using System.Collections.Generic;
using UnityEngine;
using SnakeGame.Core;

namespace SnakeGame.AI
{
    /// <summary>
    /// Simple AI that seeks food and avoids obstacles
    /// </summary>
    public class SnakeSimpleAI : MonoBehaviour, IControlledSnakeAI
    {
        [SerializeField] 
        private GridManager gridManager;
        
        [SerializeField] 
        private FoodManager foodManager;
        
        [SerializeField] 
        private SnakeMovementController snake;
        
        private bool _missingDependenciesLogged;
        
        private void Awake()
        {
            if (!snake)
            {
                TryGetComponent(out snake);
            }
        }
        
        public void Configure(GridManager grid, FoodManager food, SnakeMovementController controller)
        {
            gridManager = grid;
            foodManager = food;
            snake = controller ? controller : snake;

            var ready = ValidateDependencies(true);
            enabled = ready;
        }

        private bool ValidateDependencies(bool logErrors)
        {
            if (!gridManager)
            {
                gridManager = FindFirstObjectByType<GridManager>();
            }

            if (!foodManager)
            {
                foodManager = FindFirstObjectByType<FoodManager>();
            }

            if (!gridManager || !foodManager)
            {
                if (logErrors && !_missingDependenciesLogged)
                {
                    Debug.LogError("[SimpleSnakeAI] Missing required components!");
                    _missingDependenciesLogged = true;
                }
                return false;
            }

            if (!snake)
            {
                TryGetComponent(out snake);
            }

            if (!snake)
            {
                if (logErrors && !_missingDependenciesLogged)
                {
                    Debug.LogError("[SimpleSnakeAI] Missing SnakeMovementController reference!");
                    _missingDependenciesLogged = true;
                }
                return false;
            }

            _missingDependenciesLogged = false;
            return true;
        }
        
        /// <summary>
        /// Get next move direction for the snake
        /// </summary>
        public GridPosition GetNextMove(SnakeMovementController controller)
        {
            if (!ValidateDependencies(true))
            {
                enabled = false;
                return GridPosition.Zero;
            }
            
            // Get current head position
            var head = controller.HeadPosition;
            
            // Get all safe directions
            var safeDirections = GetSafeDirections(head, controller);
            
            if (safeDirections.Count == 0)
            {
                Debug.LogWarning($"[SimpleSnakeAI] No safe moves for {controller.SnakeId}!");
                return GridPosition.Zero; // Snake will die
            }
            
            // Find nearest food
            var targetFood = foodManager.FindNearestFood(head);
            
            if (targetFood.HasValue)
            {
                // Move toward food
                return GetBestDirectionToTarget(head, targetFood.Value, safeDirections, controller);
            }
            else
            {
                // No food, pick safest direction (most open space)
                return GetSafestDirection(head, safeDirections, controller);
            }
        }
        
        /// <summary>
        /// Get all directions that won't cause immediate collision
        /// </summary>
        private List<GridPosition> GetSafeDirections(GridPosition head, SnakeMovementController controller)
        {
            var safe = new List<GridPosition>();
            
            // Check all 6 directions (or 4 for 2D)
            var directions = GridPosition.Directions3D;
            
            foreach (var dir in directions)
            {
                var nextPos = head + dir;
                
                if (IsSafePosition(nextPos, controller))
                {
                    safe.Add(dir);
                }
            }
            
            return safe;
        }
        
        /// <summary>
        /// Check if position is safe to move to
        /// </summary>
        private bool IsSafePosition(GridPosition pos, SnakeMovementController controller)
        {
            // Check grid boundaries
            if (!gridManager.IsValidPosition(pos))
                return false;
            
            // Check self collision (ignore tail as it will move)
            var body = controller.Body;
            for (int i = 0; i < body.Count - 1; i++)
            {
                if (body[i].Equals(pos))
                    return false;
            }
            
            // Check other snakes
            if (gridManager.IsOccupiedByOtherSnake(pos, controller.SnakeId))
                return false;
            
            return true;
        }
        
        /// <summary>
        /// Get best direction to move toward target
        /// </summary>
        private GridPosition GetBestDirectionToTarget(GridPosition head, GridPosition target, 
            List<GridPosition> safeDirections, SnakeMovementController controller)
        {
            GridPosition bestDirection = safeDirections[0];
            int bestDistance = int.MaxValue;
            int bestSafety = -1;
            
            foreach (var dir in safeDirections)
            {
                var nextPos = head + dir;
                int distance = nextPos.ManhattanDistance(target);
                int safety = CalculateSafetyScore(nextPos, controller);
                
                // Prioritize distance to food, but consider safety as tiebreaker
                if (distance < bestDistance || (distance == bestDistance && safety > bestSafety))
                {
                    bestDistance = distance;
                    bestSafety = safety;
                    bestDirection = dir;
                }
            }
            
            return bestDirection;
        }
        
        /// <summary>
        /// Get the safest direction when no food is available
        /// </summary>
        private GridPosition GetSafestDirection(GridPosition head, List<GridPosition> safeDirections, 
            SnakeMovementController controller)
        {
            GridPosition bestDirection = safeDirections[0];
            int bestSafety = -1;
            
            foreach (var dir in safeDirections)
            {
                var nextPos = head + dir;
                int safety = CalculateSafetyScore(nextPos, controller);
                
                if (safety > bestSafety)
                {
                    bestSafety = safety;
                    bestDirection = dir;
                }
            }
            
            return bestDirection;
        }
        
        /// <summary>
        /// Calculate how safe a position is (higher = safer)
        /// </summary>
        private int CalculateSafetyScore(GridPosition pos, SnakeMovementController controller)
        {
            int safety = 0;
            
            // Count available moves from this position
            foreach (var dir in GridPosition.Directions3D)
            {
                var nextPos = pos + dir;
                if (IsSafePosition(nextPos, controller))
                {
                    safety++;
                }
            }
            
            // Bonus for center positions (away from walls)
            int centerDistance = Mathf.Abs(pos.X - gridManager.GridSize / 2) +
                                Mathf.Abs(pos.Y - gridManager.GridSize / 2) +
                                Mathf.Abs(pos.Z - gridManager.GridSize / 2);
            
            safety += (gridManager.GridSize * 3 - centerDistance) / 10;
            
            return safety;
        }
        
        /// <summary>
        /// Debug visualization
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !snake || !gridManager)
                return;
            
            // Draw line to target food
            var targetFood = foodManager.FindNearestFood(snake.HeadPosition);
            if (targetFood.HasValue)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(
                    gridManager.GridToWorld(snake.HeadPosition),
                    gridManager.GridToWorld(targetFood.Value)
                );
            }
        }
    }
}