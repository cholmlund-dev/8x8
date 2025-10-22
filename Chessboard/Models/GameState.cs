using System;
using System.Collections.Generic;
using System.Linq;

namespace ChessBoardApp.Models
{
    public class GameState
    {
        public List<ChessPiece> Board { get; set; } = new();
        public List<Move> Moves { get; set; } = new();
        private bool whiteToMove = true;

        public GameState()
        {
            InitializeBoard();
        }

        private void InitializeBoard()
        {
            Board.Clear();

            // Lägg till pjäser för båda spelarna
            for (int col = 0; col < 8; col++)
            {
                Board.Add(new ChessPiece("pawn", "white", 6, col));
                Board.Add(new ChessPiece("pawn", "black", 1, col));
            }

            // Vita officerare
            Board.Add(new ChessPiece("rook", "white", 7, 0));
            Board.Add(new ChessPiece("rook", "white", 7, 7));
            Board.Add(new ChessPiece("knight", "white", 7, 1));
            Board.Add(new ChessPiece("knight", "white", 7, 6));
            Board.Add(new ChessPiece("bishop", "white", 7, 2));
            Board.Add(new ChessPiece("bishop", "white", 7, 5));
            Board.Add(new ChessPiece("queen", "white", 7, 3));
            Board.Add(new ChessPiece("king", "white", 7, 4));

            // Svarta officerare
            Board.Add(new ChessPiece("rook", "black", 0, 0));
            Board.Add(new ChessPiece("rook", "black", 0, 7));
            Board.Add(new ChessPiece("knight", "black", 0, 1));
            Board.Add(new ChessPiece("knight", "black", 0, 6));
            Board.Add(new ChessPiece("bishop", "black", 0, 2));
            Board.Add(new ChessPiece("bishop", "black", 0, 5));
            Board.Add(new ChessPiece("queen", "black", 0, 3));
            Board.Add(new ChessPiece("king", "black", 0, 4));
        }

        public void MovePiece(string from, string to)
        {
            var (fromRow, fromCol) = NotationToPosition(from);
            var (toRow, toCol) = NotationToPosition(to);

            var movingPiece = Board.FirstOrDefault(p => p.Row == fromRow && p.Col == fromCol);
            if (movingPiece == null)
                throw new Exception("Ingen pjäs på startpositionen.");

            if ((movingPiece.Color == "white" && !whiteToMove) ||
                (movingPiece.Color == "black" && whiteToMove))
                throw new Exception("Inte din tur.");

            var targetPiece = Board.FirstOrDefault(p => p.Row == toRow && p.Col == toCol);
            if (targetPiece != null && targetPiece.Color == movingPiece.Color)
                throw new Exception("Du kan inte slå din egen pjäs.");

            // Gör draget
            if (targetPiece != null)
                Board.Remove(targetPiece);

            movingPiece.Row = toRow;
            movingPiece.Col = toCol;

            Moves.Add(new Move { Notation = $"{from}-{to}" });

            whiteToMove = !whiteToMove;
        }

        public void PromotePawn(string from, string to, string newType)
        {
            var (fromRow, fromCol) = NotationToPosition(from);
            var (toRow, toCol) = NotationToPosition(to);

            var pawn = Board.FirstOrDefault(p => p.Row == fromRow && p.Col == fromCol && p.Type == "pawn");
            if (pawn == null)
                throw new Exception("Ingen bonde att promota.");

            // Flytta till destination
            pawn.Row = toRow;
            pawn.Col = toCol;
            pawn.Type = newType;

            Moves.Add(new Move { Notation = $"{from}-{to}={newType.ToUpper()[0]}" });

            whiteToMove = !whiteToMove;
        }

        public List<string> GetValidMoves(string pos)
        {
            // Här kan du ha din befintliga logik för giltiga drag
            return new List<string>(); // Placeholder
        }

        private (int row, int col) NotationToPosition(string notation)
        {
            var file = notation[0] - 'a';
            var rank = 8 - int.Parse(notation[1].ToString());
            return (rank, file);
        }
    }
}
