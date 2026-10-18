# ChessSharp — Next Features Plan

> Design decisions for the next batch of features (discussed 2026-10-18). Nothing here is implemented yet.
> General context and architecture: see `GAME_DESIGN.md`.

## Suggested implementation order

1. Pieces and abilities from JSON (§2) + board sizes and holes (§3)
2. Check highlight (§1)
3. Event log → save / continue → replay (§4)
4. Menu (§5)

---

## 1. Signaling check — DECIDED

- When a king is in check, highlight the **king's square** and the **square(s) of the checking piece(s)**.
- Supports **several attackers** (double check). Attackers are found by scanning all enemy pieces, so **discovered check** is covered too.
- Check detection uses **only the pieces' `captures` patterns, never `moves`**. Today only the pawn moves differently than it captures, but the model stays general for future pieces.
- Test: generate the capture targets of every enemy piece (whether or not the target square is occupied). If the king's square is among them → check, and those pieces are the attackers.
- A king standing on a **wall** is not in check (see `GAME_DESIGN.md` §7.5).

## 2. Pieces and abilities from JSON — DECIDED

Both files are loaded **once at startup** into read-only registries. Parsed with **Newtonsoft JSON** (`com.unity.nuget.newtonsoft-json`), not `JsonUtility`.

### 2.1 `pieces.json`

```json
{ "version": 1,
  "pieces": [
    { "name": "rook",
      "moves":    [{ "dirs": [[1,0],[-1,0],[0,1],[0,-1]], "slide": true }],
      "captures": "same",
      "flags": ["CastlePartner"] },
    { "name": "pawn",
      "moves":    [{ "dirs": [[0,1]], "maxSteps": 1 },
                   { "dirs": [[0,1]], "maxSteps": 2, "condition": "firstMove" }],
      "captures": [{ "dirs": [[1,1],[-1,1]], "maxSteps": 1 }],
      "flags": ["CreatesEnPassant", "Promotes", "BerserkPawn"] }
  ] }
```

- Directions are written from **White's view**; `y` is flipped for Black.
- `slide: true` = slider (rook, bishop, queen); `slide: false` = leaper (knight, king).
- `captures: "same"` reuses the `moves` patterns.
- `flags` contains **only ability ids** — no parameters.

### 2.2 `abilities.json`

```json
{ "version": 1,
  "abilities": [
    { "id": "BerserkPawn",
      "category": "powerup",
      "appliesTo": ["pawn"],
      "displayName": "Berserk",
      "description": "After capturing 2 pieces, this pawn can also capture forward and backward.",
      "params": {
        "threshold": 2,
        "mode": "replace",
        "captures": [{ "dirs": [[-1,1],[0,1],[1,1],[0,-1]], "maxSteps": 1 }]
      } },
    { "id": "CreatesEnPassant", "category": "rule", "appliesTo": ["pawn"] }
  ] }
```

- `category`: `rule` (standard chess rules such as en passant, castling, promotion) or `powerup`.
- `appliesTo`: which piece types may have the ability.
- `mode`: whether the ability's pattern `replace`s or `add`s to the piece's pattern.
- **Parameters are global.** Changing `threshold` to 3 in the file changes it for every pawn with the ability. No per-piece overrides; a variant gets a new id.

### 2.3 Engine side

- Each piece on the board is an **instance** that points to its definition and carries its own state: owner, position, `hasMoved`, capture counter, abilities gained during the match.
- The **behavior** of each ability is a C# class (e.g. `BerserkPawnAbility`) that reads its `params`. A registry maps ability id → class.
- Abilities act through hooks: `ModifyMoves`, `ModifyCaptures`, `OnAfterCapture`, `OnTurnStart`, …
- Movement rules:
  - `moves` only target **empty** squares.
  - `captures` only target squares with an **enemy** piece.
  - A slider stops before any piece or hole; it can capture the first piece it meets.
  - A move is legal only if the player's own king is not attacked after it (simulate → check test).

### 2.4 Berserk pawn

- Per-pawn capture counter, increased in `OnAfterCapture`. **Breaking a wall counts as a capture.**
- At the threshold, the capture pattern is replaced by: forward-left, forward, forward-right, back.
- Keeps its normal moves and promotion. **Pawn-only; lost on promotion.**
- Check detection picks up the new pattern automatically (it reads `captures` from the instance).

### 2.5 Validation at startup (fail with a clear message)

- A flag in `pieces.json` with no entry in `abilities.json`.
- An ability on a piece its `appliesTo` doesn't allow.
- An ability id with no C# class.
- Missing or wrong-type `params`.

## 3. Board types — DECIDED

- Uses the existing **width × height grid + valid-square mask** (`GAME_DESIGN.md` §4.4). A hole is an invalid cell.
- **Holes:** sliders **cannot pass** over a hole (they stop before it); knights **can jump** over holes.
- **Random holes:**
  - never on a starting piece's square;
  - the board stays connected (no unreachable regions);
  - mirrored between the two sides for fairness;
  - generated from a **seed** (stored in the match file).
- **Predefined holes:** described in JSON (e.g. a `boards` section: name, width, height, list of holes).
- **Sizes:** longer, wider or both. The camera fits itself to the board size. The pawn's double-step and promotion rows depend on board height.
- **Starting setup on wider boards: OPEN** — to be determined later.

## 4. Save and replay — DECIDED

- **Match file (JSON):**
  - `version`;
  - a **snapshot of the rules** used (`pieces.json` + `abilities.json` content), so old games always replay exactly as played even if the files change later;
  - the board: size, holes, seed;
  - the starting setup, including the power-ups / special pieces chosen;
  - an ordered list of **events**: moves, captures, promotions, abilities gained or triggered, walls broken.
- **Snapshot file:** the current position ("screenshot" of the board), used by **Continue**. It can always be rebuilt from the match file.
- **Replay** with a slider: to show move N, rebuild from the start and apply N events. Cached snapshots only if this gets slow.
- **"Play from here"** (available at any point in the replay): branch the game. Later events are cut off; save as a new file by default, or overwrite.

## 5. Menu — DECIDED

1. **Play Classic** — standard chess.
2. **Play Enhanced**
   - board size;
   - holes: none / random / preset (+ seed);
   - special pieces and power-ups — **the same for both players** for now (later, with more add-ons, each player may choose differently);
   - more options can be added later.
3. **Continue match** — pick a save; loads the snapshot file.
4. **Replay match** — pick a save; loads the match file.

- A single **`GameConfig`** object is filled in by the menu and read by the game scene. Continue / Replay build it from the save file.
- The save picker shows date, board size and move count.

## Open

- Starting setup on wider boards (§3).
