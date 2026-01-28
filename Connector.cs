using HarmonyLib;
using Polytopia.Data;
using Newtonsoft.Json.Linq;
using Il2CppSystem.Linq;

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
                        if(Main.VerboseLog) Main.modLogger.LogInfo($"Added {amount} amount to {impType} in happiness");
                    }
                }
            }
        }

        foreach (JToken jtoken in rootObject.SelectTokens("$.happinessData.*").ToList())
        {
            Main.modLogger.LogMessage(jtoken.HasValues.ToString());
            JObject token = jtoken.TryCast<JObject>();
            if(token != null)
            {
                int value = ValueGetter(token);
                if(value == int.MaxValue) return;

                switch (token.Path.Split('.').Last())
                {
                    case "base": Main.BASE_HAPPINESS = value; break;
                    case "capital": Main.CAPITAL_HAPPINESS = value; break;
                    case "connection": Main.CONNECTION_HAPPINESS = value; break;
                    case "mindaggers": Main.MIN_DAGGERS = value; break;
                    case "maxdaggers": Main.MAX_DAGGERS = value; break;
                    case "mindbender": Main.MINDBENDER_HAPPINESS = value; break;
                    case "naturedivider": Main.NATURE_DIVIDER = value; break;
                    case "garrison": Main.GARRISON_HAPPINESS = value; break;
                    case "threshold": Main.HAPPY_CITY_THRESHOLD = value; break;
                    case "verboselog": {
                        if(value == 1) Main.VerboseLog = true;
                        else Main.VerboseLog = false;
                        break;
                    }
                    
                    default: break;
                }
            }
        }
    }

    private static int ValueGetter(JObject token)
    {
        if(token["value"] != null) return token["value"].ToObject<int>();
        return int.MaxValue;
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