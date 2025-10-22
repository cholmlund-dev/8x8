namespace ChessBoardApp.Models
{
    public class ChessPiece
    {
        public string Type { get; set; }      // "pawn", "rook", "knight", "bishop", "queen", "king"
        public string Color { get; set; }     // "white" eller "black"
        public int Row { get; set; }          // 0–7 (rad)
        public int Col { get; set; }          // 0–7 (kolumn)
        public bool HasMoved { get; set; } = false;
        public bool JustMovedTwoSquares { get; set; } = false;

        // 🧩 Lägg till denna konstruktor så GameState kan skapa pjäser direkt
        public ChessPiece(string type, string color, int row, int col)
        {
            Type = type;
            Color = color;
            Row = row;
            Col = col;
        }

        // 🧩 Tom konstruktor krävs för serialisering (t.ex. JSON)
        public ChessPiece() { }
    }
}
