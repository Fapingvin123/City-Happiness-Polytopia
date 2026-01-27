using BepInEx.Logging;
using HarmonyLib;
using Polytopia.Data;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Il2CppSystem.Linq;


namespace test;

public static class Main
{

    
    public static ManualLogSource modLogger;
    public static void Load(ManualLogSource logger)
    {
        PolyMod.Loader.AddPatchDataType("customrewards", typeof(CityReward));
        Harmony.CreateAndPatchAll(typeof(Main));
        modLogger = logger;
        logger.LogMessage("Happiness.dll");

    }


    #region Happiness
    public static int getHappiness(WorldCoordinates coordinates, GameState state)
    {
        TileData tile = state.Map.GetTile(coordinates);
        if (tile.improvement == null || tile.improvement.type != ImprovementData.Type.City)
        {
            modLogger.LogError("Not a city for coords " + coordinates);
            return -1;
        }

        PlayerState player;
        state.TryGetPlayer(tile.owner, out player);
        int happiness = 2;


        /////////////////////////////
        ///  HAPPINESS
        ///  BOOSTERS
        /////////////////////////////
        if (player.GetCurrentCapitalCoordinates(state) == coordinates)
        {
            happiness += 3;
        }
        else if (tile.improvement.connectedToCapitalOfPlayer == player.Id)
        {
            happiness += 1; // +1 if connected to your capital
        }

        int naturecounter = 0;
        foreach (TileData item in ActionUtils.GetCityArea(state, tile))
        {
            if (item.improvement != null && item.improvement.IsMonument())
            {
                happiness += 2; // +2 happiness for each monument
                if (item.improvement.type == ImprovementData.Type.Monument1) happiness += 2; // Additional +2 for Altar of Peace
            }
            if (item.improvement != null && item.improvement.IsTemple())
            {
                happiness += 1; // +1 Happiness for each temple
            }
            if (item.improvement != null && dicthappiness.TryGetValue(item.improvement.type, out int bonus))
            {
                happiness += bonus;
            }
            if (item.improvement == null)
            {
                naturecounter++;
            }
            if(item.unit != null && item.unit.owner != item.owner && item.unit.type == UnitData.Type.MindBender && !player.HasPeaceWith(item.unit.owner))
            {
                happiness -= 1;
            }
        }
        happiness += (int)(naturecounter / 5); //+1 happiness for each 5 unimproved tile

        //+1 happiness if city has unit with at least 4 (attack+defense)
        if (tile.unit != null && (tile.unit.GetAttack(state) + tile.unit.GetDefence(state)) >= 4) happiness += 1;

        //+1 for each park (you can negate population growth malus)
        //+1 for each rebellion (let's go easy on the player shall we)
        foreach (var item in tile.improvement.rewards)
        {
            if (item == CityReward.Park) happiness += 1;
            if (item == EnumCache<CityReward>.GetType("stabilizer")) happiness += 1;
        }




        /////////////////////////////
        ///  HAPPINESS
        ///  DRAINERS
        /////////////////////////////

        if (tile.IsBeingCaptured(state)) happiness = -100; //Low happiness but city won't revolt while under siege)
        //Every population level takes one after the first one
        if (tile.improvement.level > 2)
        {
            happiness -= (tile.improvement.level - 2);
        }

        if (tile.improvement.founder != tile.owner)
        {
            happiness -= 2;
        }


        return happiness;

    }
    #endregion


