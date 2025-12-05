using System;
using UnityEngine;

namespace SnakeGame.Core
{
    [Serializable]
    public struct GridPosition : IEquatable<GridPosition>
    {
        [SerializeField]
        private int x;

        [SerializeField]
        private int y;

        [SerializeField]
        private int z;

        public int X => x;
        public int Y => y;
        public int Z => z;

        /// <summary>
        /// Constructor for creating a grid position
        /// </summary>
        public GridPosition(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        // Static predefined directions for easy access
        public static readonly GridPosition Zero = new(0, 0, 0);
        public static readonly GridPosition Up = new(0, 1, 0);
        public static readonly GridPosition Down = new(0, -1, 0);
        public static readonly GridPosition Left = new(-1, 0, 0);
        public static readonly GridPosition Right = new(1, 0, 0);
        public static readonly GridPosition Forward = new(0, 0, 1);
        public static readonly GridPosition Back = new(0, 0, -1);

        /// <summary>
        /// All possible movement directions in 3D space
        /// </summary>
        public static readonly GridPosition[] Directions3D =
        {
            Up, Down, Left, Right, Forward, Back
        };

        /// <summary>
        /// Addition operator for combining positions/movements
        /// </summary>
        public static GridPosition operator +(GridPosition a, GridPosition b)
        {
            return new GridPosition(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        /// <summary>
        /// Subtraction operator for finding difference between positions
        /// </summary>
        public static GridPosition operator -(GridPosition a, GridPosition b)
        {
            return new GridPosition(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        /// <summary>
        /// Equality operator
        /// </summary>
        public static bool operator ==(GridPosition a, GridPosition b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Inequality operator
        /// </summary>
        public static bool operator !=(GridPosition a, GridPosition b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        /// Calculates Manhattan distance to another position
        /// </summary>
        public int ManhattanDistance(GridPosition other)
        {
            return Math.Abs(x - other.x) + Math.Abs(y - other.y) + Math.Abs(z - other.z);
        }

        /// <summary>
        /// Checks if position is within bounds
        /// </summary>
        public bool IsInBounds(int gridSize)
        {
            return x >= 0 && x < gridSize && y >= 0 && y < gridSize && z >= 0 && z < gridSize;
        }

        /// <summary>
        /// IEquatable implementation for performance (Fast typed comparison)
        /// </summary>
        public bool Equals(GridPosition other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        /// <summary>
        /// Prevents "Boxing" and ensures correct hash distribution.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is GridPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(x, y, z);
        }

        /// <summary>
        /// Useful for debugging logs: returns "(x, y, z)"
        /// </summary>
        public override string ToString()
        {
            return $"({x}, {y}, {z})";
        }
    }
}