using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ChessBoardApp.Hubs;
using ChessBoardApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChessBoardApp.Controllers
{
    public class ChessController : Controller
    {
        private readonly IHubContext<ChessHub> _hubContext;

        // --- Spelstate i RAM (för utveckling). Byt till DB/Redis senare. ---
        private static List<ChessPiece> _pieces = InitializeBoard();
        private static List<Move> _moves = new List<Move>();
        private static bool _whiteToMove = true;

        public ChessController(IHubContext<ChessHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public IActionResult Index()
        {
            ViewBag.Moves = _moves;
            return View(_pieces);
        }

        // Returnerar hela brädet (JSON) — klienten kan använda detta för att rendera uppdaterat bräde.
        [HttpGet]
        public IActionResult GetBoard()
        {
            return Json(_pieces);
        }

        // Returnerar giltiga drag för en given ruta (notation "e2")
        [HttpGet]
        public IActionResult GetValidMoves(string pos)
        {
            if (string.IsNullOrWhiteSpace(pos) || pos.Length != 2) return Json(new List<string>());

            var (row, col) = ParsePosition(pos);
            var piece = _pieces.FirstOrDefault(p => p.Row == row && p.Col == col);
            if (piece == null) return Json(new List<string>());

            var possibleMoves = new List<string>();

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    if (IsValidMove(piece, row, col, r, c) && !WouldCauseSelfCheck(piece, row, col, r, c))
                    {
                        possibleMoves.Add(PositionToNotation(r, c));
                    }
                }
            }

            return Json(possibleMoves);
        }

        // Gör ett drag — valideras server-side. När giltigt, broadcastas uppdaterat spel till alla klienter via SignalR.
        [HttpPost]
        public async Task<IActionResult> Move(string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || from.Length != 2 || to.Length != 2)
                return BadRequest("Ogiltig position.");

            var (fromRow, fromCol) = ParsePosition(from);
            var (toRow, toCol) = ParsePosition(to);

            var piece = _pieces.FirstOrDefault(p => p.Row == fromRow && p.Col == fromCol);
            if (piece == null) return BadRequest("Ingen pjäs hittades på startpositionen.");

            // Turkontroll
            if ((_whiteToMove && piece.Color != "white") || (!_whiteToMove && piece.Color != "black"))
                return BadRequest("Det är inte din tur.");

            var targetPiece = _pieces.FirstOrDefault(p => p.Row == toRow && p.Col == toCol);
            if (targetPiece != null && targetPiece.Color == piece.Color)
                return BadRequest("Du kan inte ta din egen pjäs.");

            // Giltighetskontroll (inkl. specialdrag)
            if (!IsValidMove(piece, fromRow, fromCol, toRow, toCol))
                return BadRequest("Ogiltigt drag enligt regler.");

            if (WouldCauseSelfCheck(piece, fromRow, fromCol, toRow, toCol))
                return BadRequest("Du kan inte lämna din kung i schack.");

            bool isCapture = targetPiece != null;

            // --- EN PASSANT ---
            if (piece.Type == "pawn" && Math.Abs(toCol - fromCol) == 1 && targetPiece == null)
            {
                var captured = _pieces.FirstOrDefault(p => p.Type == "pawn" && p.Row == fromRow && p.Col == toCol && p.JustMovedTwoSquares);
                if (captured != null)
                {
                    _pieces.Remove(captured);
                    isCapture = true;
                }
            }

            // --- ROKAD ---
            if (piece.Type == "king" && Math.Abs(toCol - fromCol) == 2)
            {
                if (toCol == 6) // kort rokad
                {
                    var rook = _pieces.FirstOrDefault(p => p.Type == "rook" && p.Color == piece.Color && p.Col == 7);
                    if (rook != null)
                    {
                        rook.Col = 5;
                        rook.HasMoved = true;
                    }
                }
                else if (toCol == 2) // lång rokad
                {
                    var rook = _pieces.FirstOrDefault(p => p.Type == "rook" && p.Color == piece.Color && p.Col == 0);
                    if (rook != null)
                    {
                        rook.Col = 3;
                        rook.HasMoved = true;
                    }
                }
            }

            // Utför drag (ta bort target om capture)
            if (isCapture && targetPiece != null) _pieces.Remove(targetPiece);

            piece.Row = toRow;
            piece.Col = toCol;
            piece.HasMoved = true;

            // Reset justMovedTwoSquares för alla bönder, sedan markera den som flyttade 2
            foreach (var p in _pieces.Where(p => p.Type == "pawn")) p.JustMovedTwoSquares = false;
            if (piece.Type == "pawn" && Math.Abs(toRow - fromRow) == 2)
                piece.JustMovedTwoSquares = true;

            // Notation och logg
            string notation = GetNotation(piece, from, to, isCapture);
            _moves.Add(new Move { From = from, To = to, Piece = piece.Type, Notation = notation });

            // Växla tur
            _whiteToMove = !_whiteToMove;

            // Kolla schack/schackmatt (efter att tur växlat -> kolla agent för next player)
            string enemyColor = _whiteToMove ? "white" : "black";
            bool inCheck = IsKingInCheck(enemyColor);
            bool checkmate = inCheck && !HasAnyLegalMove(enemyColor);

            if (checkmate)
                _moves.Add(new Move { Notation = "Schackmatt!" });
            else if (inCheck)
                _moves.Add(new Move { Notation = "Schack!" });

            // Broadcasta det nya spelet (from, to + hela move-listan) till alla klienter via SignalR
            await _hubContext.Clients.All.SendAsync("ReceiveMove", from, to, _moves);

            // Returnera moves som API-respons också (om klient vill använda det)
            return Json(_moves);
        }

        // --- PROMOTION: förvandla en bonde till vald pjäs ---
        [HttpPost]
        public async Task<IActionResult> Promote(string from, string to, string newPiece)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(newPiece))
                return BadRequest("Ogiltig promotion.");

            if (from.Length != 2 || to.Length != 2) return BadRequest("Ogiltig notation.");

            var (fromRow, fromCol) = ParsePosition(from);
            var (toRow, toCol) = ParsePosition(to);

            var pawn = _pieces.FirstOrDefault(p => p.Row == fromRow && p.Col == fromCol && p.Type == "pawn");
            if (pawn == null) return BadRequest("Ingen bonde att promota.");

            // Turkontroll (samma logik som för Move)
            if ((_whiteToMove && pawn.Color != "white") || (!_whiteToMove && pawn.Color != "black"))
                return BadRequest("Det är inte din tur.");

            // Kontrollera att destination är sista raden för rätt färg
            if (!((pawn.Color == "white" && toRow == 0) || (pawn.Color == "black" && toRow == 7)))
                return BadRequest("Bonde kan endast promota på sista raden.");

            // Tillåt endast queen/rook/bishop/knight
            var allowed = new[] { "queen", "rook", "bishop", "knight" };
            if (!allowed.Contains(newPiece.ToLower())) return BadRequest("Ogiltig promotionstyp.");

            // Ta bort eventuell fångad pjäs på mål (promotion kan fånga)
            var targetPiece = _pieces.FirstOrDefault(p => p.Row == toRow && p.Col == toCol);
            bool isCapture = false;
            if (targetPiece != null)
            {
                if (targetPiece.Color == pawn.Color) return BadRequest("Du kan inte ta din egen pjäs.");
                _pieces.Remove(targetPiece);
                isCapture = true;
            }

            // Flytta och byt typ
            pawn.Row = toRow;
            pawn.Col = toCol;
            pawn.Type = newPiece.ToLower();
            pawn.HasMoved = true;

            // Nollställ justMovedTwoSquares för bönder
            foreach (var p in _pieces.Where(p => p.Type == "pawn")) p.JustMovedTwoSquares = false;

            // Notation: t.ex. e7-e8=Q eller exf8=Q (förenklad)
            string shortNew = newPiece.ToLower() switch
            {
                "queen" => "Q",
                "rook" => "R",
                "bishop" => "B",
                "knight" => "N",
                _ => newPiece.ToUpper().Substring(0, 1)
            };
            char file = from[0];
            string notation = isCapture ? $"{file}x{to}={shortNew}" : $"{from}-{to}={shortNew}";

            _moves.Add(new Move { From = from, To = to, Piece = pawn.Type, Notation = notation });

            // Växla tur
            _whiteToMove = !_whiteToMove;

            // Broadcasta promotion/uppdatering via SignalR
            await _hubContext.Clients.All.SendAsync("ReceiveMove", from, to, _moves);

            return Json(_moves);
        }

        // =========== Hjälpmetoder ===========
        private static (int row, int col) ParsePosition(string pos)
        {
            int col = pos[0] - 'a';
            int row = 8 - int.Parse(pos[1].ToString());
            return (row, col);
        }

        private static string PositionToNotation(int row, int col)
        {
            char file = (char)('a' + col);
            int rank = 8 - row;
            return $"{file}{rank}";
        }

        private static string GetNotation(ChessPiece piece, string from, string to, bool isCapture)
        {
            if (piece.Type == "pawn")
            {
                if (isCapture)
                {
                    char file = from[0];
                    return $"{file}x{to}";
                }
                else return to;
            }
            else
            {
                string letter = piece.Type switch
                {
                    "rook" => "R",
                    "knight" => "N",
                    "bishop" => "B",
                    "queen" => "Q",
                    "king" => "K",
                    _ => ""
                };

                if (piece.Type == "king" && Math.Abs(to[0] - from[0]) == 2)
                    return to[0] == 'g' ? "O-O" : "O-O-O";

                return $"{letter}{(isCapture ? "x" : "")}{to}";
            }
        }

        // Kontrollera om kung med färg "color" är i schack
        private static bool IsKingInCheck(string color)
        {
            var king = _pieces.FirstOrDefault(p => p.Type == "king" && p.Color == color);
            if (king == null) return false;

            foreach (var p in _pieces.Where(p => p.Color != color))
            {
                if (IsValidMove(p, p.Row, p.Col, king.Row, king.Col))
                    return true;
            }
            return false;
        }

        // Simulera drag och kolla om det lämnar egen kung i schack
        private static bool WouldCauseSelfCheck(ChessPiece piece, int fromRow, int fromCol, int toRow, int toCol)
        {
            // Kopiera board
            var snapshot = _pieces.Select(p => new ChessPiece
            {
                Type = p.Type,
                Color = p.Color,
                Row = p.Row,
                Col = p.Col,
                HasMoved = p.HasMoved,
                JustMovedTwoSquares = p.JustMovedTwoSquares
            }).ToList();

            // Ta bort eventuell målpjäs
            var target = snapshot.FirstOrDefault(p => p.Row == toRow && p.Col == toCol);
            if (target != null) snapshot.Remove(target);

            // Flytta vald pjäs i kopian
            var moved = snapshot.First(p => p.Row == fromRow && p.Col == fromCol);
            moved.Row = toRow;
            moved.Col = toCol;

            // Hitta kungens position EFTER draget
            var king = moved.Type == "king"
                ? moved
                : snapshot.First(p => p.Type == "king" && p.Color == moved.Color);

            // Kolla om någon fiendepjäs attackerar kungen på snapshot
            foreach (var opp in snapshot.Where(p => p.Color != moved.Color))
            {
                if (IsValidMove(opp, opp.Row, opp.Col, king.Row, king.Col, snapshot))
                    return true;
            }

            return false;
        }

        private static bool HasAnyLegalMove(string color)
        {
            foreach (var p in _pieces.Where(p => p.Color == color))
            {
                for (int r = 0; r < 8; r++)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        if (IsValidMove(p, p.Row, p.Col, r, c) && !WouldCauseSelfCheck(p, p.Row, p.Col, r, c))
                            return true;
                    }
                }
            }
            return false;
        }

        // Overload: default IsValidMove som använder _pieces-listan
        private static bool IsValidMove(ChessPiece piece, int fromRow, int fromCol, int toRow, int toCol)
        {
            return IsValidMove(piece, fromRow, fromCol, toRow, toCol, _pieces);
        }

        // Ny version av IsValidMove som accepterar ett board (för snapshot-checks)
        private static bool IsValidMove(ChessPiece piece, int fromRow, int fromCol, int toRow, int toCol, List<ChessPiece> board)
        {
            if (toRow < 0 || toRow > 7 || toCol < 0 || toCol > 7) return false;
            if (fromRow == toRow && fromCol == toCol) return false;
            var target = board.FirstOrDefault(p => p.Row == toRow && p.Col == toCol);
            if (target != null && target.Color == piece.Color) return false;

            int dr = toRow - fromRow;
            int dc = toCol - fromCol;

            switch (piece.Type)
            {
                case "pawn":
                    int dir = piece.Color == "white" ? -1 : 1;
                    if (dc == 0)
                    {
                        if (dr == dir && target == null) return true;
                        if ((fromRow == 6 && piece.Color == "white" || fromRow == 1 && piece.Color == "black") &&
                            dr == 2 * dir &&
                            !board.Any(p => p.Row == fromRow + dir && p.Col == fromCol) &&
                            target == null)
                            return true;
                        return false;
                    }
                    if (Math.Abs(dc) == 1 && dr == dir)
                    {
                        if (target != null && target.Color != piece.Color) return true;
                        var enPassantTarget = board.FirstOrDefault(p => p.Type == "pawn" && p.Row == fromRow && p.Col == toCol && p.JustMovedTwoSquares);
                        if (enPassantTarget != null) return true;
                        return false;
                    }
                    return false;

                case "rook":
                    if (fromRow != toRow && fromCol != toCol) return false;
                    if (fromRow == toRow)
                        for (int c = Math.Min(fromCol, toCol) + 1; c < Math.Max(fromCol, toCol); c++)
                            if (board.Any(p => p.Row == fromRow && p.Col == c)) return false;
                    if (fromCol == toCol)
                        for (int r = Math.Min(fromRow, toRow) + 1; r < Math.Max(fromRow, toRow); r++)
                            if (board.Any(p => p.Row == r && p.Col == fromCol)) return false;
                    return true;

                case "bishop":
                    if (Math.Abs(dr) != Math.Abs(dc)) return false;
                    int stepR = dr > 0 ? 1 : -1;
                    int stepC = dc > 0 ? 1 : -1;
                    for (int i = 1; i < Math.Abs(dr); i++)
                        if (board.Any(p => p.Row == fromRow + i * stepR && p.Col == fromCol + i * stepC)) return false;
                    return true;

                case "queen":
                    var pseudoR = new ChessPiece { Type = "rook", Color = piece.Color };
                    var pseudoB = new ChessPiece { Type = "bishop", Color = piece.Color };
                    return IsValidMove(pseudoR, fromRow, fromCol, toRow, toCol, board) || IsValidMove(pseudoB, fromRow, fromCol, toRow, toCol, board);

                case "king":
                    if (Math.Abs(dr) <= 1 && Math.Abs(dc) <= 1) return true;
                    if (!piece.HasMoved && dr == 0 && Math.Abs(dc) == 2)
                    {
                        // Kort rokad
                        if (dc == 2)
                        {
                            var rook = board.FirstOrDefault(p => p.Type == "rook" && p.Color == piece.Color && !p.HasMoved && p.Col == 7);
                            if (rook != null && !board.Any(p => (p.Col == 5 || p.Col == 6) && p.Row == fromRow)) return true;
                        }
                        // Lång rokad
                        if (dc == -2)
                        {
                            var rook = board.FirstOrDefault(p => p.Type == "rook" && p.Color == piece.Color && !p.HasMoved && p.Col == 0);
                            if (rook != null && !board.Any(p => (p.Col == 1 || p.Col == 2 || p.Col == 3) && p.Row == fromRow)) return true;
                        }
                    }
                    return false;

                case "knight":
                    return (Math.Abs(dr) == 2 && Math.Abs(dc) == 1) || (Math.Abs(dr) == 1 && Math.Abs(dc) == 2);

                default:
                    return false;
            }
        }

        private static List<ChessPiece> InitializeBoard()
        {
            var pieces = new List<ChessPiece>();

            for (int i = 0; i < 8; i++)
            {
                pieces.Add(new ChessPiece { Type = "pawn", Color = "white", Row = 6, Col = i });
                pieces.Add(new ChessPiece { Type = "pawn", Color = "black", Row = 1, Col = i });
            }

            pieces.AddRange(new[]
            {
                new ChessPiece { Type = "rook", Color = "white", Row = 7, Col = 0 },
                new ChessPiece { Type = "rook", Color = "white", Row = 7, Col = 7 },
                new ChessPiece { Type = "rook", Color = "black", Row = 0, Col = 0 },
                new ChessPiece { Type = "rook", Color = "black", Row = 0, Col = 7 },
                new ChessPiece { Type = "knight", Color = "white", Row = 7, Col = 1 },
                new ChessPiece { Type = "knight", Color = "white", Row = 7, Col = 6 },
                new ChessPiece { Type = "knight", Color = "black", Row = 0, Col = 1 },
                new ChessPiece { Type = "knight", Color = "black", Row = 0, Col = 6 },
                new ChessPiece { Type = "bishop", Color = "white", Row = 7, Col = 2 },
                new ChessPiece { Type = "bishop", Color = "white", Row = 7, Col = 5 },
                new ChessPiece { Type = "bishop", Color = "black", Row = 0, Col = 2 },
                new ChessPiece { Type = "bishop", Color = "black", Row = 0, Col = 5 },
                new ChessPiece { Type = "queen", Color = "white", Row = 7, Col = 3 },
                new ChessPiece { Type = "queen", Color = "black", Row = 0, Col = 3 },
                new ChessPiece { Type = "king", Color = "white", Row = 7, Col = 4 },
                new ChessPiece { Type = "king", Color = "black", Row = 0, Col = 4 }
            });

            return pieces;
        }
    }
}
