using System;
using HarmonyLib;

namespace NoR.Randomizer.Patches
{
    /// <summary>New Game / Load Game must wait until the enemy pool finished loading at the title screen.</summary>
    [HarmonyPatch(typeof(GameStart_menu), "invoke_newgame")]
    internal static class BlockNewGameWhileLoading
    {
        private static bool Prefix() { return !Plugin.PoolBusy; }
    }

    [HarmonyPatch(typeof(GameStart_menu), "invoke_loadgame")]
    internal static class BlockLoadGameWhileLoading
    {
        private static bool Prefix() { return !Plugin.PoolBusy; }
    }

    /// <summary>Starts a randomized state when the player chooses "New Game".</summary>
    [HarmonyPatch(typeof(GameStart_menu), "fun_Newgame")]
    internal static class NewGamePatch
    {
        private static void Prefix()
        {
            Plugin.OnNewGame();
        }
    }

    /// <summary>Writes the randomizer settings into the save slot (same ES2 file, own tag).</summary>
    [HarmonyPatch(typeof(SaveFile), "SaveFlag")]
    internal static class SavePatch
    {
        public const string Tag = "NoR_Rando";

        private static void Postfix(string Path)
        {
            if (Plugin.State == null) return;
            try
            {
                ES2.Save(Plugin.State.Serialize(), Path + Tag);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Saving randomizer data failed: " + e);
            }
        }
    }

    /// <summary>Reads the randomizer settings when a save slot is loaded (before the game scene starts).</summary>
    [HarmonyPatch(typeof(LoadFile), "SetYesButtonClicked")]
    internal static class LoadPatch
    {
        private static void Prefix(string ___LoadPath, int ___CurrentSelectNum)
        {
            string path = ___LoadPath + string.Format("{0:00}", ___CurrentSelectNum + 1) + ".txt?tag=" + SavePatch.Tag;
            string data = null;
            try
            {
                if (ES2.Exists(path)) data = ES2.Load<string>(path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Reading randomizer data failed: " + e);
            }
            Plugin.OnLoadGame(data);
        }
    }
}
