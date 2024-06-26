﻿using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Configuration;

namespace PodiumPlugin
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class PodiumPlugin : BaseUnityPlugin
    {
        public const string pluginGuid = "com.metalted.zeepkist.podiumpreview";
        public const string pluginName = "Level Editor Podium Preview";
        public const string pluginVersion = "1.6.0";

        //Create a static reference for the config file.
        public static ConfigFile Cfg { get; private set; }

        public void Awake()
        {
            //Set the config and the change listener.
            Cfg = Config;
            Config.SettingChanged += Config_SettingChanged;

            //Create entries for the toggle key and the amount of dropped players.
            ConfigEntry<int> droppedPlayers = Config.Bind("Settings", "Dropped Players", 10, "The amount of dropped players.");
            ConfigEntry<KeyCode> previewToggle = Config.Bind("Settings", "Toggle Preview", KeyCode.P, "Toggle key for the preview.");

            Harmony harmony = new Harmony(pluginGuid);
            harmony.PatchAll();
            Logger.LogInfo($"Plugin {pluginName} is loaded!");
        }

        private void Config_SettingChanged(object sender, SettingChangedEventArgs e)
        {
            //Required because startup will throw an error.
            try
            {
                //Read the dropped player config and assign it to the manager.
                int toDrop = (int)PodiumPlugin.Cfg["Settings", "Dropped Players"].BoxedValue;
                if (toDrop >= 0)
                {
                    PodiumManagement.playersToDrop = toDrop;
                }
                else
                {
                    PodiumManagement.playersToDrop = 0;
                }
            }
            catch { }
        }

        //Send update calls to the manager.
        public void Update()
        {
            if (PodiumManagement.levelEditorHardLock)
            {
                if (PodiumManagement.inTestLevel)
                {
                    PodiumManagement.Update();
                }
            }
        }
    }

    public static class PodiumManagement
    {
        //True if the podium preview is currently active.
        public static bool previewing = false;
        //True if we are driving in a testing map from the level editor.
        public static bool inTestLevel = false;
        //Not null if we loaded the game scene from the level editor.
        public static GameMaster master;
        //Flag set to true when the player is in photomode.
        public static bool inPhotoMode = false;
        public static bool levelEditorHardLock = false;

        //Used for the update function.
        public static bool dropLosers = true;
        public static int playersToDrop = 10;
        public static float timer = 1f;
        public static float ticker = 0.1f;
        public static List<SetupModelCar> dropTheseGuys = new List<SetupModelCar>();
        public static List<SetupModelCar> droppedTheseGuys = new List<SetupModelCar>();
        public static OnlineResultsPodium tempPodium = null;

        //Called when we enter the level editor from the main menu or when we return from testing a map.
        public static void EnteredLevelEditor()
        {
            //Cant be previewing when in the level editor.
            previewing = false;
            //Not in a test level cause we are not in any level.
            inTestLevel = false;
            //Can't be in photomode cause we are in the level editor.
            inPhotoMode = false;
            //Enable the mod
            levelEditorHardLock = true;
        }

        //Called when we load the game scene. If this is not the test level, gameMaster will be null.
        public static void StartedGame(GameMaster gameMaster, bool isTestLevel)
        {
            //Set the master.
            master = gameMaster;
            //Set the test level flag.
            inTestLevel = isTestLevel;
            //As we just loaded the scene, we are not previewing.
            previewing = false;
            //Can't be in photomode cause we just started a game.
            inPhotoMode = false;
        }

        //Called when photomode is toggled.
        public static void ToggledPhotomode()
        {
            if (master != null)
            {
                inPhotoMode = master.isPhotoMode;

                if (inPhotoMode)
                {
                    StopPreview();
                }
            }
        }

        //Update function called from the plugin to handle key input.
        public static void Update()
        {
            //Create a toggle for previewing.
            if (Input.GetKeyDown((KeyCode)PodiumPlugin.Cfg["Settings", "Toggle Preview"].BoxedValue))
            {
                if (!previewing)
                {
                    PreviewPodium();
                }
                else
                {
                    StopPreview();
                }
            }

            //Only continue if we need to drop soapboxes in the preview.
            if (!dropLosers)
            {
                return;
            }

            //Increment the timer to drop the soapboxes.
            timer += Time.deltaTime;
            if (timer <= ticker)
            {
                return;
            }
            timer = 0;
            DropRandomGuy();
        }

        //Start a new podium preview.
        public static void PreviewPodium()
        {
            if (master != null && inTestLevel && !inPhotoMode)
            {
                //Create a new copy of the podium, as the camera animation starts at activation but never resets.
                tempPodium = GameObject.Instantiate<OnlineResultsPodium>(master.onlineResults);
                //Initialize and activate the new podium.
                DoResultsPodium(tempPodium, playersToDrop);
                //Drop the soapboxes.
                dropLosers = true;
                //Previewing is now active.
                previewing = true;
            }
        }

        //Destroy the current preview.
        public static void StopPreview()
        {
            //Just destroy everything, we'll make a new podium.
            if (previewing)
            {
                if (tempPodium != null)
                {
                    tempPodium.gameObject.SetActive(false);
                    GameObject.Destroy(tempPodium);
                }

                foreach (SetupModelCar smc in dropTheseGuys)
                {
                    GameObject.Destroy(smc.gameObject);
                }

                foreach (SetupModelCar smc in droppedTheseGuys)
                {
                    GameObject.Destroy(smc.gameObject);
                }

                dropTheseGuys.Clear();
                droppedTheseGuys.Clear();

                previewing = false;
                dropLosers = false;
            }
        }

        //Create a new podium with random players and a set amount of dropped soapboxes.
        private static void DoResultsPodium(OnlineResultsPodium podium, int droppedPlayers)
        {
            //Set the object active
            podium.gameObject.SetActive(true);

            //Find the podium in the game.
            GameObject obj = GameObject.Find("355 - Podium");

            //If there is a podium found, overlay this podium on top of it and disable it.
            if (obj != null)
            {
                podium.transform.position = obj.transform.position;
                podium.transform.rotation = obj.transform.rotation;

                Vector3 s = podium.transform.localScale;
                /*if(Mathf.Min(s.x,s.y,s.z) == 0)
                {
                    s = Vector3.one;
                    PlayerManager.Instance.messenger.Log("Zero scale podium not supported.", 3f);
                }*/

                podium.transform.localScale = s;
                podium.localPodium.SetActive(false);
            }
            else
            {
                //Put the podium at the default position.
                podium.transform.position = new Vector3(0, 500f, 0);
            }

            //Set some values on the podium
            podium.wintext1.text = "";
            podium.wintext2.text = "";
            podium.wintext3.text = "";

            //Place the 3 winners
            CosmeticsV16 player1Cosmetics = GetRandomCosmetics();
            CosmeticsV16 player2Cosmetics = GetRandomCosmetics();
            CosmeticsV16 player3Cosmetics = GetRandomCosmetics();

            podium.winner1.gameObject.SetActive(true);
            podium.winner1.DoCarSetup(player1Cosmetics, true, false, true);

            podium.winner2.gameObject.SetActive(true);
            podium.winner2.DoCarSetup(player2Cosmetics, true, false, true);

            podium.winner3.gameObject.SetActive(true);
            podium.winner3.DoCarSetup(player3Cosmetics, true, false, true);

            //Clear the drop list.
            dropTheseGuys.Clear();
            droppedTheseGuys.Clear();

            //Create some characters to drop.
            for (int i = 0; i < droppedPlayers; i++)
            {
                SetupModelCar setupModelCar = GameObject.Instantiate<SetupModelCar>(podium.fallingCarPrefab);
                setupModelCar.transform.localScale = Vector3.one;
                CosmeticsV16 rngCosmetic = GetRandomCosmetics();
                setupModelCar.DoCarSetup(rngCosmetic, true, false, true);
                setupModelCar.transform.position = podium.dropGuysHere.position + Random.insideUnitSphere * 8f;
                setupModelCar.transform.rotation = Random.rotation;
                setupModelCar.gameObject.SetActive(false);
                dropTheseGuys.Add(setupModelCar);
            }
        }

        //Drop a random cart from the list.
        public static void DropRandomGuy()
        {
            if (dropTheseGuys.Count <= 0)
            {
                return;
            }
            int i = Random.Range(0, dropTheseGuys.Count);
            SetupModelCar guy = dropTheseGuys[i];
            dropTheseGuys.RemoveAt(i);
            droppedTheseGuys.Add(guy);
            guy.gameObject.SetActive(true);
        }

        //Code for random cosmetics.
        private static CosmeticsV16 GetRandomCosmetics()
        {
            CosmeticsV16 cosmetics = new CosmeticsV16();
            int zeepkist = GetRandomIntFromList(new List<int>(PlayerManager.Instance.objectsList.wardrobe.everyZeepkist.Keys));
            int hat = GetRandomIntFromList(new List<int>(PlayerManager.Instance.objectsList.wardrobe.everyHat.Keys));
            int color = GetRandomIntFromList(new List<int>(PlayerManager.Instance.objectsList.wardrobe.everyColor.Keys));
            int glasses = GetRandomIntFromList(new List<int>(PlayerManager.Instance.objectsList.wardrobe.everyGlasses.Keys));
            int wheel = GetRandomIntFromList(new List<int>(PlayerManager.Instance.objectsList.wardrobe.everyWheel.Keys));
            int paraglider = GetRandomIntFromList(new List<int>(PlayerManager.Instance.objectsList.wardrobe.everyParaglider.Keys));
            int horn = GetRandomIntFromList(new List<int>(PlayerManager.Instance.objectsList.wardrobe.everyHorn.Keys));

            ZeepkistNetworking.CosmeticIDs cosmeticIDs = new ZeepkistNetworking.CosmeticIDs()
            {
                zeepkist = zeepkist,
                frontWheels = wheel,
                rearWheels = wheel,
                paraglider = paraglider,
                horn = horn,
                hat = hat,
                glasses = glasses,
                color_body = color,
                color_leftArm = color,
                color_rightArm = color,
                color_leftLeg = color,
                color_rightLeg = color,
                color = color
            };

            cosmetics.IDsToCosmetics(cosmeticIDs);
            return cosmetics;
        }

        private static int GetRandomIntFromList(List<int> keys)
        {
            int randomIndex = Random.Range(0, keys.Count);
            int randomKey = keys[randomIndex];
            return randomKey;
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "Awake")]
    public class MainMenuAwake
    {
        public static void Prefix()
        {
            PodiumManagement.levelEditorHardLock = false;
        }
    }

    [HarmonyPatch(typeof(LobbyManager), "Awake")]
    public class LobbyAwake
    {
        public static void Prefix()
        {
            PodiumManagement.levelEditorHardLock = false;
        }
    }

    [HarmonyPatch(typeof(GameMaster), "Awake")]
    public class GameMasterAwakePatch
    {
        public static void Postfix(GameMaster __instance)
        {
            if (__instance.GlobalLevel.IsTestLevel)
            {
                //Debug.LogWarning("We are in a test level");
                PodiumManagement.StartedGame(__instance, true);
            }
            else
            {
                //Debug.LogWarning("We are not in a test level");
                PodiumManagement.StartedGame(null, false);
            }
        }
    }

    [HarmonyPatch(typeof(LEV_LevelEditorCentral), "Awake")]
    public class LEV_Awake
    {
        public static void Postfix()
        {
            PodiumManagement.EnteredLevelEditor();
        }
    }

    [HarmonyPatch(typeof(EnableFlyingCamera2), "ToggleFlyingCamera", new[] { typeof(bool) })]
    public class ToggledPhotomode
    {
        public static void Postfix(ref bool endOfRound, EnableFlyingCamera2 __instance)
        {
            PodiumManagement.ToggledPhotomode();
        }
    }
}
