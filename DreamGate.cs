﻿// DreamGate
// Modifies Vegvisir to become a gate back to your spawn spot (normally a bed) and disables marking of boss locations.
// Encourages exploring while giving players an occasionally useful and quick way one-way trip back home.
// 
// File:    DreamGate.cs
// Project: DreamGate

using BepInEx;
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
        public const string PluginVersion = "1.0.0";

        private static bool playerIsDreaming = false;
        private static string sleepText = "ZZZZZzzzz...";

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        internal static Harmony _harmony;

        private void Awake()
        {
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
        class VegvisirHoverPatch
        {
            static void Prefix(ref string ___m_name, ref string ___m_pinName)
            {
                ___m_name = "Dream Gate:";

                Player localPlayer = Player.m_localPlayer;
                if (localPlayer.IsTeleportable())
                {
                    ___m_pinName = "The stone hums with energy.";
                } else
                {
                    ___m_pinName = "The stone hums faintly but appears inactive.";
                }
            }
        }

        [HarmonyPatch(typeof(Vegvisir), nameof(Vegvisir.Interact))]
        class VegvisirInteractPatch
        {
            static bool Prefix(Vegvisir __instance, bool hold)
            {
                Player lp = Player.m_localPlayer;
                if (hold)
                {
                    return false;
                }

                if (!lp.IsTeleportable())
                {
                    lp.Message(MessageHud.MessageType.Center, "The Dream Gate is unable to activate.");
                }
                else
                {
                    if ((bool)lp)
                    {
                        Jotunn.Logger.LogInfo($"The player begins to dream.");
                        Game.instance.StartCoroutine(FindPlayerBed());
                    }
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(SleepText), nameof(SleepText.ShowDreamText))]
        class SleepTextShowDreamTextPatch
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
        class SleepTextOnEnablePatch
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
        class SleepTextHideDreamTextPatch
        {
           static void Postfix(ref Text ___m_textField)
            {
                ___m_textField.text = sleepText;
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateBlackScreen))]
        class HudUpdateBlackScreenPatch
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
        class MessageHudShowMessagePatch
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

        private static IEnumerator FindPlayerBed()
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