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
                        if (HappinessData.VerboseLog) Main.modLogger.LogInfo($"Added {amount} amount to {impType} in happiness");
                    }
                }
            }
        }

        foreach (JToken jtoken in rootObject.SelectTokens("$.happinessData.*").ToList())
        {
            JObject token = jtoken.TryCast<JObject>();
            if (token != null)
            {
                int value = ValueGetter(token);
                if (value == int.MaxValue) return;

                if(HappinessData.VerboseLog) Main.modLogger.LogMessage("Parsed value "+value + "for "+token.Path.Split('.').Last());

                switch (token.Path.Split('.').Last())
                {
                    case "base": HappinessData.BASE_HAPPINESS = value; break;
                    case "capital": HappinessData.CAPITAL_HAPPINESS = value; break;
                    case "connection": HappinessData.CONNECTION_HAPPINESS = value; break;
                    case "mindaggers": HappinessData.MIN_DAGGERS = value; break;
                    case "maxdaggers": HappinessData.MAX_DAGGERS = value; break;
                    case "mindbender": HappinessData.MINDBENDER_HAPPINESS = value; break;
                    case "naturedivider": HappinessData.NATURE_DIVIDER = value; break;
                    case "garrison": HappinessData.GARRISON_HAPPINESS = value; break;
                    case "threshold": HappinessData.HAPPY_CITY_THRESHOLD = value; break;
                    case "boostpercentage": HappinessData.HAPPINESS_BOOST_PERCENTAGE = value; break;
                    case "park": HappinessData.PARK_HAPPINESS = value; break;
                    case "stabilizer": HappinessData.STABILIZER_HAPPINESS = value; break;
                    case "popgrowth": HappinessData.POPGROWTH_HAPPINESS = value; break;

                    case "obstructedrule":
                        {
                            if (value == 1) HappinessData.ObstructedTempleRule = true;
                            else HappinessData.ObstructedTempleRule = false;
                            break;
                        }
                    case "verboselog":
                        {
                            if (value == 1) HappinessData.VerboseLog = true;
                            else HappinessData.VerboseLog = false;
                            break;
                        }

                    default: break;
                }
            }
        }
    }

    private static int ValueGetter(JObject token)
    {
        if (token["value"] != null) return token["value"].ToObject<int>();
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