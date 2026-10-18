# ChessSharp (Chess#) — Game Design & Context

> Purpose of this file: a single source of truth to give an AI assistant (or a new collaborator) full context on the project.
> Sections marked **DECIDED** are settled. Sections marked **OPEN** still need a decision.
> Detailed design for the next batch of features (check highlight, JSON data, boards, save/replay, menu) is in **`FEATURES_PLAN.md`**.

---

## 1. Overview

**ChessSharp** is a chess game made in **C# with Unity**, with enhanced mechanics on top of standard chess:

- Pieces can be **upgraded** (power-ups, inspired by *Balatro*).
- Players can **modify the composition of their set** (swap in new/fairy piece types).
- Multiple **game modes** (see §2).
- **2D top-down** visuals (see §3).

## 2. Game modes

| Mode | Description | Status |
|---|---|---|
| **Local PvP (hotseat)** | Two players on the same game instance / same computer. | **Current focus** |
| Online PvP | Two players on different game instances. | Later (Part II/III) |
| Rogue-like vs AI | Balatro-style run against computer opponents; enemy is driven by a neural network. | Later (Part II/III) |

**DECIDED:** Focus **only on local PvP** until all Part I milestones are done.

## 3. Visual style — DECIDED

- **2D only, top-down** — classic chess look; every piece is a 2D PNG sprite.
- ~~2.5D / isometric view~~ — **dropped**.
- **No board flip** — the board always stays in the same orientation (White at the bottom).

Implementation: standard Unity 2D setup — orthographic camera, `SpriteRenderer`s for board squares, pieces and highlights. Board ↔ world conversion is simple math (square size × file/rank); clicks are converted from screen → world → square directly (no 3D raycasts needed).

---

## 4. Architecture principles — DECIDED (recommended)

1. **Rules engine is pure C#** — no `MonoBehaviour`, no `GameObject`, no `UnityEngine.Random`. Lives in its own Assembly Definition (e.g. `Chess.Core`) with **no Unity references**; Unity code lives in `Chess.Unity` and only *displays* state and *sends actions*.
   - Enables: easy online play (send moves), fast simulation for AI (can run outside Unity, e.g. .NET console for training), unit testing.
2. **Commands / actions** — a `Move` (later `Action`) is a small, serializable data object. UI creates it → engine validates and applies it.
3. **Data-driven piece movement** — no hardcoded "bishop moves diagonally". Pieces are composed from movement primitives:
   - **Slide** in a direction (optional max range) — rook, bishop, queen.
   - **Leap** by an offset — knight, king.
   - **Separate `moves` and `captures` patterns** per piece (`captures` can be `"same"`). Only the pawn differs today, but the model stays general.
   - **Flags** — a list of ability ids (e.g. `CreatesEnPassant`, `Promotes`, `BerserkPawn`); first-move-only etc. are conditions on a pattern.
   - Piece definitions are loaded at startup from **`pieces.json`**; ability definitions and parameters from **`abilities.json`** (see `FEATURES_PLAN.md` §2). Inspired by fairy-chess / Betza notation. Fairy pieces (Archbishop = B+N, Chancellor = R+N, Amazon = Q+N, Camel = (3,1) leaper, Nightrider = repeated knight) become nearly free.
