// DreamGate
// Modifies Vegvisir to become a gate back to your spawn spot (normally a bed) and disables marking of boss locations.
// Encourages exploring while giving players an occasionally useful and quick way one-way trip back home.
// 
// File:    DreamGate.cs
// Project: DreamGate

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;

namespace DreamGate
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class DreamGate : BaseUnityPlugin
    {
        public const string PluginGUID = "papajin68.dreamgate";
        public const string PluginName = "DreamGate";
        public const string PluginVersion = "1.0.1";

        private static bool playerIsDreaming = false;
        private static string sleepText = "ZZZZZzzzz...";
        private static string originalPinName;
        private static string originalName;

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        internal static Harmony _harmony;

        private static ConfigEntry<bool> _allowBossMark;
        private static ConfigEntry<bool> _allowTeleport;

        private void Awake()
        {
            Config.SaveOnConfigSet = true;

            _allowBossMark = Config.Bind("General", "AllowBossMark", false,
                new ConfigDescription("Allow marking of boss on map when clicking on Dream Gate", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _allowTeleport = Config.Bind("General", "AllowTeleport", true,
                new ConfigDescription("Allow one-way teleport to active player bed when clicking on Dream Gate", null,
                new ConfigurationManagerAttributes { IsAdminOnly = true }));

            _harmony = new Harmony(Info.Metadata.GUID);
            _harmony.PatchAll();

            Localization.AddTranslation("English", new Dictionary<string, string>
            {
                {"piece_register_location", "Touch Stone"},
                {"msg_goodmorning", "You wake up still tired and feeling vaguely uneasy."}
            });

            Jotunn.Logger.LogInfo("Loaded successfully.");
        }

        [HarmonyPatch(typeof(Vegvisir), nameof(Vegvisir.GetHoverText))]
        class p68_VegvisirHoverPatch
        {
            static void Prefix(Vegvisir __instance, ref string ___m_name, ref string ___m_pinName)
            {
                if (originalPinName.IsNullOrWhiteSpace())
                {
                    originalPinName = ___m_pinName;
                }

                if (originalName.IsNullOrWhiteSpace())
                {
                    originalName = ___m_name;
                }

                ___m_pinName = originalPinName;

                if (_allowTeleport.Value)
                {
                    ___m_name = "Dream Gate:";

                    Player localPlayer = Player.m_localPlayer;
                    if (localPlayer.IsTeleportable())
                    {
                        ___m_pinName += " - The stone hums with energy.";
                    }
                    else
                    {
                        ___m_pinName += " - The stone hums faintly but appears inactive.";
                    }
                }
                else
                {
                    ___m_name = originalName;
                }
            }
        }

        [HarmonyPatch(typeof(Vegvisir), nameof(Vegvisir.Interact))]
        class p68_VegvisirInteractPatch
        {
            static bool Prefix(Vegvisir __instance, bool hold, ref string ___m_pinName)
            {
                Player lp = Player.m_localPlayer;
                if (hold)
                {
                    return false;
                }

                if (_allowTeleport.Value)
                {
                    if (!lp.IsTeleportable())
                    {
                        lp.Message(MessageHud.MessageType.Center, "The Dream Gate is unable to activate.");
                    }
                    else
                    {
                        if ((bool)lp)
                        {
                            Jotunn.Logger.LogInfo($"The player begins to dream.");
                            Game.instance.StartCoroutine(p68_FindPlayerBed());
                        }
                    }
                }

                if (!_allowBossMark.Value)
                {
                    if (!_allowTeleport.Value)
                    {
                        lp.Message(MessageHud.MessageType.Center, "The Vegvisir is inactive and does nothing.");
                    }
                    return false;
                }

                ___m_pinName = originalPinName;
                return true;
            }
        }

        [HarmonyPatch(typeof(SleepText), nameof(SleepText.ShowDreamText))]
        class p68_SleepTextShowDreamTextPatch
        {
            static void Prefix(ref DreamTexts ___m_dreamTexts)
            {
                foreach (DreamTexts.DreamText dreamText in ___m_dreamTexts.m_texts)
                {
                    dreamText.m_chanceToDream = 1.0f;
                }
            }
        }

        [HarmonyPatch(typeof(SleepText), nameof(SleepText.OnEnable))]
        class p68_SleepTextOnEnablePatch
        {
            static void Prefix(ref Text ___m_textField)
            {
                sleepText = ___m_textField.text;
                if (playerIsDreaming)
                {
                    ___m_textField.text = "You fall into a restless sleep full of dreams...";
                }
            }
        }

        [HarmonyPatch(typeof(SleepText), nameof(SleepText.HideDreamText))]
        class p68_SleepTextHideDreamTextPatch
        {
           static void Postfix(ref Text ___m_textField)
            {
                ___m_textField.text = sleepText;
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateBlackScreen))]
        class p68_HudUpdateBlackScreenPatch
        {
            static void Prefix(ref UnityEngine.CanvasGroup ___m_loadingScreen)
            {
                if (playerIsDreaming)
                {
                    ___m_loadingScreen.alpha = 1f;
                }
            }
        }

        [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
        class p68_MessageHudShowMessagePatch
        {
            static bool Prefix(MessageHud __instance, MessageHud.MessageType type, string text, int amount = 0, Sprite icon = null)
            {
                if (playerIsDreaming && (text == "$se_shelter_start" || text == "$se_resting_start"))
                {
                    Jotunn.Logger.LogDebug($"MessageHud: {text}");
                    return false;
                }

                return true;
            }
        }

        private static IEnumerator p68_FindPlayerBed()
        {
            var sw = new Stopwatch();
            sw.Start();

            Game g = Game.instance;
            Player lp = Player.m_localPlayer;
            PlayerProfile pp = g.GetPlayerProfile();
            Transform spawn = null;

            playerIsDreaming = true;
            lp.SetSleeping(sleep: true);

            if (pp.HaveCustomSpawnPoint())
            {
                Vector3 csp = pp.GetCustomSpawnPoint();
                ZNet.instance.SetReferencePosition(csp);

                lp.TeleportTo(csp, lp.transform.rotation, distantTeleport: false);
                yield return new WaitUntil(() => ZNetScene.instance.IsAreaReady(csp));

                Bed bed = g.FindBedNearby(csp, 1f);
                if (bed != null)
                {
                    // Bed is found.  Set spawn to bed transform
                    Vector3 bp = bed.m_spawnPoint.position;
                    Jotunn.Logger.LogDebug($"Player owned bed found at custom spawn point.");
                    spawn = lp.transform;
                    spawn = bed.m_spawnPoint;
                }
                else
                {
                    // Bed wasn't found where expected.  Clear custom spawn from PlayerProfile and spawn at Start
                    pp.ClearCustomSpawnPoint();
                    //g.m_respawnWait = 0f;
                    Jotunn.Logger.LogDebug($"Player bed NOT found at custom spawn point.");
                }
            }

            if (spawn is null)
            {
                if (pp.HaveCustomSpawnPoint())
                {
                    Jotunn.Logger.LogDebug($"Player doesn't have custom spawn point set.");
                }

                spawn = g.transform;

                if (ZoneSystem.instance.GetLocationIcon(g.m_StartLocation, out var sp))
                {
                    // Player doesn't have a custom spawn point.  Spawn at Start
                    spawn.position = sp + Vector3.up * .2f;
                    Jotunn.Logger.LogDebug($"Spawn point not found, returning player to starting location.");
                }
                else
                {
                    //Unable to find start location!  Defaulting to pos(0, 0, 0)!  This generally shouldn't happen!
                    Vector3 zp = Vector3.zero;
                    spawn.position = zp;
                    Jotunn.Logger.LogDebug($"Spawn not found!  Spawning player at (0, 0, 0)!");
                }
            }

            ZNet.instance.SetReferencePosition(spawn.position);
            lp.TeleportTo(spawn.position, spawn.rotation, distantTeleport: false);
            Jotunn.Logger.LogDebug($"Spawning player at ({spawn.position.x},{spawn.position.y},{spawn.position.z}).");

            lp.m_attachPoint = spawn;
            lp.m_detachOffset = new Vector3(0f, 0.5f, 0f);
            lp.m_attached = true;
            lp.m_attachAnimation = "attach_bed";
            lp.m_zanim.SetBool("attach_bed", value: true);

            lp.HideHandItems();
            lp.ResetCloth();

            sw.Stop();
            Jotunn.Logger.LogDebug($"Elapsed: {sw.ElapsedMilliseconds / 1000f} seconds");
            yield return new WaitForSeconds(14f - sw.ElapsedMilliseconds / 1000f);

            Jotunn.Logger.LogInfo("The player awakes.");

            playerIsDreaming = false;
            lp.SetSleeping(false);

            if (lp.m_attached)
            {
                if (lp.m_attachPoint != null)
                {
                    lp.transform.position = lp.m_attachPoint.TransformPoint(lp.m_detachOffset);
                }

                lp.m_sleeping = false;
                lp.m_body.useGravity = true;
                lp.m_attached = false;
                lp.m_attachPoint = null;
                lp.m_zanim.SetBool(lp.m_attachAnimation, value: false);
                lp.m_attachAnimation = "";
                lp.ResetCloth();
            }
        }
    }
}