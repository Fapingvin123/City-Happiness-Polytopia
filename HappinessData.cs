namespace happiness;

public static class HappinessData
{
    public static int HAPPY_CITY_THRESHOLD = 5;
    public static int HAPPINESS_SEGMENTS = 5;
    public static int MIN_DAGGERS = 2;
    public static int MAX_DAGGERS = 6;
    public static int BASE_HAPPINESS = 2;
    public static int CAPITAL_HAPPINESS = 3;
    public static int CONNECTION_HAPPINESS = 1;
    public static int MINDBENDER_HAPPINESS = -1;
    public static int NATURE_DIVIDER = 5;
    public static int GARRISON_HAPPINESS = 1;
    public static int HAPPINESS_BOOST_PERCENTAGE = 25;
    public static int REBELLION_PERCENTAGE = 5;
    public static int PARK_HAPPINESS = 1;
    public static int STABILIZER_HAPPINESS = 1;
    public static int POPGROWTH_HAPPINESS = 1;
    public static bool VerboseLog = false;
    public static bool ObstructedTempleRule = true;

    public static float getBoostMultiplier()
    {
        float value = 1 + (HAPPINESS_BOOST_PERCENTAGE / 100f);
        if(VerboseLog) Main.modLogger.LogMessage("Boost multiplier: "+value);
        return value;
    }
}