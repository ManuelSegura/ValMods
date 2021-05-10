using System;
using HarmonyLib;
using Areas.NetCode;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using static CharacterDrop;

namespace Areas.Patches
{

    [HarmonyPatch]
    public static class Patches
    {

        // ----------------------------------------------------------------------------------------------------------------------------------- LANGUAGE CHANGE
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
        public static void Localization_SetupLanguage_Post(Localization __instance, ref string language)
        {

            VariantsHandler.OnLanguageChanged(language);

        }

        // ----------------------------------------------------------------------------------------------------------------------------------- PLAYER
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        public static void Player_OnSpawned(Player __instance)
        {

            Main.GLog.LogInfo($"ZoneLookup try Start because Player.OnSpawned");
            AreaHandler.ZoneLookup_Start();

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.OnRespawn))]
        public static void Player_OnRespawn(Player __instance)
        {

            Main.GLog.LogInfo($"ZoneLookup try Start because Player.OnRespawn");
            AreaHandler.ZoneLookup_Start();

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        public static void Player_OnDeath(Player __instance)
        {

            Main.GLog.LogInfo($"ZoneLookup try Stop because Player.OnDeath");
            AreaHandler.ZoneLookup_Stop();

        }


        // ----------------------------------------------------------------------------------------------------------------------------------- DUNGEONS
        [HarmonyPostfix]
        [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.Awake))]
        private static void DungeonGenerator_Awake_Post(DungeonGenerator __instance)
        {

            if (!Globals.Config.DungeonRegenEnable.Value) return;
            if (!Globals.Config.DungeonRegenAllowedThemes.Value.Contains(__instance.m_themes.ToString())) return;

            if (__instance.GetRegenRemainder() <= 0L)
                DungeonHandler.Task_Schedule(__instance, 10L);
            else
                DungeonHandler.Task_Schedule(__instance);

        }


        // ----------------------------------------------------------------------------------------------------------------------------------- RCP STUFF
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        // Register RPC calls as soon as possible
        public static void Game_Start_Post(Game __instance)
        {

            ZRoutedRpc.instance.Register<ZPackage>("Areas.SetDataValues", new Action<long, ZPackage>(RPC.SetDataValues));

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        // When opening/entering a world, set if I should be using local or remote data
        public static void ZNet_Awake_Post(ZNet __instance)
        {

            ZNetType type = __instance.GetInstanceType();

            Main.GLog.LogInfo($"Instance is \"{type.ToString()}\"");

            switch (type)
            {
                case ZNetType.Local:
                case ZNetType.Server:
                    Main.LoadData(EDS.Local);
                    break;

                case ZNetType.Client:
                    break;

                default: break;
            }

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
        public static void ZNet_OnDestroy_Post(ZNet __instance)
        {

            Main.ResetData(EDS.Current);

            DungeonHandler.Task_UnscheduleAll();

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_CharacterID))]
        // When receiving a client, send data
        public static void ZNet_RPCCharacterID_Post(ZNet __instance, ZRpc rpc, ZDOID characterID)
        {

            if (__instance.GetInstanceType() == ZNetType.Client) return;

            long client = __instance.GetPeer(rpc).m_uid;

            Main.GLog.LogInfo($"Instance is sending Data to client \"{client}\"");
            RPC.SendDataToClient(client);

        }


        // ----------------------------------------------------------------------------------------------------------------------------------- CRITTER REGISTER
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
        public static void Character_Awake_Post(Character __instance)
        {

            if (__instance.IsPlayer()) return;
            if (CritterHandler.CheckedCritters.Contains(__instance.transform)) return;

            CritterHandler.CheckedCritters.Add(__instance.transform);

        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.OnDestroy))]
        public static void Character_OnDeath_Pre(Character __instance)
        {

            if (__instance.IsPlayer()) return;
            CritterHandler.CheckedCritters.Remove(__instance.transform);
            CritterHandler.CheckedCritters.RemoveWhere(a => a == null);

        }


        // ----------------------------------------------------------------------------------------------------------------------------------- CRITTER DAMAGE
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static void Character_Damage_Pre(Character __instance, HitData hit)
        {

            Character attacker = hit.GetAttacker();
            if (attacker == null) return;

            float multi = attacker.GetDamageMulti();
            hit.ApplyModifier(multi);

        }


        // ----------------------------------------------------------------------------------------------------------------------------------- CRITTER EVOLUTIONS
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.SetupLevelVisualization))]
        public static void LevelEffects_SetupLevelVisualization_Pre(LevelEffects __instance, ref int level)
        {

            CritterHandler.Apply_CT_Evolution(__instance.m_character);

        }


        // ----------------------------------------------------------------------------------------------------------------------------------- CRITTER LOOT
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        public static void CharacterDrop_GenerateDropList_Post(CharacterDrop __instance, ref List<KeyValuePair<GameObject, int>> __result)
        {

            if (!Globals.Config.LootEnable.Value) return;

            List<KeyValuePair<GameObject, int>> list = new List<KeyValuePair<GameObject, int>>();

            int lvlReward = 1;

            if (__instance.m_character)
            {
                int lvl = __instance.m_character.GetLevel();

                if (lvl <= 3)
                    lvlReward = Mathf.FloorToInt(Mathf.Max(1, 2 * (lvl - 1)));
                else
                {
                    int adjustLvl = Mathf.FloorToInt((1 / Globals.Config.LootFix.Value) * lvl + 3);
                    lvlReward = Mathf.FloorToInt(Mathf.Max(1, 2 * (adjustLvl - 1)));
                }

                Main.GLog.LogInfo($"Calculating DropList of \"{__instance.m_character.GetCleanName()}\" at \"{lvl}\" ");
            }

            foreach (Drop drop in __instance.m_drops)
            {
                if (drop.m_prefab == null) continue;

                float dropChance = drop.m_chance;

                if (drop.m_levelMultiplier) dropChance *= (float)lvlReward;

                if (UnityEngine.Random.value <= dropChance)
                {
                    int amount = UnityEngine.Random.Range(drop.m_amountMin, drop.m_amountMax);
                    if (drop.m_levelMultiplier) amount *= lvlReward;
                    if (drop.m_onePerPlayer) amount = ZNet.instance.GetNrOfPlayers();
                    if (amount > 0) list.Add(new KeyValuePair<GameObject, int>(drop.m_prefab, amount));
                }
            }

            __result = list;

        }


        // ----------------------------------------------------------------------------------------------------------------------------------- CRITTER CAPTURE
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Spawn))]
        private static IEnumerable<CodeInstruction> SpawnSystem_Spawn_Transpiler(IEnumerable<CodeInstruction> instructions)
        {

            List<CodeInstruction> ins = new List<CodeInstruction>(instructions);
            List<CodeInstruction> codes = new List<CodeInstruction>();

            for (var i = 0; i < ins.Count; i++)
            {
                MethodInfo method = ins[i].operand as MethodInfo;
                string str = method?.Name;

                if (str == "Instantiate")
                {
                    // i   = call to instantiate
                    codes.Add(ins[i]);
                    // i+1 = stloc.0 storages gameobject in stloc.0
                    codes.Add(ins[i + 1]);
                    // i+2 = ldloc.0 loads from stloc.0 which should be the gameobject
                    codes.Add(new CodeInstruction(OpCodes.Ldloc_0));
                    // i+3 = call the modify critter info where the first arg is ldloc.0
                    codes.Add(new CodeInstruction(OpCodes.Call, CritterHandler.CT_SetHolderInfo));

                    // this jumps i+1 because i'm already adding it
                    i++;
                    // this jumps i because i'm already adding it
                    continue;
                }

                codes.Add(ins[i]);

            }

            return codes;

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Spawn))]
        public static void SpawnSystem_Spawn_Post(SpawnSystem __instance) { CritterHandler.ModifyHolden(); }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Spawn))]
        private static IEnumerable<CodeInstruction> CreatureSpawner_Spawn_Transpiler(IEnumerable<CodeInstruction> instructions)
        {

            List<CodeInstruction> ins = new List<CodeInstruction>(instructions);
            List<CodeInstruction> codes = new List<CodeInstruction>();

            for (var i = 0; i < ins.Count; i++)
            {
                MethodInfo method = ins[i].operand as MethodInfo;
                string str = method?.Name;

                if (str == "Instantiate")
                {
                    // i   = call to instantiate
                    codes.Add(ins[i]);
                    // i+1 = stloc.3 storages gameobject in stloc.3
                    codes.Add(ins[i + 1]);
                    // i+2 = ldloc.3 loads from stloc.3 which should be the gameobject
                    codes.Add(new CodeInstruction(OpCodes.Ldloc_3));
                    // i+3 = call the modify critter info where the first arg is ldloc.3
                    codes.Add(new CodeInstruction(OpCodes.Call, CritterHandler.CT_SetHolderInfo));

                    // this jumps i+1 because i'm already adding it
                    i++;
                    // this jumps i because i'm already adding it
                    continue;
                }

                codes.Add(ins[i]);

            }

            return codes;

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Spawn))]
        public static void CreatureSpawner_Spawn_Post(CreatureSpawner __instance) { CritterHandler.ModifyHolden(); }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(SpawnArea), nameof(SpawnArea.SpawnOne))]
        private static IEnumerable<CodeInstruction> SpawnArea_SpawnOne_Transpiler(IEnumerable<CodeInstruction> instructions)
        {

            List<CodeInstruction> ins = new List<CodeInstruction>(instructions);
            List<CodeInstruction> codes = new List<CodeInstruction>();

            for (var i = 0; i < ins.Count; i++)
            {
                MethodInfo method = ins[i].operand as MethodInfo;
                string str = method?.Name;

                if (str == "Instantiate")
                {
                    // i   = call to instantiate
                    codes.Add(ins[i]);
                    // i+1 = stloc.s V_4 storages gameobject in stloc.s V_4
                    codes.Add(ins[i + 1]);
                    // i+2 = ldloc.s V_4 loads from stloc.s V_4 which should be the gameobject
                    codes.Add(new CodeInstruction(OpCodes.Ldloc_S, 4));
                    // i+3 = call the modify critter info where the first arg is ldloc.s V_4
                    codes.Add(new CodeInstruction(OpCodes.Call, CritterHandler.CT_SetHolderInfo));

                    // this jumps i+1 because i'm already adding it
                    i++;
                    // this jumps i because i'm already adding it
                    continue;
                }

                codes.Add(ins[i]);

            }

            return codes;

        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SpawnArea), nameof(SpawnArea.SpawnOne))]
        public static void SpawnArea_SpawnOne_Post(SpawnArea __instance) { CritterHandler.ModifyHolden(); }


        // ----------------------------------------------------------------------------------------------------------------------------------- RAGDOLL SETUP
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
        public static void Ragdoll_Setup_Post(Ragdoll __instance, CharacterDrop characterDrop)
        {

            if (characterDrop.m_character.IsPlayer()) return;

            LevelEffects levelEffects = characterDrop.GetComponentInChildren<LevelEffects>();
            var renderer = levelEffects?.m_mainRender ?? characterDrop.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer != null) __instance.m_mainModel.materials = new Material[] { renderer.material };

            __instance.transform.localScale = characterDrop.m_character.transform.localScale;

        }


        // ----------------------------------------------------------------------------------------------------------------------------------- MODIFY SPAWNERS
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Awake))]
        public static void SpawnSystem_Awake_Post(SpawnSystem __instance) { SpawnerHandler.Modify_SS(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Awake))]
        public static void CreatureSpawner_Awake_Post(CreatureSpawner __instance) { SpawnerHandler.Modify_CS(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SpawnArea), nameof(SpawnArea.Awake))]
        public static void SpawnArea_Awake_Post(SpawnArea __instance) { SpawnerHandler.Modify_SA(__instance); }

    }

}
