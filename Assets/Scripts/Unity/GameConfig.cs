namespace Chess.Unity
{
    /// <summary>What the game scene should do when it opens.</summary>
    public enum StartMode
    {
        /// <summary>A fresh local PvP game (<see cref="GameConfig.Classic"/> picks Normal or Enhanced).</summary>
        NewGame,
        /// <summary>Pick up the last saved match where it was left.</summary>
        Continue,
        /// <summary>Open the last saved match in the move-by-move replay.</summary>
        Replay,
    }

    /// <summary>
    /// The hand-off between the main menu and the game scene: the menu fills it in (<see cref="Request"/>),
    /// the game scene reads it exactly once (<see cref="Take"/>). Static, so it survives the scene load.
    /// When the game scene is opened directly (Play in the editor) nothing is pending and the
    /// inspector settings of BoardController apply, as before.
    /// </summary>
    public sealed class GameConfig
    {
        public StartMode Mode { get; }

        /// <summary>Normal chess (classic start position, no power-ups). Only meaningful for <see cref="StartMode.NewGame"/>:
        /// Continue and Replay use whatever mode the saved match was played in.</summary>
        public bool Classic { get; }

        GameConfig(StartMode mode, bool classic)
        {
            Mode = mode;
            Classic = classic;
        }

        static GameConfig _pending;

        public static void Request(StartMode mode, bool classic = false) => _pending = new GameConfig(mode, classic);

        /// <summary>The pending request (or null) — and clears it, so a request is only ever used once.</summary>
        public static GameConfig Take()
        {
            var config = _pending;
            _pending = null;
            return config;
        }
    }
}
