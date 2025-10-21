using System.Collections.Generic;

namespace ChessBoardApp.Models
{
    public class GameState
    {
        public List<ChessPiece> Pieces { get; set; } = new();
        public List<Move> Moves { get; set; } = new();

        public GameState()
        {
            Pieces = new List<ChessPiece>();
            Moves = new List<Move>();
        }
    }
}
