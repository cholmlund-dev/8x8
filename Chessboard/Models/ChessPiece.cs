namespace ChessBoardApp.Models
{
    public class ChessPiece
    {
        public string Type { get; set; }  // "pawn", "rook", "knight", ...
        public string Color { get; set; } // "white" or "black"
        public int Row { get; set; }
        public int Col { get; set; }

        // --- Avancerade schackfunktioner ---

        // Används för att se om torn eller kung har flyttat (för rokad)
        public bool HasMoved { get; set; } = false;

        // Endast relevant för bonden: om den precis flyttat två steg från start
        public bool JustMovedTwoSquares { get; set; } = false;

        // Om pjäsen fångats via en passant
        public bool CapturedEnPassant { get; set; } = false;
    }
}
