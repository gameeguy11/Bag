using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Nautilus.Handlers;
using Nautilus.Options;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PickupFullCarryalls
{
    [BepInPlugin("com.yourname.pickupfullcarryalls", "Pickup Full Carry-alls", "1.0.0")]
    [BepInDependency("com.snmodding.nautilus")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
            Log.LogInfo("Patched successfully!");

            OptionsPanelHandler.RegisterModOptions(new PFC_Options("Pickup Full Carry-alls"));
            Log.LogInfo("Registered mod options");

            ItemActionHandler.RegisterMiddleClickAction(
                TechType.LuggageBag,
                InventoryOpener.OnMiddleClick,
                "open storage"
            );
            ItemActionHandler.RegisterMiddleClickAction(
                TechType.SmallStorage,
                InventoryOpener.OnMiddleClick,
                "open storage"
            );

            Log.LogInfo("Registered middle click actions");
        }

        public static class PFC_Config
        {
            public static bool Enable
            {
                get => PlayerPrefs.GetInt("pfcEnable", 1) == 1;
                set
                {
                    PlayerPrefs.SetInt("pfcEnable", value ? 1 : 0);
                    PlayerPrefs.Save();
                }
            }

            public static int AllowMMBIndex
            {
                get => PlayerPrefs.GetInt("pfcMMBIndex", 0);
                set
                {
                    PlayerPrefs.SetInt("pfcMMBIndex", value);
                    PlayerPrefs.Save();
                }
            }

            public static string[] AllowMMBOptions =
            {
            "Yes",
            "Only in player inventory",
            "No",
        };

            public static string AllowMMB => AllowMMBOptions[AllowMMBIndex];
        }

        // Only ONE PFC_Options class
        public class PFC_Options : ModOptions
        {
            public PFC_Options(string name) : base(name)
            {
                var toggle = ModToggleOption.Create("pfcEnable", "Enable", PFC_Config.Enable);
                toggle.OnChanged += (sender, e) =>
                {
                    if (e is ToggleChangedEventArgs t)
                    {
                        Plugin.Log.LogInfo(t.Value ? "Enabled mod" : "Disabled mod");
                        PFC_Config.Enable = t.Value;
                    }
                };
                AddItem(toggle);

                var choice = ModChoiceOption<string>.Create(
                    "pfcMMB",
                    "Open storage in inventory",
                    PFC_Config.AllowMMBOptions,
                    PFC_Config.AllowMMBIndex
                );
                choice.OnChanged += (sender, e) =>
                {
                    if (e is ChoiceChangedEventArgs<string> c)
                    {
                        Plugin.Log.LogInfo($"Set storage opening in inventory to: \"{c.Value}\"");
                        PFC_Config.AllowMMBIndex = c.Index;
                    }
                };
                AddItem(choice);
            }
        }

        public static class InventoryOpener
        {
            public static InventoryItem LastOpened;
            public static uGUI_ItemsContainer InventoryUGUI;
            public static bool DontEnable;

            public static void OnMiddleClick(InventoryItem item)
            {
                // Guard: replaces the old Condition callback
                if (!PFC_Config.Enable) return;
                if (!CanOpen(item)) return;

                Vector2int cursorPosition = GetCursorPosition();

                DontEnable = true;
                Player.main.GetPDA().Close();
                DontEnable = false;

                StorageContainer container = item.item.gameObject
                    .GetComponentInChildren<PickupableStorage>()
                    .storageContainer;
                container.Open();
                container.onUse.Invoke();

                if (PlayerInventoryContains(item))
                {
                    if (LastOpened != null)
                    {
                        LastOpened.isEnabled = true;
                        GetIconForItem(LastOpened)?.SetChroma(1f);
                    }
                    item.isEnabled = false;
                    GetIconForItem(item)?.SetChroma(0f);
                    LastOpened = item;
                }

                Player.main.StartCoroutine(ResetCursor(cursorPosition));
            }

            public static bool Condition(InventoryItem item)
            {
                if (!PFC_Config.Enable) return false;
                if (!CanOpen(item)) return false;
                return true;
            }

            public static bool CanOpen(InventoryItem item)
            {
                switch (PFC_Config.AllowMMB)
                {
                    case "Yes": return true;
                    case "No": return false;
                    case "Only in player inventory": return PlayerInventoryContains(item);
                    default: return false;
                }
            }

            public static bool PlayerInventoryContains(InventoryItem item)
            {
                IList<InventoryItem> matchingItems =
                    Inventory.main.container.GetItems(item.item.GetTechType());
                if (matchingItems == null) return false;
                return matchingItems.Contains(item);
            }

            public static uGUI_ItemIcon GetIconForItem(InventoryItem item)
            {
                var items = typeof(uGUI_ItemsContainer)
                    .GetField("items", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(InventoryUGUI) as Dictionary<InventoryItem, uGUI_ItemIcon>;
                return items?[item];
            }

            #region Mouse Position

            public static IEnumerator ResetCursor(Vector2int position)
            {
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                SetCursorPosition(position);
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Point { public int X; public int Y; }

            public static Vector2int GetCursorPosition()
            {
                GetCursorPos(out Point point);
                return new Vector2int(point.X, point.Y);
            }

            public static void SetCursorPosition(Vector2int position)
            {
                SetCursorPos(position.x, position.y);
            }

            [DllImport("user32.dll")]
            public static extern bool GetCursorPos(out Point pos);
            [DllImport("user32.dll")]
            public static extern bool SetCursorPos(int X, int Y);

            #endregion
        }

        public static class Patches
        {
            #region Storage Pickup

            [HarmonyPatch(typeof(PickupableStorage), "OnHandClick")]
            public static class PickupableStorage_OnHandClick
            {
                [HarmonyPrefix]
                public static bool Prefix(PickupableStorage __instance, GUIHand hand)
                {
                    TechType type = __instance.pickupable.GetTechType();
                    if (PFC_Config.Enable && (type == TechType.LuggageBag || type == TechType.SmallStorage))
                    {
                        __instance.pickupable.OnHandClick(hand);
                        Plugin.Log.LogInfo("Picked up a carry-all");
                        return false;
                    }
                    return true;
                }
            }

            [HarmonyPatch(typeof(PickupableStorage), "OnHandHover")]
            public static class PickupableStorage_OnHandHover
            {
                [HarmonyPrefix]
                public static bool Prefix(PickupableStorage __instance, GUIHand hand)
                {
                    TechType type = __instance.pickupable.GetTechType();
                    if (PFC_Config.Enable && (type == TechType.LuggageBag || type == TechType.SmallStorage))
                    {
                        __instance.pickupable.OnHandHover(hand);
                        return false;
                    }
                    return true;
                }
            }

            #endregion

            #region Destruction Prevention

            [HarmonyPatch(typeof(ItemsContainer), "IItemsContainer.AllowedToRemove")]
            public static class IItemsContainer_AllowedToRemove
            {
                [HarmonyPrefix]
                public static bool Prefix(ItemsContainer __instance, ref bool __result, Pickupable pickupable, bool verbose)
                {
                    if (!PFC_Config.Enable) return true;
                    if (__instance != Inventory.main.container) return true;
                    if (pickupable == InventoryOpener.LastOpened?.item)
                    {
                        __result = false;
                        return false;
                    }
                    return true;
                }
            }

            [HarmonyPatch(typeof(uGUI_ItemsContainer), "Init")]
            public static class uGUI_ItemsContainer_Init
            {
                [HarmonyPostfix]
                public static void Postfix(uGUI_ItemsContainer __instance, ItemsContainer container)
                {
                    if (container == Inventory.main.container)
                        InventoryOpener.InventoryUGUI = __instance;
                }
            }

            [HarmonyPatch(typeof(PDA), "Close")]
            public static class PDA_Close
            {
                [HarmonyPostfix]
                public static void Postfix()
                {
                    if (InventoryOpener.LastOpened != null && !InventoryOpener.DontEnable)
                    {
                        InventoryOpener.LastOpened.isEnabled = true;
                        InventoryOpener.GetIconForItem(InventoryOpener.LastOpened)?.SetChroma(1f);
                        InventoryOpener.LastOpened = null;
                    }
                }
            }

            #endregion
        }
    }
}
