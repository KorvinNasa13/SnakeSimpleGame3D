
using System.Runtime.CompilerServices;

namespace SnakeGame.Utils
{
    public static class MathUtils
    {
        public static int SignInt(int v)
        {
            return v > 0 ? 1 : v < 0 ? -1 : 0;
        }
    }
}