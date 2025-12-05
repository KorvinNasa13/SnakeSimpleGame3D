using System.Collections.Generic;
using UnityEngine;
using SnakeGame.Core;

namespace SnakeGame.AI
{
    public class SnakeSimpleAI : MonoBehaviour, IControlledSnakeAI
    {
        [Header("Settings")]
        [SerializeField] private bool lookAheadForTraps = true;
        
        [Header("Debug Visuals")]
        [SerializeField] private bool showDebugLines = true;
        [SerializeField] private Color foodLineColor = Color.red;
        [SerializeField] private Color moveLineColor = Color.green;

        // Cache dependencies
        private GridManager _grid;
        private FoodManager _food;
        private SnakeMovementController _controller;

        // Debug state
        private GridPosition? _lastTargetFood;
        private GridPosition _lastMoveDir;

        public void Configure(GridManager grid, FoodManager food, SnakeMovementController controller)
        {
            _grid = grid;
            _food = food;
            _controller = controller;
        }

        private void Start()
        {
            if (!_controller) TryGetComponent(out _controller);
            if (!_grid) _grid = FindFirstObjectByType<GridManager>();
            if (!_food) _food = FindFirstObjectByType<FoodManager>();
        }

        public GridPosition GetNextMove(SnakeMovementController snake)
        {
            if (!_grid || !snake) return GridPosition.Zero;

            var head = snake.HeadPosition;
            var currentDir = snake.CurrentDirection;

            // 1. Find Food
            var foodTarget = _food ? _food.FindNearestFood(head) : null;
            _lastTargetFood = foodTarget; // Save for Gizmos

            // 2. Evaluate all 6 directions
            var bestDir = currentDir;
            var bestScore = int.MinValue;

            var directions = GridPosition.Directions3D;

            foreach (var dir in directions)
            {
                if (IsOpposite(dir, currentDir)) continue;

                var nextPos = head + dir;
                var score = 0;

                // A. Safety Check (Occupied check handles Food correctly now)
                if (!IsSafe(nextPos, snake))
                {
                    score = -10000;
                }
                else
                {
                    // B. Food Incentive
                    if (foodTarget.HasValue)
                    {
                        var distOld = head.ManhattanDistance(foodTarget.Value);
                        var distNew = nextPos.ManhattanDistance(foodTarget.Value);

                        if (distNew < distOld) score += 50;
                        else score -= 10;
                    }

                    // C. Trap Avoidance
                    if (lookAheadForTraps)
                    {
                        var openExits = CountOpenExits(nextPos, snake);
                        if (openExits == 0) score -= 2000;
                        else score += openExits * 5;
                    }

                    if (dir.Equals(currentDir)) score += 5;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDir = dir;
                }
            }
            
            _lastMoveDir = bestDir; // Save for Gizmos
            return bestDir;
        }

        // --- Helpers ---
        
        private bool IsSafe(GridPosition pos, SnakeMovementController snake)
        {
            if (!_grid.IsValidPosition(pos)) return false;

            if (_grid.IsOccupied(pos))
            {
                // CRITICAL: Food is "Occupied" but Safe
                if (_food != null && _food.IsFoodAt(pos)) return true;
                return false;
            }
            return true;
        }

        private int CountOpenExits(GridPosition fromPos, SnakeMovementController snake)
        {
            var exits = 0;
            foreach (var dir in GridPosition.Directions3D)
            {
                if (IsSafe(fromPos + dir, snake)) exits++;
            }
            return exits;
        }

        private bool IsOpposite(GridPosition a, GridPosition b)
        {
            return a.X == -b.X && a.Y == -b.Y && a.Z == -b.Z;
        }
        
        // --- Debug Visualization ---

        private void OnDrawGizmos()
        {
            if (!showDebugLines || !_controller || !_grid) return;
            if (!Application.isPlaying) return;

            Vector3 headWorld = _grid.GridToWorld(_controller.HeadPosition);

            // 1. Draw Line to Target Food
            if (_lastTargetFood.HasValue)
            {
                Gizmos.color = foodLineColor;
                Vector3 foodWorld = _grid.GridToWorld(_lastTargetFood.Value);
                Gizmos.DrawLine(headWorld, foodWorld);
                Gizmos.DrawWireSphere(foodWorld, _grid.CellSize * 0.3f);
            }

            // 2. Draw Intended Move Direction
            if (_lastMoveDir != GridPosition.Zero)
            {
                Gizmos.color = moveLineColor;
                Vector3 targetWorld = _grid.GridToWorld(_controller.HeadPosition + _lastMoveDir);
                Gizmos.DrawLine(headWorld, targetWorld);
                Gizmos.DrawSphere(targetWorld, _grid.CellSize * 0.2f);
            }
        }
    }
}