using BepInEx.Logging;
using HarmonyLib;
using Polytopia.Data;
using UnityEngine;


namespace happiness;

public static class Main
{
    private static Il2CppSystem.Collections.Generic.List<TileData> CityTilesP = new(); // So that entire map isnt checked every command

    public static ManualLogSource modLogger;
    public static void Load(ManualLogSource logger)
    {
        PolyMod.Loader.AddPatchDataType("customrewards", typeof(CityReward));
        Harmony.CreateAndPatchAll(typeof(Main));
        Harmony.CreateAndPatchAll(typeof(Connector));
        modLogger = logger;
        logger.LogMessage("City Happiness v1.1");
    }


    #region getHappiness
    public static int getHappiness(WorldCoordinates coordinates, GameState gameState)
    {
        TileData tile = gameState.Map.GetTile(coordinates);
        if (tile.improvement == null || tile.improvement.type != ImprovementData.Type.City)
        {
            modLogger.LogError("Not a city for coords " + coordinates);
            return int.MaxValue;
        }
        gameState.TryGetPlayer(tile.owner, out PlayerState player);

        int happiness = HappinessData.BASE_HAPPINESS; // Base value
        if (HappinessData.VerboseLog) Main.modLogger.LogMessage("---------- Evalling " + tile.improvement.name + ": " + HappinessData.BASE_HAPPINESS + " as base ---------------");


        /////////////////////////////
        ///  HAPPINESS
        ///  BOOSTERS
        /////////////////////////////
        if (player.GetCurrentCapitalCoordinates(gameState) == coordinates)
        {
            happiness += HappinessData.CAPITAL_HAPPINESS;
            if (HappinessData.VerboseLog) Main.modLogger.LogMessage("+" + HappinessData.CAPITAL_HAPPINESS + " from Capital");
        }
        else if (tile.improvement.connectedToCapitalOfPlayer == player.Id)
        {
            happiness += HappinessData.CONNECTION_HAPPINESS; // if connected to your capital
            if (HappinessData.VerboseLog) modLogger.LogMessage($"+{HappinessData.CONNECTION_HAPPINESS} from connection");
        }

        int naturecounter = 0;
        foreach (TileData item in ActionUtils.GetCityArea(gameState, tile))
        {
            if (item.improvement != null && Connector.dicthappiness.TryGetValue(item.improvement.type, out int bonus))
            {
                happiness += bonus;
                if (HappinessData.VerboseLog) modLogger.LogMessage("+" + bonus + " from improvement: "+item.improvement.type.ToString());
                if(item.improvement.IsTemple() && HappinessData.ObstructedTempleRule && item.unit != null && item.unit.owner != item.owner && !player.HasPeaceWith(item.unit.owner))
                {
                    happiness -= bonus;
                    if(HappinessData.VerboseLog) modLogger.LogMessage("Nevermind, temple is obstructed by enemy unit");
                }
            }
            if (item.improvement == null)
            {
                naturecounter++;
            }
            if (item.unit != null && item.unit.owner != item.owner && item.unit.HasAbility(UnitAbility.Type.Convert) && !player.HasPeaceWith(item.unit.owner))
            {
                happiness += HappinessData.MINDBENDER_HAPPINESS;
                if (HappinessData.VerboseLog) modLogger.LogMessage($"{HappinessData.MINDBENDER_HAPPINESS} from mindbender");
            }
        }
        happiness += (int)(naturecounter / HappinessData.NATURE_DIVIDER); //+1 happiness for each 5 unimproved tile
        if (HappinessData.VerboseLog) modLogger.LogMessage("+" + naturecounter / HappinessData.NATURE_DIVIDER + " from nature");

        //+1 happiness if city has unit on it
        if (tile.unit != null && tile.unit.owner == tile.owner)
        {
            happiness += HappinessData.GARRISON_HAPPINESS;
            if (HappinessData.VerboseLog) modLogger.LogMessage($"+{HappinessData.GARRISON_HAPPINESS} from garrison");
        }

        //+1 for each park (you can negate population growth malus)
        //+1 for each rebellion (let's go easy on the player shall we)
        foreach (var item in tile.improvement.rewards)
        {
            if (item == CityReward.PopulationGrowth) {happiness += HappinessData.POPGROWTH_HAPPINESS; if(HappinessData.VerboseLog) modLogger.LogMessage($"+ {HappinessData.POPGROWTH_HAPPINESS} from Population Growth");}
            if (item == CityReward.Park) { happiness += HappinessData.PARK_HAPPINESS; if (HappinessData.VerboseLog) modLogger.LogMessage($"+{HappinessData.PARK_HAPPINESS} from park"); }
            if (item == EnumCache<CityReward>.GetType("stabilizer")) { happiness += HappinessData.STABILIZER_HAPPINESS; if (HappinessData.VerboseLog) modLogger.LogMessage($"+{HappinessData.STABILIZER_HAPPINESS} from stabilizer"); }
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
            if (HappinessData.VerboseLog) modLogger.LogMessage("-" + (tile.improvement.level - 2) + " from crowdedness");
        }

        if (tile.improvement.founder != tile.owner)
        {
            happiness -= 2;
            if (HappinessData.VerboseLog) modLogger.LogMessage("-2 from not being the founder");
        }


        ////////////////////
        /// CUSTOM MODIFIERS
        ////////////////////
        if (Connector.modifiers.Count > 0)
            foreach (var mod in Connector.modifiers)
            {
                try
                {
                    happiness += mod(coordinates, gameState);
                    if (HappinessData.VerboseLog) modLogger.LogMessage("Changed it by " + mod(coordinates, gameState));
                }
                catch (Exception ex)
                {
                    modLogger.LogError($"Happiness modifier failed: {ex}");
                }
            }

        if (HappinessData.VerboseLog) modLogger.LogMessage("Final: " + happiness + "\n---------------");
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
                if (happiness >= HappinessData.HAPPY_CITY_THRESHOLD) __result += Localization.Get("info.happycity");
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
        int total = basis + HappinessData.HAPPINESS_SEGMENTS; // End of the happiness segments
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
        if (happiness >= HappinessData.HAPPY_CITY_THRESHOLD)
        {
            work = (int)(work * HappinessData.getBoostMultiplier());
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
        if (happiness >= HappinessData.HAPPY_CITY_THRESHOLD)
        {
            __instance.Amount = (int)(__instance.Amount * HappinessData.getBoostMultiplier());
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
        int amount = severity > HappinessData.MIN_DAGGERS ? severity : HappinessData.MIN_DAGGERS;
        amount = amount > HappinessData.MAX_DAGGERS ? HappinessData.MAX_DAGGERS : amount;
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
                        int chance = (1 - happiness) * HappinessData.REBELLION_PERCENTAGE;
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
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.SetTileAsCapital))]
    private static void capitalfounder(MapGenerator __instance, GameState gameState, PlayerState playerState, TileData tile)
    {
        tile.improvement.founder = playerState.Id;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActionManager), nameof(ActionManager.ExecuteCommand))]
    private static void Refresh(ActionManager __instance, CommandBase command, string error)
    {

        if (GameManager.GameState == null) return;
        foreach (var item in CityTilesP)
        {
            if (item == null || item.improvement == null || item.owner != command.PlayerId || item.improvement.type != ImprovementData.Type.City) continue;
            ManualRefresh(item.coordinates);
        }
    }

    #endregion


}
