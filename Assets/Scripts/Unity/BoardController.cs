using System.Collections;
using System.Collections.Generic;
using Chess.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chess.Unity
{
    /// <summary>
    /// Presentation layer for local PvP: draws the board, pieces and highlights, runs the
    /// click-to-select / click-to-move state machine, the promotion picker and a simple HUD.
    /// Drop it on an empty GameObject in the scene and press Play — everything else is built at runtime.
    /// It never decides legality: it asks the engine (Game.GetMovesFrom) and draws the result.
    /// </summary>
    public class BoardController : MonoBehaviour
    {
        enum InputState { Idle, PieceSelected, PromotionPick, Animating }

        [Header("Layout")]
        [SerializeField] float squareSize = 1f;
        [SerializeField] float cameraPadding = 0.9f;
        [SerializeField] bool showCoordinates = true;

        [Header("Colours")]
        [SerializeField] Color lightSquare = new Color(0.93f, 0.85f, 0.71f);
        [SerializeField] Color darkSquare = new Color(0.71f, 0.53f, 0.39f);
        [SerializeField] Color selectedColor = new Color(0.30f, 0.65f, 0.95f, 0.55f);
        [SerializeField] Color lastMoveColor = new Color(0.95f, 0.85f, 0.20f, 0.40f);
        [SerializeField] Color moveDotColor = new Color(0.10f, 0.10f, 0.10f, 0.30f);
        [SerializeField] Color captureColor = new Color(0.85f, 0.15f, 0.15f, 0.60f);
        [SerializeField] Color checkColor = new Color(1.00f, 0.10f, 0.10f, 0.75f);
        [Tooltip("Square tint of the king in check and of the piece(s) giving check.")]
        [SerializeField] Color checkSquareColor = new Color(0.90f, 0.10f, 0.10f, 0.60f);
        [SerializeField] Color background = new Color(0.16f, 0.17f, 0.20f);

        [Header("Pieces")]
        [Tooltip("Optional. Leave empty to use generated placeholders.")]
        [SerializeField] PieceSpriteSet pieceSprites;
        [SerializeField] float pieceScale = 0.85f;
        [SerializeField] float moveDuration = 0.18f;

        [Header("Mode")]
        [Tooltip("Classic = standard chess: classic start position and only \"rule\" abilities (no power-ups). " +
                 "Applies when Play starts. Temporary until the menu exists.")]
        [SerializeField] bool classicMode;

        [Header("Debug")]
        [Tooltip("Optional FEN to start from (empty = the mode's start position).")]
        [SerializeField] string startFen = "";

        // Sorting orders (back → front)
        const int OrderSquare = 0, OrderWall = 1, OrderTint = 2, OrderCheck = 3, OrderPiece = 4, OrderMarker = 7,
                  OrderMoving = 10, OrderOverlay = 20, OrderPicker = 21;

        Game _game;
        Camera _cam;
        Material _spriteMaterial;
        Transform _squaresRoot, _piecesRoot, _highlightRoot, _pickerRoot;

        readonly Dictionary<int, GameObject> _pieceViews = new Dictionary<int, GameObject>();
        readonly List<GameObject> _highlightPool = new List<GameObject>();
        int _highlightsUsed;

        InputState _state = InputState.Idle;
        Square _selected;
        List<Move> _selectedMoves = new List<Move>();
        readonly List<(Square square, Move move)> _pickerOptions = new List<(Square, Move)>();

        void Start()
        {
            // Piece and ability definitions come from StreamingAssets/Data/*.json.
            if (!RulesBootstrap.Load(includePowerUps: !classicMode)) { enabled = false; return; }

            _cam = Camera.main;

            // Unlit sprites, so nothing depends on 2D lights in the scene.
            var unlit = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (unlit != null) _spriteMaterial = new Material(unlit);

            _squaresRoot = NewRoot("Squares");
            _piecesRoot = NewRoot("Pieces");
            _highlightRoot = NewRoot("Highlights");
            _pickerRoot = NewRoot("PromotionPicker");

            NewGame();
        }

        Transform NewRoot(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        void NewGame()
        {
            StopAllCoroutines();
            _game = new Game(!string.IsNullOrWhiteSpace(startFen) ? startFen : classicMode ? Game.ClassicFen : Game.StartFen);
            _state = InputState.Idle;
            _selectedMoves.Clear();
            ClosePicker();

            foreach (Transform c in _squaresRoot) Destroy(c.gameObject);
            SetupCamera();
            BuildSquares();
            RebuildPieces();
            RefreshHighlights();
        }

        // ───────────────────────── Setup ─────────────────────────

        void SetupCamera()
        {
            if (_cam == null) return;
            var b = _game.Board;
            _cam.orthographic = true;
            _cam.transform.position = new Vector3(b.Width * squareSize / 2f, b.Height * squareSize / 2f, -10f);
            float halfH = b.Height * squareSize / 2f + cameraPadding;
            float halfW = b.Width * squareSize / 2f + cameraPadding;
            _cam.orthographicSize = Mathf.Max(halfH, halfW / Mathf.Max(0.01f, _cam.aspect));
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = background;
        }

        void BuildSquares()
        {
            var b = _game.Board;
            foreach (var sq in b.ValidSquares())
            {
                bool light = (sq.File + sq.Rank) % 2 == 1;
                var sr = CreateSprite($"Square {sq}", _squaresRoot, SpriteFactory.Square, light ? lightSquare : darkSquare, OrderSquare);
                sr.transform.position = SquareToWorld(sq);
                sr.transform.localScale = Vector3.one * squareSize;
            }

            if (!showCoordinates) return;
            var labelColor = new Color(1, 1, 1, 0.55f);
            for (int f = 0; f < b.Width; f++)
                CreateLabel(((char)('a' + f)).ToString(), _squaresRoot,
                    new Vector3((f + 0.5f) * squareSize, -0.3f * squareSize, 0), labelColor, 0.045f, OrderSquare + 1);
            for (int r = 0; r < b.Height; r++)
                CreateLabel((r + 1).ToString(), _squaresRoot,
                    new Vector3(-0.3f * squareSize, (r + 0.5f) * squareSize, 0), labelColor, 0.045f, OrderSquare + 1);
        }

        void RebuildPieces()
        {
            foreach (var go in _pieceViews.Values) Destroy(go);
            _pieceViews.Clear();
            foreach (var (sq, piece) in _game.Board.Pieces())
                _pieceViews[piece.Id] = CreatePieceView(piece.Definition, piece.Color, SquareToWorld(sq), _piecesRoot,
                                                        piece.IsWall ? OrderWall : OrderPiece,
                                                        $"{piece} #{piece.Id}", piece.IsBerserk, HasKillMark(piece));
        }

        GameObject CreatePieceView(PieceDefinition def, PieceColor color, Vector3 position, Transform parent, int order, string name,
                                   bool berserk = false, bool killMark = false)
        {
            bool white = color == PieceColor.White;
            var custom = pieceSprites != null ? pieceSprites.Get(def.Symbol, white) : null;

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            if (berserk)
            {
                // Berserk aura: red glow behind the piece.
                var aura = CreateSprite("BerserkAura", root.transform, SpriteFactory.Circle, new Color(0.9f, 0.1f, 0.1f, 0.75f), order - 1);
                aura.transform.localScale = Vector3.one * squareSize * Mathf.Min(0.98f, pieceScale * 1.12f);
            }

            if (custom == null && def.IsWall)
            {
                // Placeholder wall: brick tile; the backing colour shows through as mortar and outline.
                float s = squareSize * 0.92f;
                var backing = CreateSprite("WallBacking", root.transform, SpriteFactory.Square,
                    white ? new Color(0.35f, 0.33f, 0.31f) : new Color(0.85f, 0.83f, 0.78f), order);
                backing.transform.localScale = Vector3.one * s;
                var bricks = CreateSprite("Bricks", root.transform, SpriteFactory.Bricks,
                    white ? new Color(0.90f, 0.86f, 0.78f) : new Color(0.36f, 0.30f, 0.28f), order);
                bricks.transform.localScale = Vector3.one * s;
                bricks.sortingOrder = order; // same layer, drawn after the backing
                bricks.transform.localPosition = new Vector3(0, 0, -0.001f);
            }
            else if (custom != null)
            {
                var sr = CreateSprite("Sprite", root.transform, custom, Color.white, order);
                // Fit any PNG into the square regardless of its pixels-per-unit.
                float size = Mathf.Max(custom.bounds.size.x, custom.bounds.size.y);
                sr.transform.localScale = Vector3.one * (squareSize * pieceScale / Mathf.Max(0.0001f, size));
            }
            else
            {
                // Placeholder: outlined disc + letter.
                var outline = CreateSprite("Outline", root.transform, SpriteFactory.Circle, white ? Color.black : Color.white, order);
                outline.transform.localScale = Vector3.one * squareSize * pieceScale;
                var disc = CreateSprite("Disc", root.transform, SpriteFactory.Circle,
                    white ? new Color(0.97f, 0.97f, 0.95f) : new Color(0.13f, 0.13f, 0.15f), order + 1);
                disc.transform.localScale = Vector3.one * squareSize * pieceScale * 0.88f;
                CreateLabel(def.Symbol + (berserk ? "!" : ""), root.transform, position, white ? Color.black : Color.white, 0.09f, order + 2)
                    .fontStyle = FontStyle.Bold;
            }

            if (killMark)
            {
                // Small blood drop in the top-right corner of the square (child of the piece, so it moves with it).
                var drop = CreateSprite("KillMark", root.transform, SpriteFactory.Drop, new Color(0.78f, 0.05f, 0.08f), order + 3);
                drop.transform.localScale = Vector3.one * squareSize * 0.22f;
                drop.transform.position = position + new Vector3(0.36f, 0.34f, 0) * squareSize;
            }
            return root;
        }

        /// <summary>A pawn (anything that can go berserk) with at least one kill but not berserk yet.</summary>
        static bool HasKillMark(Piece p) =>
            p.Definition.Has(AbilityIds.BerserkPawn) && p.CaptureCount > 0 && !p.IsBerserk;

        TextMesh CreateLabel(string text, Transform parent, Vector3 worldPos, Color color, float charSize, int order)
        {
            var go = new GameObject($"Label {text}");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = squareSize * charSize;
            tm.color = color;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) { tm.font = font; mr.sharedMaterial = font.material; }
            mr.sortingOrder = order;
            return tm;
        }

        SpriteRenderer CreateSprite(string name, Transform parent, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            if (_spriteMaterial != null) sr.sharedMaterial = _spriteMaterial;
            return sr;
        }

        // ───────────────────────── Input ─────────────────────────

        void Update()
        {
            if (_state == InputState.Animating) return; // input locked

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.uKey.wasPressedThisFrame && keyboard.ctrlKey.isPressed) { DoUndo(); return; }

            if ((mouse != null && mouse.rightButton.wasPressedThisFrame) ||
                (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
            {
                Deselect();
                return;
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 screen = mouse.position.ReadValue();
                if (IsOverHud(screen)) return;

                if (_state == InputState.PromotionPick)
                {
                    OnPickerClicked(screen);
                    return;
                }

                if (TryScreenToSquare(screen, out var sq)) OnSquareClicked(sq);
                else Deselect(); // outside the board
            }
        }

        void OnSquareClicked(Square sq)
        {
            if (_game.IsOver) return;
            var piece = _game.Board[sq];
            bool ownPiece = piece != null && piece.Color == _game.SideToMove && !piece.IsWall; // walls never move

            switch (_state)
            {
                case InputState.Idle:
                    if (ownPiece) Select(sq);
                    break;

                case InputState.PieceSelected:
                    if (sq == _selected) { Deselect(); break; }
                    var targets = _selectedMoves.FindAll(m => m.To == sq);
                    if (targets.Count == 1) { PerformMove(targets[0]); break; }
                    if (targets.Count > 1) { OpenPicker(targets); break; } // promotion choices
                    if (ownPiece) { Select(sq); break; } // switch selection directly
                    Deselect();
                    break;
            }
        }

        void Select(Square sq)
        {
            _selected = sq;
            _selectedMoves = _game.GetMovesFrom(sq);
            _state = InputState.PieceSelected;
            RefreshHighlights();
        }

        void Deselect()
        {
            if (_state == InputState.PromotionPick) ClosePicker();
            _selectedMoves.Clear();
            _state = InputState.Idle;
            RefreshHighlights();
        }

        void PerformMove(Move move)
        {
            var record = _game.TryMakeMove(move);
            if (record == null) { Deselect(); return; }
            _selectedMoves.Clear();
            StartCoroutine(AnimateMove(record));
        }

        void DoUndo()
        {
            if (_state == InputState.Animating) return;
            if (_state == InputState.PromotionPick) ClosePicker();
            if (!_game.Undo()) return;
            _selectedMoves.Clear();
            _state = InputState.Idle;
            RebuildPieces();
            RefreshHighlights();
        }

        // ───────────────────────── Promotion picker ─────────────────────────

        void OpenPicker(List<Move> promotions)
        {
            _state = InputState.PromotionPick;
            _pickerOptions.Clear();
            var target = promotions[0].To;
            var color = _game.Board[promotions[0].From].Color;
            int towardCentre = target.Rank >= _game.Board.Height / 2 ? -1 : 1;

            // Dim the board.
            var b = _game.Board;
            var dim = CreateSprite("Dim", _pickerRoot, SpriteFactory.Square, new Color(0, 0, 0, 0.55f), OrderOverlay);
            dim.transform.position = new Vector3(b.Width * squareSize / 2f, b.Height * squareSize / 2f, 0);
            dim.transform.localScale = new Vector3(b.Width * squareSize, b.Height * squareSize, 1);

            // Options in the order of the Promotes ability's "choices" (Q, R, B, N), stacked from the target square toward the centre.
            int i = 0;
            foreach (var symbol in _game.Board[promotions[0].From].Definition.Promotes.Choices)
            {
                var move = promotions.Find(m => m.Promotion == symbol);
                if (move.Promotion == '\0') continue;
                var sq = target.Offset(0, i * towardCentre);
                i++;
                _pickerOptions.Add((sq, move));

                var bg = CreateSprite($"Option {symbol}", _pickerRoot, SpriteFactory.Circle, new Color(0.95f, 0.95f, 0.95f, 0.95f), OrderPicker);
                bg.transform.position = SquareToWorld(sq);
                bg.transform.localScale = Vector3.one * squareSize * 0.98f;
                CreatePieceView(PieceLibrary.BySymbol[symbol], color, SquareToWorld(sq), _pickerRoot, OrderPicker + 1, $"Choice {symbol}");
            }
            RefreshHighlights();
        }

        void OnPickerClicked(Vector2 screen)
        {
            if (TryScreenToSquare(screen, out var sq))
                foreach (var (optSq, move) in _pickerOptions)
                    if (optSq == sq) { ClosePicker(); PerformMove(move); return; }
            Deselect(); // clicked elsewhere → cancel
        }

        void ClosePicker()
        {
            _pickerOptions.Clear();
            if (_pickerRoot == null) return;
            foreach (Transform c in _pickerRoot) Destroy(c.gameObject);
        }

        // ───────────────────────── Animation ─────────────────────────

        IEnumerator AnimateMove(MoveRecord record)
        {
            _state = InputState.Animating;
            RefreshHighlights();

            var m = record.Move;
            var moving = new List<(GameObject view, Vector3 from, Vector3 to)>
            {
                (_pieceViews[record.Mover.Id], SquareToWorld(m.From), SquareToWorld(m.To)),
            };
            // Bounce (target stood on a wall): lunge toward the target, smash the wall, come back.
            bool bounce = record.Bounced;
            if (m.Kind == MoveKind.Castle && record.CastlePartner != null)
            {
                int dir = m.To.File > m.From.File ? 1 : -1;
                moving.Add((_pieceViews[record.CastlePartner.Id], SquareToWorld(m.Aux), SquareToWorld(m.To.Offset(-dir, 0))));
            }

            foreach (var mv in moving) SetSortingOffset(mv.view, OrderMoving - OrderPiece);
            float duration = Mathf.Max(0.0001f, moveDuration) * (bounce ? 2f : 1f);
            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float e = bounce ? 0.65f * Mathf.Sin(t * Mathf.PI) : 1f - (1f - t) * (1f - t); // out-and-back / ease-out
                foreach (var mv in moving) mv.view.transform.position = Vector3.Lerp(mv.from, mv.to, e);
                yield return null;
            }
            foreach (var mv in moving)
            {
                mv.view.transform.position = bounce ? mv.from : mv.to;
                SetSortingOffset(mv.view, OrderPiece - OrderMoving);
            }

            if (record.Captured != null && _pieceViews.TryGetValue(record.Captured.Id, out var capturedView))
            {
                Destroy(capturedView); // works for en passant too (the view is found by piece id)
                _pieceViews.Remove(record.Captured.Id);
            }

            // A capture can change the pawn's look (kill mark → berserk), so rebuild its view.
            bool killChangedLook = record.Captured != null && record.Mover.Definition.Has(AbilityIds.BerserkPawn);
            if (record.Promoted || killChangedLook)
            {
                Destroy(_pieceViews[record.Mover.Id]);
                _pieceViews[record.Mover.Id] = CreatePieceView(record.Mover.Definition, record.Mover.Color,
                    SquareToWorld(record.MoverSquare), _piecesRoot, OrderPiece, $"{record.Mover} #{record.Mover.Id}", record.Mover.IsBerserk, HasKillMark(record.Mover));
            }

            // Another piece upgraded after this move (e.g. knight → paladin when its condition was met off-move): redraw all.
            foreach (var (piece, _) in record.Transformations)
                if (piece != record.Mover) { RebuildPieces(); break; }

            _state = InputState.Idle;
            RefreshHighlights();
        }

        static void SetSortingOffset(GameObject root, int delta)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.sortingOrder += delta;
        }

        // ───────────────────────── Highlights ─────────────────────────

        void RefreshHighlights()
        {
            _highlightsUsed = 0;

            var last = _game.LastMove;
            if (last != null)
            {
                AddHighlight(last.Move.From, SpriteFactory.Square, lastMoveColor, 1f, OrderTint);
                AddHighlight(last.Move.To, SpriteFactory.Square, lastMoveColor, 1f, OrderTint);
            }

            foreach (var royal in _game.RoyalsInCheck(_game.SideToMove))
            {
                AddHighlight(royal, SpriteFactory.Square, checkSquareColor, 1f, OrderCheck);
                AddHighlight(royal, SpriteFactory.Glow, checkColor, 1f, OrderCheck);
            }
            foreach (var checker in _game.CheckingPieces(_game.SideToMove))
                AddHighlight(checker, SpriteFactory.Square, checkSquareColor, 1f, OrderCheck);

            if (_state == InputState.PieceSelected || _state == InputState.PromotionPick)
            {
                AddHighlight(_selected, SpriteFactory.Square, selectedColor, 1f, OrderTint);
                if (_state == InputState.PieceSelected)
                {
                    var drawn = new HashSet<Square>();
                    foreach (var m in _selectedMoves)
                    {
                        if (!drawn.Add(m.To)) continue; // 4 promotion moves share one square
                        bool capture = _game.IsCapture(m); // climbing your own wall is not a capture
                        if (capture)
                        {
                            AddHighlight(m.To, SpriteFactory.Corners, captureColor, 1f, OrderMarker);
                            AddHighlight(m.To, SpriteFactory.Ring, captureColor, 0.98f, OrderTint);
                        }
                        else AddHighlight(m.To, SpriteFactory.Circle, moveDotColor, 0.32f, OrderMarker);
                    }
                }
            }

            for (int i = _highlightsUsed; i < _highlightPool.Count; i++) _highlightPool[i].SetActive(false);
        }

        void AddHighlight(Square sq, Sprite sprite, Color color, float scale, int order)
        {
            GameObject go;
            if (_highlightsUsed < _highlightPool.Count) go = _highlightPool[_highlightsUsed];
            else
            {
                go = CreateSprite("Highlight", _highlightRoot, sprite, color, order).gameObject;
                _highlightPool.Add(go);
            }
            _highlightsUsed++;

            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.color = color; sr.sortingOrder = order;
            go.transform.position = SquareToWorld(sq);
            go.transform.localScale = Vector3.one * squareSize * scale;
            go.SetActive(true);
        }

        // ───────────────────────── HUD (IMGUI — placeholder until a proper UI pass) ─────────────────────────

        const float HudBarHeight = 44f;
        GUIStyle _statusStyle, _bannerStyle;

        bool IsOverHud(Vector2 screen)
        {
            float guiY = Screen.height - screen.y; // IMGUI y goes down
            if (guiY <= HudBarHeight) return true;
            if (DrawPromptVisible && DrawPromptRect.Contains(new Vector2(screen.x, guiY))) return true;
            return false;
        }

        bool DrawPromptVisible => !_game.IsOver && _game.DrawOfferedBy != null && _game.DrawOfferedBy != _game.SideToMove;
        Rect DrawPromptRect => new Rect(Screen.width / 2f - 170, HudBarHeight + 8, 340, 70);

        void OnGUI()
        {
            if (_game == null) return;
            _statusStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _bannerStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            GUI.Box(new Rect(0, 0, Screen.width, HudBarHeight), GUIContent.none);

            string status;
            if (_game.IsOver) status = _game.Status.Describe(_game.Winner);
            else
            {
                status = $"{_game.SideToMove} to move";
                if (_game.IsInCheck(_game.SideToMove)) status += " — CHECK!";
                if (_game.DrawOfferedBy == _game.SideToMove) status += "  (draw offered)";
            }
            GUI.Label(new Rect(12, 0, Screen.width - 460, HudBarHeight), status, _statusStyle);

            bool busy = _state == InputState.Animating;
            GUI.enabled = !busy;
            float x = Screen.width - 450, y = 7, w = 105, h = HudBarHeight - 14;
            if (GUI.Button(new Rect(x, y, w, h), "Undo")) DoUndo();
            GUI.enabled = !busy && !_game.IsOver && _game.DrawOfferedBy == null;
            if (GUI.Button(new Rect(x + 112, y, w, h), "Offer draw")) _game.OfferDraw(_game.SideToMove);
            GUI.enabled = !busy && !_game.IsOver;
            if (GUI.Button(new Rect(x + 224, y, w, h), "Resign")) { _game.Resign(_game.SideToMove); Deselect(); }
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(x + 336, y, w, h), "New game")) NewGame();
            GUI.enabled = true;

            if (DrawPromptVisible)
            {
                var r = DrawPromptRect;
                GUI.Box(r, $"{_game.DrawOfferedBy} offers a draw");
                if (GUI.Button(new Rect(r.x + 20, r.y + 30, 140, 30), "Accept")) { _game.AcceptDraw(); Deselect(); }
                if (GUI.Button(new Rect(r.x + 180, r.y + 30, 140, 30), "Decline")) _game.DeclineDraw();
            }

            if (_game.IsOver)
                GUI.Box(new Rect(Screen.width / 2f - 230, Screen.height / 2f - 40, 460, 80), _game.Status.Describe(_game.Winner), _bannerStyle);
        }

        // ───────────────────────── Coordinates ─────────────────────────

        /// <summary>Centre of a square in world space. Board origin = bottom-left corner at (0,0). White is always at the bottom.</summary>
        public Vector3 SquareToWorld(Square sq) =>
            new Vector3((sq.File + 0.5f) * squareSize, (sq.Rank + 0.5f) * squareSize, 0f);

        bool TryScreenToSquare(Vector2 screen, out Square sq)
        {
            sq = default;
            if (_cam == null) return false;
            Vector3 world = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
            sq = new Square(Mathf.FloorToInt(world.x / squareSize), Mathf.FloorToInt(world.y / squareSize));
            return _game.Board.IsValid(sq);
        }
    }
}
