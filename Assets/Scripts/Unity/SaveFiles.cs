using System.IO;
using UnityEngine;

namespace Chess.Unity
{
    /// <summary>
    /// Where the last match is kept: two small files in the player's save folder (Application.persistentDataPath/Saves).
    /// They're named .txt as asked, but the content is packed bits (see Chess.Core.PositionCodec / MatchRecorder),
    /// so a text editor shows gibberish.
    /// </summary>
    public static class SaveFiles
    {
        public const string MovesFileName = "last_match_moves.txt";
        public const string BoardFileName = "last_match_board.txt";

        public static string Folder => Path.Combine(Application.persistentDataPath, "Saves");
        public static string MovesPath => Path.Combine(Folder, MovesFileName);
        public static string BoardPath => Path.Combine(Folder, BoardFileName);

        /// <summary>Continue needs at least the board file.</summary>
        public static bool HasLastMatch => File.Exists(BoardPath);

        public static void Write(byte[] movesFile, byte[] boardFile)
        {
            Directory.CreateDirectory(Folder);
            WriteSafely(MovesPath, movesFile);
            WriteSafely(BoardPath, boardFile);
        }

        public static byte[] ReadBoard() => File.ReadAllBytes(BoardPath);

        /// <summary>The move-history file, or null if it's missing (Continue then works from the board file alone).</summary>
        public static byte[] ReadMovesOrNull() => File.Exists(MovesPath) ? File.ReadAllBytes(MovesPath) : null;

        /// <summary>Writes a temp file first, so a crash mid-write never leaves half a save behind.</summary>
        static void WriteSafely(string path, byte[] bytes)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
