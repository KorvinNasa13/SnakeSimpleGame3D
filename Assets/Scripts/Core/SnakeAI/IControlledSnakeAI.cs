using SnakeGame.Core;

namespace SnakeGame.AI
{
    public interface IControlledSnakeAI
    {
        GridPosition GetNextMove(SnakeMovementController snake);
    }
}