4. **Board representation** — simple array (no bitboards; they don't fit custom pieces well). Use a **width × height grid + a mask of valid squares** instead of a hard-coded 8×8, so alternative board shapes (§7.4) come for free. Standard chess = 8×8 with every square valid.
   - **Per-piece state:** each piece instance carries its own data — type, owner, capture counter (berserk pawn), list of modifiers, "standing on a wall" flag. The Zobrist hash must include all of it.
5. **Legal moves are generic** — generate pseudo-legal moves → simulate each → reject if own royal piece ends up attacked. No special-case pin logic. Required for power-ups to interact correctly with check.
6. **Determinism** — any randomness in rules uses a **seeded RNG** inside the engine (needed for online + replays).
7. **Move history** from milestone 1 (needed for undo, repetition, AI search).
8. **"Royal" flag** instead of hardcoding "the King" (supports future royal variants).
9. The engine never knows about rendering, input or camera — those are presentation only.

---

## 5. Milestone 1 — Basic chess, step 1 (board, movement) — ✅ DONE

Scope: 2D board, pieces with their basic movement, click-to-move input, highlights.

### 5.1 Moving pieces — DECIDED: click-to-select, click-to-move

1. Select a piece.
2. Select one of the highlighted squares it can move to.

Input state machine: **Idle → PieceSelected → Animating → (turn switch) → Idle**

| State | Click on | Result |
|---|---|---|
| Idle | Own piece | Select it, show highlights |
| Idle | Empty square / enemy piece | Nothing |
| PieceSelected | Highlighted square | Perform move |
| PieceSelected | Same piece | Deselect |
| PieceSelected | Another own piece | Switch selection directly |
| PieceSelected | Anything else | Deselect |
| PieceSelected | Right-click / Esc | Deselect |
| Animating | Anything | Ignored (input locked until the move animation finishes) |

The UI asks the engine `GetMovesFrom(square)` and just draws the result — UI does not change when legality rules or power-ups are added later.

**Click detection:** convert the mouse position to world space with the orthographic camera, then to a board square (floor of x/y divided by square size). Clicks outside the board deselect.

**Promotion in M1:** auto-promote to Queen (picker UI comes in M2).

### 5.2 Highlights

Visually distinct types:
- Selected piece's square
- Quiet move target (small dot)
- Capture target (ring / corner markers; enemy piece stays visible)
- **Last move** (from + to squares, subtle tint) — recommended in M1 for hotseat readability
- *(M2)* King in check — plus the **square(s) of the checking piece(s)** (see `FEATURES_PLAN.md` §1)

---

## 6. Milestone 2 — Basic chess, step 2 (complete standard chess) — ✅ DONE

> Perft passes at depth 5 from the start position and on Kiwipete / positions 3–5 (EditMode tests in `Assets/Tests/EditMode`). Undo implemented; clocks not yet.

- Check, checkmate, stalemate
- Castling (incl. can't castle out of / through / into check)
- En passant
- Promotion with piece picker UI
- 50-move rule
- Threefold repetition (Zobrist hashing — hash must include piece type **and** upgrades for later)
- Insufficient material
- Resign / draw offer
- Optional: clocks, undo
- **Validation: perft tests** from the start position — depth 1–5 = 20 / 400 / 8,902 / 197,281 / 4,865,609.

---

## 7. Milestone 3 — Power-ups, shop, new piece types

Suggested split:
- **3a — New piece types** (pure movement data if M1 is data-driven).
- **3b — Power-up hook system + pre-match shop.**

**Shop:** players buy power-ups **before the match**.

### 7.1 Power-up categories (Balatro mapping — draft, to be discussed)

| Balatro | Chess++ equivalent |
|---|---|
| Jokers (global passives) | Army-wide passives, e.g. "Knights can also move 1 square orthogonally" |
| Tarot (modify a card) | Upgrade a specific piece (extra move pattern, shield…) |
| Planets (level up a hand type) | Level up a piece **type** (all bishops get something) |
| Spectral (risky / big) | Transform a pawn into a fairy piece, sacrifice for a big effect |
| Vouchers | Rule changes (extra shop slot, start with promoted pawn…) |

### 7.2 Engine integration

- Power-ups hook into well-defined points: `ModifyMoveGeneration`, `OnBeforeCapture`, `OnAfterMove`, `OnTurnStart`, … with a **defined resolution order**.
- Anything that can change legality **must run inside the simulation used for check detection**, so checkmate is always correct.

### 7.3 OPEN questions (decide before M3)

1. **In-match active abilities (consumables)?** If yes: does using one consume the turn? Can it escape check? (Checkmate then = no legal moves **and** no legal ability uses.)
2. **Can the king be upgraded / replaced?** (Engine uses a "royal" flag either way.)
3. **PvP shop economy:** equal budgets? Blind or alternating picks? Is the opponent's loadout visible before the match?
4. ~~**Random effects?**~~ **DECIDED:** yes (e.g. random holes in the board) — always through the seeded RNG in the engine; the seed is stored in the match file.

### 7.4 Planned mechanics — ideas (list will grow)

1. **Berserk pawn** (ability id `BerserkPawn`, pawns only; threshold and pattern come from `abilities.json`, default threshold 2) — when a pawn has captured **2 pieces**, it goes berserk and its capture pattern widens (tracked **per pawn**). Capture pattern (P = pawn, x = can capture, White's perspective, forward = up):
   ```
   x x x
   _ P _
   _ x _
   ```
   i.e. it can capture forward-left, forward, forward-right and one square straight back.
2. **Wall** — new piece that can only **replace a pawn** (e.g. in the setup / shop).
   - Any piece can move **on top of** a wall; the wall then acts as an **extra life**.
   - If a piece standing on a wall is captured, the **wall is destroyed instead**, and the attacking piece **returns to the square it came from** (the attack costs the attacker its turn).
   - Knights **cannot jump over** walls.
   - Full rules: see §7.5 (decided).
3. **Archbishop** — new piece, moves as Bishop + Knight.
4. **Different board shapes** for some levels deep in a rogue-like run, e.g. a plus shape, or a normal board with the centre missing. (Engine: valid-square mask, §4.4.)
5. **Modifiers for standard pieces** — e.g. the King can move 2 squares in a straight line.
6. **Drawbacks on modifiers** — e.g. the King moves *only* 2 squares in a straight line, losing its 1-square straight move. (Data-driven: a modifier simply replaces/removes movement primitives.)
7. *More to come.*

### 7.5 Rules for the new mechanics — DECIDED

**Wall**
1. Walls are **stationary** (never move on their own).
2. **Nobody passes through a wall.** If a piece's trajectory meets a wall, the piece **stops on the wall**. An empty wall **can be captured** (destroyed).
3. A **knight's path is blocked where the wall is** — a wall on its path stops the jump; nothing more.
4. A **king standing on a wall is not in check** when attacked.
5. Walls are **reusable**: when a piece leaves a wall, the wall stays on its square for other pieces.
6. **En passant** against a pawn that ended its double step on a wall **breaks that wall** (normal wall rule: the wall is destroyed instead of the pawn).

**Berserk pawn**
1. A berserk pawn keeps its normal moves and promotion; berserk only changes its `captures`.
2. Berserk is **pawn-only** and is **lost on promotion** (the counter goes with it).
3. **Breaking a wall counts as a capture** for the berserk counter.

---

## 8. Milestone 4 — Tests, balance pass, cleanup

Originally "possible refactoring". With the architecture in §4 applied from M1, this becomes: test coverage, balance pass, cleanup — a checkpoint before networking.

*(Suggested Milestone 0: Unity 6 LTS project, git + Unity `.gitignore` + Git LFS for art, assembly definitions, folder structure.)*

---

## 9. Parts II & III — Other game modes (later)

### Online PvP
- Send **moves (commands)**, not state; both sides validate with the deterministic engine.
- Candidates: Netcode for GameObjects + Unity Relay, Mirror, Photon.

### Rogue-like vs AI (Balatro-style)
- Run structure with shops between rounds; enemies scale in strength.
### 9.1 AI opponent — DECIDED: own search engine + small NN (later)

**Final shape:** our own **alpha-beta search in C#** + a **small neural network as the evaluation function**. The network is added **last**.

Plan:
1. **Now:** alpha-beta search (iterative deepening, move ordering, transposition table) built on our own rules engine's move generation, + a **handcrafted evaluation**.
   - Search is mechanic-agnostic: it only asks the engine for legal moves and applies them, so every new piece / modifier / board shape works for the AI automatically.
   - Eval uses **generic terms** (material, mobility, attacked pieces, king safety). Piece values computed automatically from mobility, so new pieces/modifiers get a value without hand-tuning. Small extra terms per mechanic where needed (e.g. pawn with 1 capture ≈ close to berserk; piece on a wall bonus).
2. **When the rules stop changing:** train a small NN (PyTorch → **ONNX** → **Sentis / Unity Inference Engine**) on self-play games generated by our engine, and swap it in as the evaluator. The search stays the same.
   - NN input should describe pieces **by capability** (movement primitives, flags, capture count, on-wall) rather than by fixed piece ID, so new modifiers don't always force a full retrain.
3. **Difficulty** (both stages): search depth, deliberate mistakes, stronger enemy power-ups per round (like Balatro blinds).

Implementation notes:
- Evaluation behind an interface, e.g. `IEvaluator.Evaluate(Position)` → handcrafted and NN are interchangeable; play them against each other to check whether the NN is actually better.
- Run the search on a background thread (pure C# engine → `Task.Run`) so the game doesn't freeze.
- Requires the rules engine to run headless (outside Unity) for self-play / data generation — another reason for §4.1.

**Why not a NN first:** each new mechanic changes the NN's input format and makes old self-play data invalid → regenerate data + retrain (hours–days per change); unseen rogue-like combinations are unpredictable; blunders are hard to debug. A handcrafted eval degrades gracefully and is fixed in minutes. A rogue-like also doesn't need a superhuman AI — it needs one that scales.

**Rejected: Fairy-Stockfish** (+ fine-tuned NNUE). It supports archbishop, piece modifiers/drawbacks and (mostly) board shapes via its config, but **not** the berserk pawn (per-piece capture count / type change) or the wall (stacking, failed capture returning the attacker) without a C++ fork. Other issues: two separate rulebooks to keep in sync, GPLv3 licence, native binary (awkward on mobile, no WebGL), and its NNUE is trained per fixed variant (useless for varying rogue-like runs). **Still useful as a dev tool:** perft validation and sparring partner for variants it can express.

---

## 10. Decisions log

| Topic | Decision |
|---|---|
| Engine | Unity + C# |
| Current scope | Local PvP only until Part I milestones are done |
| Move input | Click piece → click highlighted square |
| Project name | ChessSharp (renamed from FCPL_ChessPlusPlus) |
| Visuals | 2D top-down only; 2.5D/isometric view dropped |
| Board flip | None — board orientation is fixed (White at bottom) |
| Promotion in M1 | Auto-Queen |
| Board representation | Width × height grid + valid-square mask (supports board shapes) |
| AI opponent | Own C# alpha-beta search + handcrafted eval now; small NN eval later (§9.1) |
| Fairy-Stockfish | Rejected as the game AI; dev/testing tool only |
| Threefold / 50-move | Automatic draw (no claim needed) — simpler for hotseat |
| Draw offer (hotseat) | Side to move offers; opponent accepts/declines on their turn; making a move declines |
| Undo | Allowed any time in local PvP; after resign / agreed draw it just reopens the game |
| HUD | Temporary IMGUI bar (status, Undo, Offer draw, Resign, New game) until a proper UI pass |
| Castling / en passant | Generic piece abilities (`Castles`, `CastlePartner`, `CreatesEnPassant`, …), not hardcoded to king/rook/pawn |
| Piece / ability data | Loaded at startup from `pieces.json` + `abilities.json` (Newtonsoft JSON), not ScriptableObjects |
| Check detection | Uses only pieces' `captures` patterns; UI highlights king + checking piece(s) |
| Ability parameters | Global, defined only in `abilities.json`; no per-piece overrides |
| Save files | Match file (rules snapshot + setup + seed + event list) + snapshot file for Continue |
| Classic mode | Standard chess |
| Wall / berserk rules | See §7.5 |