    #region UI
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

    

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CityStatusProgressBar), nameof(CityStatusProgressBar.UpdateFields))]
    public static void newbar(CityStatusProgressBar __instance)
    {
        int basis = __instance.segments.Count;
        int total = basis + 5;
        float num = Mathf.Min(__instance.minWidth + (float)(total - basis - 2) * __instance.centerFieldWidth, __instance.maxWidth);

        var display = getDisplay(__instance);
        if (display == null) return;
        TileData tile = GameManager.GameState.Map.GetTile(Main.findFromDisplay(display));
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

        if (tile.owner == GameManager.LocalPlayer.Id)
        {
            int work = display.city.Tile.Data.CalculateWork(GameManager.GameState);
            if (happiness > 4)
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
        }

        // Sixth segment that stores the icon
        CityStatusProgressSegment segment;

        segment = UnityEngine.Object.Instantiate<CityStatusProgressSegment>(__instance.segmentPrefab, __instance.transform, false);
        __instance.segments.Add(segment);
        float num4 = num / (float)(total - basis);
        segment.SetWidth(0.01f);
        segment.SetType(CityStatusProgressSegment.Type.Left);
        segment.transform.localPosition = new Vector3(-0.5f, -0.15f, 0f);
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
    public static bool HappyProduction(IncreaseCurrencyAction __instance, GameState state)
    {
        if (state.Map.GetTile(__instance.Source).improvement == null || state.Map.GetTile(__instance.Source).improvement.type != ImprovementData.Type.City)
        {
            return true;
        }
        int happiness = getHappiness(__instance.Source, state);
        if (happiness > 4)
        {
            __instance.Amount = (int)(__instance.Amount * 1.25);
        }
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TileDataExtensions), nameof(TileDataExtensions.CalculateWork), typeof(TileData), typeof(GameState), typeof(int))]
    public static void RebellionNoBusiness(this TileData tile, GameState gameState, int improvementLevel, ref int __result)
    {
        foreach(var tile2 in gameState.Map.GetTileNeighbors(tile.coordinates))
        {
            if(tile2.unit != null && (tile2.unit.type == UnitData.Type.Dagger || tile2.unit.type == UnitData.Type.Pirate) && tile2.unit.owner == 255)
            {
                __result = 0;
                return;
            }
        }
    }

    public static void SpawnRebellion(WorldCoordinates coords, GameState gameState, int severity)
    {
        int amount = severity > 2 ? severity : 2;
        amount = amount > 6 ? 6 : amount;
        bool didRebellion = false;
        TileData city = gameState.Map.GetTile(coords);

        foreach (var tile in ActionUtils.GetCityArea(gameState, city))
        {
            System.Random rnd = new System.Random();
            if (tile.unit == null && (rnd.Next(0, 2) == 1 || amount > 4) && amount >= 1)
            {
                gameState.ActionStack.Add(new TrainAction(255, UnitData.Type.Dagger, tile.coordinates, 0));
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
            popup.Header = "REBELLION!";
            popup.Description = "There happened a bloody rebellion in the city of " + city.improvement.name + "! Due to general unhappiness and disloyalty, your people said enough is enough, and now daggers are roaming the city's countryside!\n\nCrush the rebels!";
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
        foreach (var tile in gameState.Map.Tiles)
        {
            if (tile != null && tile.improvement != null && tile.improvement.type == ImprovementData.Type.City)
            {
                var city = tile.improvement;
                if (tile.owner == __instance.PlayerId && !tile.IsBeingCaptured(gameState))
                {
                    int happiness = getHappiness(tile.coordinates, gameState);
                    if (happiness < 0)
                    {
                        System.Random rnd = new System.Random();
                        int chance = (1 - happiness) * 5;
                        if (rnd.Next(0, 101) < chance)
                        {
                            Main.SpawnRebellion(tile.coordinates, gameState, -happiness);
                        }
                    }
                }
            }
        }
    }
    #endregion

    #region misc

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]
    public static void refreshuponcapture(CaptureCityAction __instance, GameState gameState)
    {
        if (__instance.PlayerId == GameManager.LocalPlayer.Id)
            MapRenderer.Current.GetTileInstance(__instance.Coordinates).improvement.Cast<City>().cityOverlay.progressBar.UpdateFields();
    }

    #endregion

    #region Parsing

    public static Dictionary<ImprovementData.Type, int> dicthappiness = new Dictionary<ImprovementData.Type, int>();

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
                        modLogger.LogInfo($"Added {amount} amount to {impType} in happiness");
                    }
                }
            }
        }
    }
    
    #endregion
}
