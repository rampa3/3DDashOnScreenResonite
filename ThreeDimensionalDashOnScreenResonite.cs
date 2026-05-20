using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using System.Reflection;
using System;
using Elements.Core;
using FrooxEngine.UIX;
using Renderite.Shared;

namespace ThreeDimensionalDashOnScreen;

public class ThreeDimensionalDashOnScreen : ResoniteMod
{
    public override string Name => "3DDashOnScreenResonite";
    public override string Author => "rampa3";
    public override string Version => "3.7.0";
    public override string Link => "https://github.com/rampa3/3DDashOnScreenResonite";

    private static ModConfiguration _config;
    private static Harmony _harmony;

    private sealed class PatchEntry(ModConfigurationKey<bool> key, Action patch, Action unpatch)
    {
        public readonly ModConfigurationKey<bool> Key = key;
        public readonly Action Patch = patch;
        public readonly Action Unpatch = unpatch;

        public bool LastValue;
    }

    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> ModEnabled = new ModConfigurationKey<bool>("ModEnabled", "Enabled", () => true);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<Key> DesktopControlPanelKey = new ModConfigurationKey<Key>("DesktopControlPanelKey", "Desktop tab control panel key", () => Key.N);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> ReleaseCamUI = new ModConfigurationKey<bool>("ReleaseCamUI", "Release Camera Controls UI from its slider (requires restart on change)", () => false);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<Key> UIEditModeKey = new ModConfigurationKey<Key>("UIEditModeKey", "UI edit mode key", () => Key.F4);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> KeepNotificationOnScreen = new ModConfigurationKey<bool>("KeepNotificationOnScreen", "Keep Notification On Screen?", () => true);

    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> PatchDash = new ModConfigurationKey<bool>("Patch Dash", "Patch Dash behavior", () => true);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> PatchSlotPositioning = new ModConfigurationKey<bool>("Patch SlotPositioning", "Patch Slot Positioning", () => true);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> PatchUIEditKey = new ModConfigurationKey<bool>("Patch UIEditKey", "Patch UI Edit Key", () => true);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> PatchDesktopPanel = new ModConfigurationKey<bool>("Patch DesktopPanel", "Patch Desktop Control Panel", () => true);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> PatchCameraUI = new ModConfigurationKey<bool>("Patch CameraUI", "Patch Camera UI", () => true);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> PatchItemGrab = new ModConfigurationKey<bool>("Patch ItemGrab", "Patch Item Grab", () => true);
    [AutoRegisterConfigKey] private static readonly ModConfigurationKey<bool> PatchNotifications = new ModConfigurationKey<bool>("Patch Notifications", "Patch Notifications", () => true);

    private static readonly List<PatchEntry> PatchEntries = new List<PatchEntry>
    {
        new PatchEntry(PatchDash, DashPatch.Patch, DashPatch.Unpatch),
        new PatchEntry(PatchSlotPositioning, SlotPositioningPatch.Patch, SlotPositioningPatch.Unpatch),
        new PatchEntry(PatchUIEditKey, UIEditKeyPatch.Patch, UIEditKeyPatch.Unpatch),
        new PatchEntry(PatchDesktopPanel, DesktopControlPanelPatch.Patch, DesktopControlPanelPatch.Unpatch),
        new PatchEntry(PatchCameraUI, CameraUIPatch.Patch, CameraUIPatch.Unpatch),
        new PatchEntry(PatchItemGrab, ItemGrabPatch.Patch, ItemGrabPatch.Unpatch),
        new PatchEntry(PatchNotifications, NotificationPatch.Patch, NotificationPatch.Unpatch)
    };

    public override void OnEngineInit()
    {
        _config = GetConfiguration()!;
        _config.Save(true);

        _harmony = new Harmony("net.rampa3.3DDashOnScreenResonite");

        foreach (PatchEntry patch in PatchEntries)
        {
            bool enabled = _config.GetValue(patch.Key);

            patch.LastValue = enabled;

            if (_config.GetValue(ModEnabled) && enabled)
            {
                patch.Patch();
            }

            patch.Key.OnChanged += val => { TogglePatch(patch, (bool)val!); };
        }

        PatchDash.OnChanged += val =>
        {
            if (!(bool)val!)
            {
                SetPatchItemGrab(false);
            }
        };

        PatchItemGrab.OnChanged += val =>
        {
            bool enabled = (bool)val!;

            if (enabled && !_config.GetValue(PatchDash))
            {
                SetPatchItemGrab(false);
            }
        };

        ModEnabled.OnChanged += val =>
        {
            if ((bool)val!)
            {
                PatchAll();
            }
            else
            {
                UnpatchAll();
            }
        };
    }

