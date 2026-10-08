SandboxVars = {
    VERSION = 6,
    -- Controls the zombie population. Default = Normal
    -- 1 = Insane
    -- 2 = High
    -- 3 = Low
    -- 4 = Normal
    Zombies = 4,
    -- Applies randomized zombie distribution.
    ZombieVoronoiNoise = true,
    -- Extra points available during character creation. Min: -100 Max: 100 Default: 0
    CharacterFreePoints = 2,
    -- Comma-separated item types removed from the world. Default = ""
    LootItemRemovalList = "",
    -- Controls plant growth speed. Default = Normal
    -- 1 = Very Fast
    -- 2 = Fast
    -- 3 = Normal
    -- 4 = Slow
    -- 5 = Very Slow
    Farming = 3,
    -- Selects the water shutoff range. Default = 0 - 30 Days
    -- 1 = Instant
    -- 2 = 0 - 30 Days
    -- 3 = 0 - 60 Days
    WaterShut = 2,
    -- Effective water shutoff day. Min: -1 Max: 365 Default: 14
    WaterShutModifier = 14,
    -- Selects the electricity shutoff range. Default = 14 - 30 Days
    -- 1 = Instant
    -- 2 = 14 - 30 Days
    -- 3 = 14 - 60 Days
    ElecShut = 2,
    -- Effective electricity shutoff day. Min: -1 Max: 365 Default: 21
    ElecShutModifier = 21,
    -- Selects the alarm decay range. Default = 0 - 30 Days
    -- 1 = Instant
    -- 2 = 0 - 30 Days
    -- 3 = 0 - 60 Days
    AlarmDecay = 2,
    -- Effective alarm decay day. Min: -1 Max: 365 Default: 10
    AlarmDecayModifier = 10,
    -- Controls plant abundance. Default = Very Scarce
    -- 1 = Very Scarce
    -- 2 = Scarce
    -- 3 = Normal
    PlantAbundance = 1,
    -- Controls helicopter frequency. Default = Once
    -- 1 = Never
    -- 2 = Once
    -- 3 = Sometimes
    Helicopter = 2,
    -- Controls ambient event frequency. Default = Sometimes
    -- 1 = Never
    -- 2 = Sometimes
    -- 3 = Often
    MetaEvent = 2,
    -- Controls sleeping event frequency. Default = Sometimes
    -- 1 = Never
    -- 2 = Sometimes
    -- 3 = Often
    SleepingEvent = 2,
    -- Controls vehicle story chance. Default = Rare
    -- 1 = Never
    -- 2 = Very Rare
    -- 3 = Rare
    VehicleStoryChance = 3,
    -- Controls zone story chance. Default = Rare
    -- 1 = Never
    -- 2 = Very Rare
    -- 3 = Rare
    ZoneStoryChance = 3,
    -- Controls animal stats speed. Default = Ultra Fast
    -- 1 = Ultra Fast
    -- 2 = Normal
    AnimalStatsModifier = 1,
    -- Controls animal meta stats speed. Default = Ultra Fast
    -- 1 = Ultra Fast
    -- 2 = Normal
    AnimalMetaStatsModifier = 1,
    -- Controls animal pregnancy speed. Default = Ultra Fast
    -- 1 = Ultra Fast
    -- 2 = Normal
    AnimalPregnancyTime = 1,
    -- Controls animal aging speed. Default = Ultra Fast
    -- 1 = Ultra Fast
    -- 2 = Normal
    AnimalAgeModifier = 1,
    -- Controls milk production speed. Default = Ultra Fast
    -- 1 = Ultra Fast
    -- 2 = Normal
    AnimalMilkIncModifier = 1,
    -- Controls wool production speed. Default = Ultra Fast
    -- 1 = Ultra Fast
    -- 2 = Normal
    AnimalWoolIncModifier = 1,
    -- Controls egg hatching speed. Default = Ultra Fast
    -- 1 = Ultra Fast
    -- 2 = Normal
    AnimalEggHatch = 1,
    -- Controls animal track rarity. Default = Extremely Rare
    -- 1 = Never
    -- 2 = Extremely Rare
    -- 3 = Rare
    AnimalTrackChance = 2,
    -- Controls animal path rarity. Default = Extremely Rare
    -- 1 = Never
    -- 2 = Extremely Rare
    -- 3 = Rare
    AnimalPathChance = 2,
    Basement = {
        -- Controls basement spawn frequency. Default = Frequent
        -- 1 = Never
        -- 2 = Rare
        -- 3 = Normal
        -- 4 = Common
        -- 5 = Frequent
        SpawnFrequency = 5,
    },
    ZombieLore = {
        -- Controls zombie strength. Default = Normal
        -- 1 = Weak
        -- 2 = Normal
        -- 3 = Strong
        Strength = 2,
    },
    ZombieConfig = {
        -- Number of zombies removed by cleanup. Min: 0 Max: 1000 Default: 100
        ZombiesCountBeforeDelete = 100,
    },
    MultiplierConfig = {
        -- Farming skill progression multiplier. Min: 0 Max: 1000 Default: 1.00
        Farming = 3.0,
        -- Strength skill progression multiplier. Min: 0 Max: 1000 Default: 1.00
        Strength = 2.0,
        -- Global progression multiplier. Min: 0 Max: 1000 Default: 1.00
        Global = 3.0,
    },
    ExampleNpc = {
        -- Chance of a synthetic encounter. Min: 0 Max: 100 Default: 25
        EncounterChance = 40,
    },
}
