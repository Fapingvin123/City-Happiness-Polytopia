using BepInEx.Logging;
using HarmonyLib;
using Polytopia.Data;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Il2CppSystem.Linq;
using BepInEx.Configuration;

namespace happiness;

public delegate int HappinessModifier(WorldCoordinates coordinates, GameState gameState);

public static class Connector
{

    #region Parsing

    public static Dictionary<ImprovementData.Type, int> dicthappiness = new();

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.AddGameLogicPlaceholders))]
    private static void GameLogicData_Parse(GameLogicData __instance, JObject rootObject)
    {
        foreach (JToken jtoken in rootObject.SelectTokens("$.improvementData.*").ToList())
        {
            JObject token = jtoken.TryCast<JObject>();

            if (token != null)
            {
                if (EnumCache<ImprovementData.Type>.TryGetType(token.Path.Split('.').Last(), out var impType))
                {
                    string key = token["GrantsHappiness"] != null ? "GrantsHappiness" : "grantsHappiness";
                    if (token[key] != null)
                    {
                        int amount = token[key]!.ToObject<int>();
                        dicthappiness[impType] = amount;
                        token.Remove(key);
                        Main.modLogger.LogInfo($"Added {amount} amount to {impType} in happiness");
                    }
                }
            }
        }
    }

    #endregion

    #region Modifiers
    /// <summary>
    /// List of functions that can alter city happiness from the outside.
    /// </summary>
    public static readonly List<HappinessModifier> modifiers = new List<HappinessModifier>();

    public static void RegisterModifier(HappinessModifier modifier)
    {
        if (!modifiers.Contains(modifier))
            modifiers.Add(modifier);
    }

    public static void UnregisterModifier(HappinessModifier modifier)
    {
        modifiers.Remove(modifier);
    }


    #endregion
}