    private static void SetPatchItemGrab(bool enabled)
    {
        if (_config.GetValue(PatchItemGrab) == enabled)
        {
            return;
        }

        _config.Set(PatchItemGrab, enabled);
    }

    private static PatchEntry GetPatch(ModConfigurationKey<bool> key) => PatchEntries.First(x => Equals(x.Key, key));

    private static void TogglePatch(PatchEntry patch, bool enabled)
    {
        if (!_config.GetValue(ModEnabled))
            return;

        if (enabled)
        {
            patch.Patch();
        }
        else
        {
            patch.Unpatch();
        }
    }

    private static void PatchAll()
    {
        bool dashEnabled = GetPatch(PatchDash).LastValue;

        foreach (PatchEntry patch in PatchEntries)
        {
            bool enabled = patch.LastValue;

            if (Equals(patch.Key, PatchItemGrab))
            {
                enabled &= dashEnabled;
            }

            _config.Set(patch.Key, enabled);
        }
    }

    private static void UnpatchAll()
    {
        foreach (PatchEntry patch in PatchEntries)
        {
            patch.LastValue = _config.GetValue(patch.Key);
            _config.Set(patch.Key, false);
        }
    }

    private static readonly MethodInfo UpdateOverlayState = AccessTools.Method(typeof(UserspaceRadiantDash), "UpdateOverlayState");
    private static void RefreshDashState()
    {
        UserspaceRadiantDash dash = Userspace.UserspaceWorld?.GetRadiantDash();
        if (dash != null)
        {
            UpdateOverlayState.Invoke(dash, Array.Empty<object>());
        }
    }

    [HarmonyPatchCategory("Dash")]
    private static class DashPatch
    {
        private static readonly FieldInfo LastField = AccessTools.Field(typeof(UserspaceRadiantDash), "_lastCursorUnlockRegistered");
        private static readonly FieldInfo KeyboardBlock = AccessTools.Field(typeof(KeyboardBlock), nameof(FrooxEngine.KeyboardBlock.GLOBAL_BLOCK));
        private static readonly FieldInfo MouseBlock = AccessTools.Field(typeof(MouseBlock), nameof(FrooxEngine.MouseBlock.GLOBAL_BLOCK));
        private static readonly MethodInfo RegisterUnlock = AccessTools.Method(typeof(InputBindingManager), nameof(InputBindingManager.RegisterCursorUnlock));
        private static readonly MethodInfo UnregisterUnlock = AccessTools.Method(typeof(InputBindingManager), nameof(InputBindingManager.UnregisterCursorUnlock));

        public static void Patch()
        {
            _harmony.PatchCategory("Dash");

            UserspaceRadiantDash dash = Userspace.UserspaceWorld?.GetRadiantDash();
            if (dash != null)
            {
                dash.Input.UnregisterCursorUnlock(dash);
                LastField.SetValue(dash, false);
            }

            RefreshDashState();
        }

        public static void Unpatch()
        {
            _harmony.UnpatchCategory("Dash");

            UserspaceRadiantDash dash = Userspace.UserspaceWorld?.GetRadiantDash();
            if (dash != null)
            {
                try
                {
                    bool shouldUnlock = dash.InputInterface.ScreenActive && dash.Open;

                    if (shouldUnlock)
                        dash.Input.RegisterCursorUnlock(dash);
                    else
                        dash.Input.UnregisterCursorUnlock(dash);

                    LastField.SetValue(dash, shouldUnlock);
                }
                catch (Exception e)
                {
                    Warn(e.Message);
                }
            }

            RefreshDashState();
        }

        [HarmonyPatch(typeof(UserspaceRadiantDash), "OnCommonUpdate"), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].StoresField(LastField))
                {
                    codes[i - 2].opcode = OpCodes.Nop;
                    codes[i - 1].opcode = OpCodes.Nop;
                    codes[i].opcode = OpCodes.Nop;
                }

                if (codes[i].LoadsField(KeyboardBlock) || codes[i].LoadsField(MouseBlock))
                {
                    codes[i - 2].opcode = OpCodes.Nop;
                    codes[i - 1].opcode = OpCodes.Nop;
                    codes[i].opcode = OpCodes.Nop;
                    codes[i + 1].opcode = OpCodes.Nop;
                    i += 1;
                }

