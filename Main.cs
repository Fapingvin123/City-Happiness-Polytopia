using BepInEx.Logging;
using HarmonyLib;
using Polytopia.Data;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Il2CppSystem.Linq;
using Unity.Collections;
using PolyMod;


namespace happiness;

public static class Main
{
    public const int HAPPY_CITY_THRESHOLD = 5;
    public const int HAPPINESS_SEGMENTS = 5;
    public const int MIN_DAGGERS = 2;
    public const int MAX_DAGGERS = 6;
    public static bool VerboseLog = false;
    private static Il2CppSystem.Collections.Generic.List<TileData> CityTilesP = new(); // So that entire map isnt checked every command

    public static ManualLogSource modLogger;
    public static void Load(ManualLogSource logger)
    {
        PolyMod.Loader.AddPatchDataType("customrewards", typeof(CityReward));
        Harmony.CreateAndPatchAll(typeof(Main));
        Harmony.CreateAndPatchAll(typeof(Connector));
        modLogger = logger;
        logger.LogMessage("Happiness.dll");
    }


    #region Happiness
    public static int getHappiness(WorldCoordinates coordinates, GameState gameState)
    {
        TileData tile = gameState.Map.GetTile(coordinates);
        if (tile.improvement == null || tile.improvement.type != ImprovementData.Type.City)
        {
            modLogger.LogError("Not a city for coords " + coordinates);
            return int.MaxValue;
        }

        PlayerState player;
        gameState.TryGetPlayer(tile.owner, out player);
        int happiness = 2; // Base value
        if(VerboseLog) Main.modLogger.LogMessage("---------- Evalling "+ tile.improvement.name + ": 2 as base ---------------");


        /////////////////////////////
        ///  HAPPINESS
        ///  BOOSTERS
        /////////////////////////////
        if (player.GetCurrentCapitalCoordinates(gameState) == coordinates)
        {
            if(VerboseLog) Main.modLogger.LogMessage("+3 from Capital");
            happiness += 3;
        }
        else if (tile.improvement.connectedToCapitalOfPlayer == player.Id)
        {
            happiness += 1; // +1 if connected to your capital
            if(VerboseLog) modLogger.LogMessage("+1 from connection");
        }

        int naturecounter = 0;
        foreach (TileData item in ActionUtils.GetCityArea(gameState, tile))
        {
            /*if (item.improvement != null && item.improvement.IsMonument())
            {
                happiness += 2; // +2 happiness for each monument
                if (item.improvement.type == ImprovementData.Type.Monument1) happiness += 2; // Additional +2 for Altar of Peace
            }*/
            if (item.improvement != null && item.improvement.IsTemple())
            {
                happiness += 1; // +1 Happiness for each temple
                if(VerboseLog) modLogger.LogMessage("+1 from temple");
            }
            if (item.improvement != null && Connector.dicthappiness.TryGetValue(item.improvement.type, out int bonus))
            {
                happiness += bonus;
                if(VerboseLog) modLogger.LogMessage("+"+ bonus + " from improvement");
            }
            if (item.improvement == null)
            {
                naturecounter++;
            }
            if (item.unit != null && item.unit.owner != item.owner && item.unit.type == UnitData.Type.MindBender && !player.HasPeaceWith(item.unit.owner))
            {
                happiness -= 1;
                if(VerboseLog) modLogger.LogMessage("-1 from mindbender");
            }
        }
        happiness += (int)(naturecounter / 5); //+1 happiness for each 5 unimproved tile
        if(VerboseLog) modLogger.LogMessage("+"+naturecounter/5+" from nature");

        //+1 happiness if city has unit with at least 4 (attack+defense)
        if (tile.unit != null && (tile.unit.GetAttack(gameState) + tile.unit.GetDefence(gameState)) >= 3.9f){
            if(VerboseLog) modLogger.LogMessage("+1 from garrison");
            happiness += 1;
        }

        //+1 for each park (you can negate population growth malus)
        //+1 for Walls cause let's be honest no one chooses that - with the -2 happiness fixed im sure this is overkill
        //+1 for each rebellion (let's go easy on the player shall we)
        foreach (var item in tile.improvement.rewards)
        {
            if (item == CityReward.Park) {happiness += 1; if(VerboseLog) modLogger.LogMessage("+1 from park");}
            //if (item == CityReward.CityWall) {happiness += 1; if(VerboseLog) modLogger.LogMessage("+1 from wall");}
            if (item == EnumCache<CityReward>.GetType("stabilizer")) {happiness += 1; if(VerboseLog) modLogger.LogMessage("+1 from stabilizer");}
        }




        /////////////////////////////
        ///  HAPPINESS
        ///  DRAINERS
        /////////////////////////////

        if (tile.IsBeingCaptured(gameState)) happiness = -100; //Low happiness but city won't revolt while under siege) so purely aesthetic
        //Every population level takes one after the first one
        if (tile.improvement.level > 2)
        {
            happiness -= (tile.improvement.level - 2);
            if(VerboseLog) modLogger.LogMessage("-"+ (tile.improvement.level - 2) + " from crowdedness");
        }

        if (tile.improvement.founder != tile.owner)
        {
            happiness -= 2;
            if(VerboseLog) modLogger.LogMessage("-2 from not being the founder");
        }


        ////////////////////
        /// CUSTOM MODIFIERS
        ////////////////////
        if(Connector.modifiers.Count > 0)
        foreach (var mod in Connector.modifiers)
        {
            try
            {
                happiness += mod(coordinates, gameState);
                if(VerboseLog) modLogger.LogMessage("Changed it by "+mod(coordinates, gameState));
            }
            catch (Exception ex)
            {
                modLogger.LogError($"Happiness modifier failed: {ex}");
            }
        }

        if(VerboseLog) modLogger.LogMessage("Final: "+happiness+ "\n---------------");
        return happiness;

    }
    #endregion


