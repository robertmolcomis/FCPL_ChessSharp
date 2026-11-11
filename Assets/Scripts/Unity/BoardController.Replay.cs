using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Chess.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chess.Unity
{
    /// <summary>
    /// Replay of the last saved match (the move-history file).
    /// ← / → step one move (animated; hold to keep stepping), Home / End jump to the start / end, Esc leaves.
    /// The slider at the bottom jumps straight to any move.
    ///
    /// Every position is worked out once when the replay opens (<see cref="ReplayTimeline"/>), so showing a move
    /// is only moving sprites: dragging the slider as fast as you like never falls behind. The live game is left
    /// alone and comes back when you exit.
    /// </summary>
    public partial class BoardController
    {
        const float ReplayBarHeight = 48f;
        const float RepeatDelay = 0.35f;      // hold an arrow this long before it starts repeating
        const float RepeatInterval = 1f / 14; // then this many moves per second

        ReplayTimeline _replay;               // null = not replaying
        int _replayPly;                       // frame on screen (or the one being animated to)
        float _sliderValue;
        float _repeatAt;
        bool _replayRulesSwitched, _powerUpsBeforeReplay;

        Coroutine _replayTween;
        readonly List<GameObject> _raisedViews = new List<GameObject>(); // drawn on top while sliding

        // What each piece view currently shows, so a frame only redraws pieces whose look changed.
        readonly Dictionary<int, (PieceDefinition def, PieceColor color, bool berserk, bool killMark)> _viewLooks =
            new Dictionary<int, (PieceDefinition, PieceColor, bool, bool)>();

        GUIStyle _counterStyle;

        // ───────────────────────── enter / exit ─────────────────────────

        void EnterReplay()
        {
            if (_replay != null || _state == InputState.Animating) return;

            byte[] movesFile;
            bool classic;
            try
            {
                movesFile = File.ReadAllBytes(SaveFiles.MovesPath);
                classic = SaveFormat.IsClassic(movesFile, SaveFormat.FileKind.Moves);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SaveFormatException)
            {
                ShowNotice($"Couldn't open the replay: {e.Message}");
                return;
            }

            // The match may have been played in the other mode: build it with its own rules.
            bool powerUpsBefore = PieceLibrary.Data.PowerUpsIncluded;
            bool switchRules = classic == powerUpsBefore;
            if (switchRules && !RulesBootstrap.Load(includePowerUps: !classic))
            {
                RulesBootstrap.Load(includePowerUps: powerUpsBefore);
                ShowNotice("Couldn't open the replay: the rules for that match didn't load");
                return;
            }

            ReplayTimeline timeline;
            try
            {
                timeline = ReplayTimeline.FromMovesFile(movesFile);
            }
            catch (SaveFormatException e)
            {
                if (switchRules) RulesBootstrap.Load(includePowerUps: powerUpsBefore);
                ShowNotice($"Couldn't open the replay: {e.Message}");
                return;
            }

            if (_state == InputState.PromotionPick) ClosePicker();
            _selectedMoves.Clear();
            _state = InputState.Idle;

            _replay = timeline;
            _replayRulesSwitched = switchRules;
            _powerUpsBeforeReplay = powerUpsBefore;
            _replayPly = 0;
            _sliderValue = 0;
            ClearPieceViews(); // the live game's pieces; the replay draws its own
            ShowFrame(_replay.Frames[0]);
        }

        /// <summary>Back to the live game (does nothing when not replaying).</summary>
        void ExitReplay()
        {
            if (_replay == null) return;
            StopReplayTween();
            _replay = null;
            if (_replayRulesSwitched) RulesBootstrap.Load(includePowerUps: _powerUpsBeforeReplay);
            _replayRulesSwitched = false;
            RebuildPieces();
            RefreshHighlights();
        }

        // ───────────────────────── input ─────────────────────────

        void UpdateReplay()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame) { ExitReplay(); return; }
            if (keyboard.homeKey.wasPressedThisFrame) { ReplayGoTo(0, animate: false); return; }
            if (keyboard.endKey.wasPressedThisFrame) { ReplayGoTo(_replay.MoveCount, animate: false); return; }

            // A fresh press steps once at normal speed; holding keeps stepping, faster, with short slides.
            int dir = keyboard.leftArrowKey.wasPressedThisFrame ? -1 : keyboard.rightArrowKey.wasPressedThisFrame ? 1 : 0;
            if (dir != 0)
            {
                ReplayGoTo(_replayPly + dir, animate: true, moveDuration);
                _repeatAt = Time.time + RepeatDelay;
                return;
            }
            dir = keyboard.leftArrowKey.isPressed ? -1 : keyboard.rightArrowKey.isPressed ? 1 : 0;
            if (dir != 0 && Time.time >= _repeatAt)
            {
                ReplayGoTo(_replayPly + dir, animate: true, Mathf.Min(moveDuration, RepeatInterval * 0.9f));
                _repeatAt = Time.time + RepeatInterval;
            }
        }

        /// <summary>
        /// Shows move <paramref name="target"/>. One step with <paramref name="animate"/> slides the pieces; anything else
        /// (a jump, the slider) snaps at once. A slide still running is finished instantly first, so input never queues up.
        /// </summary>
        void ReplayGoTo(int target, bool animate, float duration = 0f)
        {
            target = Mathf.Clamp(target, 0, _replay.MoveCount);
            if (_replayTween != null)
            {
                StopReplayTween();
                ShowFrame(_replay.Frames[_replayPly]);
            }
            if (target == _replayPly) return;

            var from = _replay.Frames[_replayPly];
            var to = _replay.Frames[target];
            _replayPly = target;
            _sliderValue = target;

            if (animate && duration > 0f && Mathf.Abs(to.Ply - from.Ply) == 1)
                _replayTween = StartCoroutine(SlideToFrame(from, to, duration));
            else
                ShowFrame(to);
        }

        // ───────────────────────── drawing ─────────────────────────

        /// <summary>Puts every piece where the frame has it: moves existing views, redraws changed ones, removes the rest.</summary>
        void ShowFrame(ReplayFrame frame)
        {
            var present = new HashSet<int>();
            foreach (var p in frame.Pieces)
            {
                present.Add(p.Id);
                bool killMark = p.Definition.Has(AbilityIds.BerserkPawn) && p.CaptureCount > 0 && !p.IsBerserk;
                var look = (p.Definition, p.Color, p.IsBerserk, killMark);
                var position = SquareToWorld(p.Square);

                _pieceViews.TryGetValue(p.Id, out var view);
                if (view != null && _viewLooks.TryGetValue(p.Id, out var shown) && shown.Equals(look))
                {
                    view.transform.position = position;
                    continue;
                }
                if (view != null) Destroy(view); // promoted, went berserk, got a kill mark...
                _pieceViews[p.Id] = CreatePieceView(p.Definition, p.Color, position, _piecesRoot, p.IsWall ? OrderWall : OrderPiece,
                                                    $"{p.Color} {p.Definition.DisplayName} #{p.Id}", p.IsBerserk, killMark);
                _viewLooks[p.Id] = look;
            }

            var gone = new List<int>();
            foreach (var id in _pieceViews.Keys) if (!present.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                Destroy(_pieceViews[id]);
                _pieceViews.Remove(id);
                _viewLooks.Remove(id);
            }

            RefreshReplayHighlights(frame);
        }

        /// <summary>Slides the pieces that change square between two neighbouring frames, then shows the new frame exactly.</summary>
        IEnumerator SlideToFrame(ReplayFrame from, ReplayFrame to, float duration)
        {
            RefreshReplayHighlights(to);

            var target = new Dictionary<int, Square>();
            foreach (var p in to.Pieces) target[p.Id] = p.Square;

            // Works both ways and for every kind of move: castling moves two pieces, a capture's victim simply
            // disappears at the end, going backwards brings it back.
            var moving = new List<(GameObject view, Vector3 from, Vector3 to)>();
            foreach (var p in from.Pieces)
                if (target.TryGetValue(p.Id, out var sq) && sq != p.Square && _pieceViews.TryGetValue(p.Id, out var view))
                    moving.Add((view, SquareToWorld(p.Square), SquareToWorld(sq)));

            foreach (var m in moving)
            {
                SetSortingOffset(m.view, OrderMoving - OrderPiece);
                _raisedViews.Add(m.view);
            }

            for (float t = 0; t < 1f; t += Time.deltaTime / duration)
            {
                float e = 1f - (1f - t) * (1f - t); // ease-out
                foreach (var m in moving) m.view.transform.position = Vector3.Lerp(m.from, m.to, e);
                yield return null;
            }

            LowerRaisedViews();
            _replayTween = null;
            ShowFrame(to);
        }

        void StopReplayTween()
        {
            if (_replayTween != null) StopCoroutine(_replayTween);
            _replayTween = null;
            LowerRaisedViews();
        }

        void LowerRaisedViews()
        {
            foreach (var view in _raisedViews)
                if (view != null) SetSortingOffset(view, OrderPiece - OrderMoving);
            _raisedViews.Clear();
        }

        void RefreshReplayHighlights(ReplayFrame frame)
        {
            _highlightsUsed = 0;
            if (frame.LastMove is Move m)
            {
                AddHighlight(m.From, SpriteFactory.Square, lastMoveColor, 1f, OrderTint);
                AddHighlight(m.To, SpriteFactory.Square, lastMoveColor, 1f, OrderTint);
            }
            foreach (var royal in frame.CheckedRoyals)
            {
                AddHighlight(royal, SpriteFactory.Square, checkSquareColor, 1f, OrderCheck);
                AddHighlight(royal, SpriteFactory.Glow, checkColor, 1f, OrderCheck);
            }
            foreach (var checker in frame.Checkers)
                AddHighlight(checker, SpriteFactory.Square, checkSquareColor, 1f, OrderCheck);
            for (int i = _highlightsUsed; i < _highlightPool.Count; i++) _highlightPool[i].SetActive(false);
        }

        /// <summary>Removes every piece view (live or replay).</summary>
        void ClearPieceViews()
        {
            _raisedViews.Clear();
            foreach (var go in _pieceViews.Values) Destroy(go);
            _pieceViews.Clear();
            _viewLooks.Clear();
        }

        // ───────────────────────── HUD ─────────────────────────

        string ReplayStatus()
        {
            var f = _replay.Frames[_replayPly];
            string state = f.Status.IsOver() ? f.Status.Describe(f.Winner) : $"{f.SideToMove} to move";
            return $"Replay — {f.Label} — {state}";
        }

        void DrawReplayBar()
        {
            _counterStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };

            float top = Screen.height - ReplayBarHeight;
            GUI.Box(new Rect(0, top, Screen.width, ReplayBarHeight), GUIContent.none);

            float y = top + 9, h = ReplayBarHeight - 18, w = 40;
            int last = _replay.MoveCount;
            GUI.enabled = _replayPly > 0;
            if (GUI.Button(new Rect(10, y, w, h), "|<")) ReplayGoTo(0, animate: false);
            if (GUI.Button(new Rect(54, y, w, h), "<")) ReplayGoTo(_replayPly - 1, animate: true, moveDuration);
            GUI.enabled = _replayPly < last;
            if (GUI.Button(new Rect(98, y, w, h), ">")) ReplayGoTo(_replayPly + 1, animate: true, moveDuration);
            if (GUI.Button(new Rect(142, y, w, h), ">|")) ReplayGoTo(last, animate: false);
            GUI.enabled = last > 0;

            // The slider snaps to whole moves and jumps without animation, so a fast drag shows every position at once.
            var sliderRect = new Rect(200, top + ReplayBarHeight / 2f - 6, Mathf.Max(50, Screen.width - 330), 12);
            float value = GUI.HorizontalSlider(sliderRect, _sliderValue, 0, Mathf.Max(1, last));
            if (last > 0 && !Mathf.Approximately(value, _sliderValue))
            {
                _sliderValue = value;
                int target = Mathf.RoundToInt(value);
                if (target != _replayPly) ReplayGoTo(target, animate: false);
            }
            GUI.enabled = true;

            GUI.Label(new Rect(Screen.width - 120, top, 110, ReplayBarHeight), $"{_replayPly} / {last}", _counterStyle);
        }
    }
}