                if (codes[i].Calls(RegisterUnlock) || codes[i].Calls(UnregisterUnlock))
                {
                    codes[i - 3].opcode = OpCodes.Nop;
                    codes[i - 2].opcode = OpCodes.Nop;
                    codes[i - 1].opcode = OpCodes.Nop;
                    codes[i].opcode = OpCodes.Nop;
                }
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch(typeof(UserspaceRadiantDash), "OnCommonUpdate"), HarmonyPostfix]
        private static void Postfix(UserspaceRadiantDash __instance)
        {
            __instance.Dash.ScreenProjection.Value = false;
        }

        [HarmonyPatch(typeof(UserspaceRadiantDash), "UpdateOverlayState"), HarmonyPrefix]
        private static bool Prefix(UserspaceRadiantDash __instance, SyncRef<Slot> ____notificationsRoot, SyncRef<Slot> ____notificationsHolder)
        {
            RadiantDash dash = __instance.Dash;
            dash.VisualsRoot.SetParent(dash.Slot, false);
            dash.VisualsRoot.SetIdentityTransform();

            Slot notificationsParent = ____notificationsRoot.Target;
            if (!_config.GetValue(PatchNotifications) && !__instance.InputInterface.VR_Active)
            {
                OverlayManager overlayManager = __instance.World.GetGloballyRegisteredComponent<OverlayManager>();
                notificationsParent = overlayManager.OverlayRoot;
            }

            Slot target = ____notificationsHolder.Target;
            target.SetParent(notificationsParent, keepGlobalTransform: false);
            target.SetIdentityTransform();
            return false;
        }
    }

    [HarmonyPatchCategory("SlotPositioning")]
    private static class SlotPositioningPatch
    {
        public static void Patch()
        {
            _harmony.PatchCategory("SlotPositioning");
        }

        public static void Unpatch()
        {
            _harmony.UnpatchCategory("SlotPositioning");
        }

        [HarmonyPatch(typeof(SlotPositioning), nameof(SlotPositioning.PositionInFrontOfUser)), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            if (codes[0].opcode == OpCodes.Ldarg_0 && codes[1].opcode == OpCodes.Callvirt && codes[2].opcode == OpCodes.Call && (codes[3].opcode == OpCodes.Bne_Un_S || codes[3].opcode == OpCodes.Bne_Un || codes[3].opcode == OpCodes.Brfalse_S || codes[3].opcode == OpCodes.Brfalse))
            {
                codes[0].opcode = OpCodes.Nop;
                codes[1].opcode = OpCodes.Nop;
                codes[2].opcode = OpCodes.Nop;
                codes[3].opcode = OpCodes.Br_S;
            }
            else
            {
                Error("SlotPositioning.PositionInFrontOfUser: Could not patch because of unexpected opcode");
            }

            return codes;
        }
    }

    [HarmonyPatchCategory("UIEditKey")]
    private static class UIEditKeyPatch
    {
        public static void Patch()
        {
            _harmony.PatchCategory("UIEditKey");
        }

        public static void Unpatch()
        {
            _harmony.UnpatchCategory("UIEditKey");
        }

        [HarmonyPatch(typeof(Userspace), "OnCommonUpdate"), HarmonyPostfix]
        private static void Postfix(Userspace __instance)
        {
            if (!__instance.InputInterface.GetKey(Key.Control) && (!__instance.InputInterface.GetKey(Key.Alt) || !__instance.InputInterface.GetKey(Key.AltGr)))
            {
                if (__instance.InputInterface.GetKeyDown(_config.GetValue(UIEditModeKey)) && __instance.InputInterface.ScreenActive)
                {
                    Userspace.UserInterfaceEditMode = !Userspace.UserInterfaceEditMode;
                }
            }
        }
    }

    [HarmonyPatchCategory("DesktopControlPanel")]
    private static class DesktopControlPanelPatch
    {
        public static void Patch()
        {
            _harmony.PatchCategory("DesktopControlPanel");
        }

        public static void Unpatch()
        {
            _harmony.UnpatchCategory("DesktopControlPanel");
        }

        [HarmonyPatch(typeof(DesktopController), "OnCommonUpdate"), HarmonyPostfix]
        private static void Postfix(DesktopController __instance)
        {
            MethodInfo toggleControls = __instance.GetType().GetMethod("ToggleControls", BindingFlags.NonPublic | BindingFlags.Instance);
            if (__instance.InputInterface.GetKeyDown(_config.GetValue(DesktopControlPanelKey)) && __instance.InputInterface.ScreenActive)
            {
                toggleControls?.Invoke(__instance, Array.Empty<object>());
            }
        }
    }

    [HarmonyPatchCategory("CameraUI")]
    private static class CameraUIPatch
    {
        public static void Patch()
        {
            _harmony.PatchCategory("CameraUI");
        }

        public static void Unpatch()
        {
            _harmony.UnpatchCategory("CameraUI");
        }

        [HarmonyPatch(typeof(InteractiveCameraControl), "OnAttach"), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (!_config.GetValue(ReleaseCamUI) && codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Call && codes[i + 2].opcode == OpCodes.Callvirt && ((MethodInfo)codes[i + 2].operand == typeof(InputInterface).GetMethod("get_VR_Active")) && codes[i + 3].opcode == OpCodes.Brfalse_S)
                {
                    codes[i].opcode = OpCodes.Nop;
                    codes[i + 1].opcode = OpCodes.Nop;
                    codes[i + 2].opcode = OpCodes.Nop;
                    codes[i + 3].opcode = OpCodes.Nop;
                }

                if (_config.GetValue(ReleaseCamUI) && codes[i].opcode == OpCodes.Dup && codes[i + 1].opcode == OpCodes.Brtrue_S && codes[i + 2].opcode == OpCodes.Pop && codes[i + 3].opcode == OpCodes.Br_S)
                {
                    codes[i + 4].opcode = OpCodes.Pop;
                }
            }

            return codes.AsEnumerable();
        }

        [HarmonyPatch(typeof(InteractiveCameraControl), "OnAttach"), HarmonyPostfix]
        private static void Postfix(InteractiveCameraControl __instance)
        {
            if (_config.GetValue(ReleaseCamUI))
            {
                __instance.Slot.GetComponent<Slider>().Destroy();
            }
        }
    }

    [HarmonyPatchCategory("ItemGrab")]
    private static class ItemGrabPatch
    {
        public static void Patch()
        {
            _harmony.PatchCategory("ItemGrab");
        }

        public static void Unpatch()
        {
            _harmony.UnpatchCategory("ItemGrab");
        }

        [HarmonyPatch(typeof(InteractionHandler), "OnInputUpdate"), HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Ldfld && codes[i + 2].opcode == OpCodes.Stloc_0 && codes[i + 3].opcode == OpCodes.Ldarg_0 && codes[i + 4].opcode == OpCodes.Call && codes[i + 5].opcode == OpCodes.Callvirt && ((MethodInfo)codes[i + 5].operand == typeof(InputInterface).GetMethod("get_VR_Active")))
                {
                    codes[i + 3].opcode = OpCodes.Nop;
                    codes[i + 4].opcode = OpCodes.Nop;
                    codes[i + 5].opcode = OpCodes.Ldc_I4_1;
                }
            }

            return codes.AsEnumerable();
        }
    }

    [HarmonyPatchCategory("Notifications")]
    private static class NotificationPatch
    {
        private static bool _patched;

        public static void Patch()
        {
            if (!_patched)
            {
                _patched = true;
                _harmony.PatchCategory("Notifications");
                Debug("Notifications fix applied!");
            }

            RefreshDashState();
        }

        public static void Unpatch()
        {
            // _harmony.UnpatchCategory("Notifications");

            RefreshDashState();
        }

        [HarmonyPatch(typeof(NotificationPanel), "OnCommonUpdate"), HarmonyPostfix]
        private static void Postfix(NotificationPanel __instance, SyncRef<Canvas> ____canvas)
        {
            if (PatchNotifications.Value && PatchDash.Value)
            {
                ____canvas.Target.Size.Value = NotificationPanel.CANVAS_SIZE;
                __instance.Slot.LocalScale = float3.One * NotificationPanel.VR_SCALE;
            }

            if (!PatchNotifications.Value && KeepNotificationOnScreen.Value)
            {
                if (__instance.Slot.Position_Field.ActiveLink == null)
                {
                    __instance.Slot.LocalPosition = new float3(z: -0.1f);
                    __instance.Slot.Position_Field.DriveFrom(__instance.Slot.Position_Field);
                }
            }
            else
            {
                if (__instance.Slot.Position_Field.ActiveLink != null)
                {
                    __instance.Slot.Position_Field.ReleaseLink(__instance.Slot.Position_Field.ActiveLink);
                }
            }
        }
    }
}