    #region UI

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BuildingUtils), nameof(BuildingUtils.GetInfo))]
    private static void AddHappinessInfo(ref string __result, PolytopiaBackendBase.Common.SkinType skinOfCurrentLocalPlayer, ImprovementData improvementData, ImprovementState improvementState = null, PlayerState owner = null, TileData tileData = null)
    {
        if (owner == null || owner.Id == 0 || owner.Id == 255 || improvementState == null) return;
        if (owner.Id == GameManager.LocalPlayer.Id && tileData.owner != 255 && tileData.owner != 0 && tileData.owner == owner.Id)
        {
            if (improvementData != null && improvementData.type == ImprovementData.Type.City)
            {
                int happiness = getHappiness(tileData.coordinates, GameManager.GameState);
                __result += Localization.Get("currenthappiness", new Il2CppSystem.Object[] { happiness.ToString() });
                __result += " ";
                if (happiness >= HAPPY_CITY_THRESHOLD) __result += Localization.Get("info.happycity");
                else if (happiness <= 0) __result += Localization.Get("info.unhappycity");
                else __result += Localization.Get("info.mehcity");
            }
        }
    }

    public static CityStatusDisplay getDisplay(CityStatusProgressBar a)
    {
        foreach (CityStatusDisplay display in UnityEngine.Object.FindObjectsOfType<CityStatusDisplay>()) // or your own collection of As
        {
            if (display != null && display.progressBar != null)
            {
                var value = display.progressBar;
                if (value != null)
                {
                    if (ReferenceEquals(value, a))
                    {
                        //modLogger.LogMessage("Found display " + display.city.DisplayName);
                        return display;
                    }
                }
            }
        }
        return null;
    }

    public static WorldCoordinates findFromDisplay(CityStatusDisplay display)
    {
        if (display == null) return WorldCoordinates.NULL_COORDINATES;
        GameState state = GameManager.GameState;
        foreach (var tile in state.Map.Tiles)
        {
            if (tile != null && tile.improvement != null && tile.improvement.type == ImprovementData.Type.City)
            {
                Tile tile2 = MapRenderer.Current.GetTileInstance(tile.coordinates);
                if (tile2 != null && tile2.improvement != null)
                {
                    City city2 = tile2.improvement.Cast<City>(); //tile2.improvement as City;
                    if (display.city == city2) return tile.coordinates;
                }
            }
        }
        return WorldCoordinates.NULL_COORDINATES;
    }



    /// <summary>
    /// The Main UI Function
    /// </summary>
    /// <param name="__instance"></param>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CityStatusProgressBar), nameof(CityStatusProgressBar.UpdateFields))]
    public static void newbar(CityStatusProgressBar __instance)
    {
        int basis = __instance.segments.Count; // End of the original segments
        int total = basis + HAPPINESS_SEGMENTS; // End of the happiness segments
        float num = Mathf.Min(__instance.minWidth + (float)(total - basis - 2) * __instance.centerFieldWidth, __instance.maxWidth);

        var display = getDisplay(__instance);
        if (display == null) return;
        TileData tile = GameManager.GameState.Map.GetTile(Main.findFromDisplay(display));

        if (tile.owner != GameManager.LocalPlayer.Id) return;

        var happiness = Main.getHappiness(tile.coordinates, GameManager.GameState);

        for (int j = basis; j < __instance.segments.Count; j++)
        {
            UnityEngine.Object.Destroy(__instance.segments[j].gameObject);
            __instance.segments.RemoveAt(j--);
        }

        for (int i = basis; i < total; i++)
        {
            CityStatusProgressSegment cityStatusProgressSegment;
            if (__instance.segments.Count >= total)
            {
                cityStatusProgressSegment = __instance.segments[i];
            }
            else
            {
                cityStatusProgressSegment = UnityEngine.Object.Instantiate<CityStatusProgressSegment>(__instance.segmentPrefab, __instance.transform, false);
                __instance.segments.Add(cityStatusProgressSegment);
            }
            if (i == basis)
            {
                cityStatusProgressSegment.SetType(CityStatusProgressSegment.Type.Left);
            }
            else if (i == total - 1)
            {
                cityStatusProgressSegment.SetType(CityStatusProgressSegment.Type.Right);
            }
            else
            {
                cityStatusProgressSegment.SetType(CityStatusProgressSegment.Type.Middle);
            }
            float num2 = num / (float)(total - basis);
            int num3 = happiness + basis;
            Color color;
            if (num3 - basis > 2)
            {
                color = Color.green;
            }
            else if (num3 - basis <= 1)
            {
                color = Color.red;
            }
            else color = Color.yellow;
            cityStatusProgressSegment.SetWidth(num2);
            cityStatusProgressSegment.transform.localPosition = new Vector3((float)(i - basis) * num2 - num * 0.5f + cityStatusProgressSegment.width * 0.5f, -0.15f, 0f);
            cityStatusProgressSegment.SetColor((i < num3) ? color : __instance.baseColor);
            cityStatusProgressSegment.SetDot(((num3 - basis) <= 0) && (basis - i > num3 - basis), Color.red);
            //cityStatusProgressSegment.dot.sprite = PolyMod.Registry.GetSprite("unhappy");

            cityStatusProgressSegment.Render();
        }

        // Production boost
        int work = display.city.Tile.Data.CalculateWork(GameManager.GameState);
        if (happiness >= HAPPY_CITY_THRESHOLD)
        {
            work = (int)(work * 1.25);
            display.nameContainer.workLabel.color = Color.green;
            display.nameContainer.workLabel.m_isUsingBold = true;
            display.nameContainer.workLabel.fontStyle = TMPro.FontStyles.Bold;
            display.nameContainer.SetWork(work);
        }
        else
        {
            display.nameContainer.workLabel.color = Color.white;
            display.nameContainer.workLabel.m_isUsingBold = false;
            display.nameContainer.workLabel.fontStyle = TMPro.FontStyles.Normal;
            display.nameContainer.MarkDirty();
            display.nameContainer.UpdateSize();
        }

        // Sixth segment that stores the icon
        CityStatusProgressSegment segment;

        segment = UnityEngine.Object.Instantiate<CityStatusProgressSegment>(__instance.segmentPrefab, __instance.transform, false);
        __instance.segments.Add(segment);
        segment.SetWidth(0.01f); // make it very thin so as the icon hides it completely
        segment.SetType(CityStatusProgressSegment.Type.Left);
        segment.transform.localPosition = new Vector3(-0.5f, -0.15f, 0f); // trial and error ahh hardcode
        segment.SetColor(Color.white);
        segment.SetDot(true, Color.white);
        if (happiness >= 3) segment.dot.sprite = PolyMod.Registry.GetSprite("happiness");
        else if (happiness <= 0) segment.dot.sprite = PolyMod.Registry.GetSprite("unhappiness");
        else segment.dot.sprite = PolyMod.Registry.GetSprite("meh");
        segment.Render();
    }
    #endregion


    #region Gameplay

    [HarmonyPrefix]
    [HarmonyPatch(typeof(IncreaseCurrencyAction), nameof(IncreaseCurrencyAction.Execute))]
    public static void HappyProduction(IncreaseCurrencyAction __instance, GameState state)
    {
        if (state.Map.GetTile(__instance.Source).improvement == null || state.Map.GetTile(__instance.Source).improvement.type != ImprovementData.Type.City)
        {
            return;
        }
        int happiness = getHappiness(__instance.Source, state);
        if (happiness >= HAPPY_CITY_THRESHOLD)
        {
            __instance.Amount = (int)(__instance.Amount * 1.25);
        }
        return;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TileDataExtensions), nameof(TileDataExtensions.CalculateWork), typeof(TileData), typeof(GameState), typeof(int))]
    public static void RebellionNoBusiness(this TileData tile, GameState gameState, int improvementLevel, ref int __result)
    {
        foreach (var tile2 in gameState.Map.GetTileNeighbors(tile.coordinates))
        {
            if (tile2.unit != null && (tile2.unit.type == UnitData.Type.Dagger || tile2.unit.type == UnitData.Type.Pirate) && tile2.unit.owner == 255)
            {
                __result = 0;
                return;
            }
        }
    }


    public static void SpawnRebellion(WorldCoordinates coords, GameState gameState, int severity)
    {
        int amount = severity > MIN_DAGGERS ? severity : MIN_DAGGERS;
        amount = amount > MAX_DAGGERS ? MAX_DAGGERS : amount;
        bool didRebellion = false;
        TileData city = gameState.Map.GetTile(coords);

        foreach (var tile in ActionUtils.GetCityArea(gameState, city))
        {
            System.Random rnd = new System.Random();
            if (tile.unit == null && (rnd.Next(0, 2) == 1 || amount > 4) && amount >= 1)
            {
                if (!tile.IsWater) gameState.ActionStack.Add(new TrainAction(255, UnitData.Type.Dagger, tile.coordinates, 0));
                else gameState.ActionStack.Add(new TrainAction(255, UnitData.Type.Pirate, tile.coordinates, 0));
                didRebellion = true;
                amount--;
            }
        }
        if (amount > 0)
        {
            modLogger.LogMessage("Didn't spawn enough daggers");
        }
        if (!didRebellion)
        {
            modLogger.LogMessage("A rebellion should've occured but did not");
            return;
        }

        if (city.owner == GameManager.LocalPlayer.Id)
        {
            BasicPopup popup = PopupManager.GetBasicPopup();
            popup.Header = Localization.Get("rebellion.header");
            popup.Description = Localization.Get("rebellion.description", new Il2CppSystem.Object[] { city.improvement.name });
            List<PopupBase.PopupButtonData> popupButtons = new()
            {
                new("buttons.ohno")
            };
            popup.buttonData = popupButtons.ToArray();
            popup.Show();
        }
        city.improvement.AddReward(EnumCache<CityReward>.GetType("stabilizer"));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StartTurnAction), nameof(StartTurnAction.ExecuteDefault))]
    public static void Rebellion(GameState gameState, StartTurnAction __instance)
    {
        CityTilesP.Clear();
        foreach (var tile in gameState.Map.Tiles)
        {
            if (tile != null && tile.improvement != null && tile.improvement.type == ImprovementData.Type.City)
            {
                CityTilesP.Add(tile);
                var city = tile.improvement;
                if (tile.owner == __instance.PlayerId && !tile.IsBeingCaptured(gameState))
                {
                    int happiness = getHappiness(tile.coordinates, gameState);
                    if (happiness <= 0)
                    {
                        System.Random rnd = new System.Random();
                        int chance = (1 - happiness) * 5;
                        if (rnd.Next(0, 101) < chance)
                        {
                            Main.SpawnRebellion(tile.coordinates, gameState, (-1) * happiness);
                        }
                    }
                }
            }
        }
    }
    #endregion

    #region Refreshing

    private static void ManualRefresh(WorldCoordinates coordinates)
    {
        MapRenderer.Current?.GetTileInstance(coordinates)?.improvement?.Cast<City>()?.cityOverlay?.progressBar?.UpdateFields();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]
    private static void founder(CaptureCityAction __instance, GameState gameState)
    {
        if (gameState.Map.GetTile(__instance.Coordinates).improvement?.founder == 0)
            gameState.Map.GetTile(__instance.Coordinates).improvement.founder = __instance.PlayerId;

        // if (__instance.PlayerId == GameManager.LocalPlayer.Id)
        //    ManualRefresh(__instance.Coordinates);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.SetTileAsCapital))]
    public static void capitalfounder(MapGenerator __instance, GameState gameState, PlayerState playerState, TileData tile)
    {
        tile.improvement.founder = playerState.Id;
    }

   /* [HarmonyPostfix]
    [HarmonyPatch(typeof(CaptureCityReaction), nameof(CaptureCityReaction.Execute))]
    public static void refreshuponcapture(CaptureCityReaction __instance, Il2CppSystem.Action onComplete)
    {
        if(__instance.action.PlayerId == GameManager.LocalPlayer.Id)
            ManualRefresh(__instance.action.Coordinates);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MoveCommand), nameof(MoveCommand.Execute))]
    private static void refreshupongarrisonchange(MoveCommand __instance, GameState gameState)
    {
        TileData tile = gameState.Map.GetTile(__instance.From);
        TileData tile1 = gameState.Map.GetTile(__instance.To);
        if (__instance.PlayerId != GameManager.LocalPlayer.Id) return;
        if (tile.improvement != null && tile.improvement.type == ImprovementData.Type.City)
            ManualRefresh(tile.coordinates);
        if (tile1.improvement != null && tile1.improvement.type == ImprovementData.Type.City)
            ManualRefresh(tile1.coordinates);
    }*/

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActionManager), nameof(ActionManager.ExecuteCommand))]
    public static void Refresh(ActionManager __instance, CommandBase command, string error)
    {
        
        if(GameManager.GameState == null) return;
        foreach(var item in CityTilesP)
        {
            if(item == null || item.improvement == null || item.owner != command.PlayerId || item.improvement.type != ImprovementData.Type.City) continue;
            ManualRefresh(item.coordinates);
        }
    }

    #endregion


}
