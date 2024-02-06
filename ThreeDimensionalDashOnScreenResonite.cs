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

namespace ThreeDimensionalDashOnScreen
{
    public class ThreeDimensionalDashOnScreen : ResoniteMod
	{
		public override string Name => "3DDashOnScreenResonite";
		public override string Author => "rampa3";
		public override string Version => "3.6.0";
		public override string Link => "https://github.com/rampa3/3DDashOnScreenResonite";
		private static ModConfiguration Config;


		public override void OnEngineInit()
		{
			Config = GetConfiguration();
			Config.Save(true);
			Harmony harmony = new Harmony("net.rampa3.3DDashOnScreenResonite");
			if (Config.GetValue(MOD_ENABLED))
            {
				patchDash(harmony);
				patchSlotPositioning(harmony);
				addUIEditKey(harmony);
				addDesktopControlPanelKeybind(harmony);
				patchCameraUI(harmony);
				disableForceItemKeepGrabbed(harmony);
				fixResoniteNotifications(harmony);
                Debug("All patches applied successfully!");
            } else {
				Debug("3DDashOnScreen disabled!");
			}
			
		}

		[AutoRegisterConfigKey]
		private static ModConfigurationKey<bool> MOD_ENABLED = new ModConfigurationKey<bool>("ModEnabled", "Enabled (requires restart on change)", () => true);

		[AutoRegisterConfigKey]
		private static ModConfigurationKey<Key> DESKTOP_CONTROL_PANEL_KEY = new ModConfigurationKey<Key>("DesktopControlPanelKey", "Desktop tab control panel key", () => Key.N);

		[AutoRegisterConfigKey]
		private static ModConfigurationKey<bool> RELEASE_CAM_UI = new ModConfigurationKey<bool>("ReleaseCamUI", "Release Camera Controls UI from its slider (requires restart on change)", () => false);

		[AutoRegisterConfigKey]
		private static ModConfigurationKey<Key> UI_EDIT_MODE_KEY = new ModConfigurationKey<Key>("UIEditModeKey", "UI edit mode key", () => Key.F4);

		private static void disableForceItemKeepGrabbed(Harmony harmony)
        {
			MethodInfo original = AccessTools.DeclaredMethod(typeof(InteractionHandler), "OnInputUpdate", new Type[] { });
			MethodInfo transpiler = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(disableForceItemKeepGrabbedTranspiler));
			harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));
			Debug("Forcing keep last item held when dash is open patched out!");
        }
        
        private static void fixResoniteNotifications(Harmony harmony)
		{
			MethodInfo original = AccessTools.DeclaredMethod(typeof(NotificationPanel), "OnCommonUpdate");
            MethodInfo postfix = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(notificationPanelPostfix));
            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
			Debug("Notifications fix applied!");
		}

        private static void notificationPanelPostfix(NotificationPanel __instance, SyncRef<Canvas> ____canvas)
        {
            ____canvas.Target.Size.Value = NotificationPanel.CANVAS_SIZE;
            Slot slot = __instance.Slot;
            float3 v = float3.One;
            slot.LocalScale = v * NotificationPanel.VR_SCALE;
            __instance.Slot.LocalPosition = float3.Zero;
        }
        private static IEnumerable<CodeInstruction> disableForceItemKeepGrabbedTranspiler(IEnumerable<CodeInstruction> instructions)
        {
			var codes = new List<CodeInstruction>(instructions);
			for (var i = 0; i < codes.Count; i++)
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

		private static void addDesktopControlPanelKeybind(Harmony harmony)
        {
			MethodInfo originalDesktopControllerOnCommonUpdate = AccessTools.DeclaredMethod(typeof(DesktopController), "OnCommonUpdate", new Type[] { });
			MethodInfo postfixDesktopControllerOnCommonUpdate = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(DesktopControlsKeybindPostfix));
            harmony.Patch(originalDesktopControllerOnCommonUpdate, postfix: new HarmonyMethod(postfixDesktopControllerOnCommonUpdate));
			Debug("Desktop tab control panel key added!");
        }

		private static void DesktopControlsKeybindPostfix(DesktopController __instance)
        {
			MethodInfo toggleControls = __instance.GetType().GetMethod("ToggleControls", BindingFlags.NonPublic | BindingFlags.Instance);
			if (__instance.InputInterface.GetKeyDown(Config.GetValue(DESKTOP_CONTROL_PANEL_KEY)) && __instance.InputInterface.ScreenActive)
			{
				toggleControls.Invoke(__instance, new Object[] { });
			}
		}

		private static void patchCameraUI(Harmony harmony)
        {
			MethodInfo original = AccessTools.DeclaredMethod(typeof(InteractiveCameraControl), "OnAttach", new Type[] { });
			MethodInfo transpiler = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(CameraUITranspiler));
			MethodInfo postfix = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(removeCamUISlider));
			harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));
			harmony.Patch(original, postfix: new HarmonyMethod(postfix));
			Debug("Camera Controls patched!");
		}

		private static IEnumerable<CodeInstruction> CameraUITranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			for (var i = 0; i < codes.Count; i++)
			{
				if (!Config.GetValue(RELEASE_CAM_UI) && codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Call && codes[i + 2].opcode == OpCodes.Callvirt && ((MethodInfo)codes[i + 2].operand == typeof(InputInterface).GetMethod("get_VR_Active")) && codes[i + 3].opcode == OpCodes.Brfalse_S)
				{
					codes[i].opcode = OpCodes.Nop;
					codes[i + 1].opcode = OpCodes.Nop;
					codes[i + 2].opcode = OpCodes.Nop;  //change the whole if statement and it's contents to do nothing
					codes[i + 3].opcode = OpCodes.Nop;
				}

				if (Config.GetValue(RELEASE_CAM_UI) && codes[i].opcode == OpCodes.Dup && codes[i + 1].opcode == OpCodes.Brtrue_S && codes[i + 2].opcode == OpCodes.Pop && codes[i + 3].opcode == OpCodes.Br_S)  //find the grabbable destroy call
				{
					codes[i + 4].opcode = OpCodes.Pop;  //remove it and instead remove the surplus grabbacle reference
				}
			}
			return codes.AsEnumerable();
		}

		private static void removeCamUISlider(InteractiveCameraControl __instance)
		{
			if (Config.GetValue(RELEASE_CAM_UI))
			{
				Slider slider = __instance.Slot.GetComponent<Slider>(null, false);
				slider.Destroy();
			}
		}

		private static void addUIEditKey(Harmony harmony)
        {
			MethodInfo original = AccessTools.DeclaredMethod(typeof(Userspace), "OnCommonUpdate", new Type[] { });
			MethodInfo postfix = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(KeybindPostfix));
			harmony.Patch(original, postfix: new HarmonyMethod(postfix));
			Debug("UI Edit Mode keybind added!");
        }
		private static void KeybindPostfix(Userspace __instance)
		{
			if (!__instance.InputInterface.GetKey(Key.Control) && (!__instance.InputInterface.GetKey(Key.Alt) || !__instance.InputInterface.GetKey(Key.AltGr)))
			{
				if (__instance.InputInterface.GetKeyDown(Config.GetValue(UI_EDIT_MODE_KEY)) && __instance.InputInterface.ScreenActive)
				{
					Userspace.UserInterfaceEditMode = !Userspace.UserInterfaceEditMode;
				}
			}
		}

		private static void patchSlotPositioning(Harmony harmony)
        {
			MethodInfo original = AccessTools.Method(typeof(SlotPositioning), nameof(SlotPositioning.PositionInFrontOfUser));
			MethodInfo transpiler = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(positioningTranspiler));
			harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));
			Debug("Slot positioning patched!");
		}

		private static IEnumerable<CodeInstruction> positioningTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            if (codes[0].opcode == OpCodes.Ldarg_0 && codes[1].opcode == OpCodes.Callvirt && codes[2].opcode == OpCodes.Call && (codes[3].opcode == OpCodes.Bne_Un_S || codes[3].opcode == OpCodes.Bne_Un))
			{
				codes[0].opcode = OpCodes.Nop;
				codes[1].opcode = OpCodes.Nop; //replace if with unconditional jump
				codes[2].opcode = OpCodes.Nop;
				codes[3].opcode = OpCodes.Br_S;
			}
			else
			{
				Error("SlotPositioning.PositionInFrontOfUser: Could not patch because of unexpected opcode");
				return instructions;
			}
			return codes;
		}

		private static void patchDash(Harmony harmony)
        {
			MethodInfo originalOnCommonUpdate = AccessTools.DeclaredMethod(typeof(UserspaceRadiantDash), "OnCommonUpdate", new Type[] { });
			MethodInfo originalUpdateOverlayState = AccessTools.DeclaredMethod(typeof(UserspaceRadiantDash), "UpdateOverlayState", new Type[] { });
			MethodInfo radiantDashCommonUpdateTranspiler = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(RadiantDashCommonUpdateTranspiler));
			MethodInfo updateOverlayStatePrefix = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(UpdateOverlayStatePatch));
			MethodInfo onCommonUpdatePostfix = AccessTools.DeclaredMethod(typeof(ThreeDimensionalDashOnScreen), nameof(ScreenProjectionPatch));
			harmony.Patch(originalOnCommonUpdate, transpiler: new HarmonyMethod(radiantDashCommonUpdateTranspiler));
			harmony.Patch(originalOnCommonUpdate, postfix: new HarmonyMethod(onCommonUpdatePostfix));
			harmony.Patch(originalUpdateOverlayState, prefix: new HarmonyMethod(updateOverlayStatePrefix));
			Debug("Dash patched!");
		}

		private static IEnumerable<CodeInstruction> RadiantDashCommonUpdateTranspiler(IEnumerable<CodeInstruction> instructions)
		{  //remove blocking of controls when dash is open
			var codes = new List<CodeInstruction>(instructions);
			for (var i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Ldsfld && ((FieldInfo)codes[i].operand == typeof(KeyboardBlock).GetField("GLOBAL_BLOCK") || (FieldInfo)codes[i].operand == typeof(MouseBlock).GetField("GLOBAL_BLOCK")))
				{
					codes[i - 2].opcode = OpCodes.Nop;
					codes[i - 1].opcode = OpCodes.Nop;
					codes[i].opcode = OpCodes.Nop;
					codes[i + 1].opcode = OpCodes.Nop;
					i += 1;
				}
				if (codes[i].opcode == OpCodes.Callvirt && ((MethodInfo)codes[i].operand == typeof(InputBindingManager).GetMethod("RegisterCursorUnlock") ||
				(MethodInfo)codes[i].operand == typeof(InputBindingManager).GetMethod("UnregisterCursorUnlock")))
				{
					codes[i - 3].opcode = OpCodes.Nop;
					codes[i - 2].opcode = OpCodes.Nop;
					codes[i - 1].opcode = OpCodes.Nop;
					codes[i].opcode = OpCodes.Nop;
				}
			}

			return codes.AsEnumerable();
		}

		private static bool UpdateOverlayStatePatch(UserspaceRadiantDash __instance, SyncRef<Slot> ____notificationsRoot, SyncRef<Slot> ____notificationsHolder)
		{
			RadiantDash dash = __instance.Dash;
			dash.VisualsRoot.SetParent(dash.Slot, false);
			dash.VisualsRoot.SetIdentityTransform();
            ____notificationsHolder.Target.SetParent(____notificationsRoot.Target, keepGlobalTransform: false);
            ____notificationsHolder.Target.SetIdentityTransform();
            return false;
		}

		private static void ScreenProjectionPatch(UserspaceRadiantDash __instance)
		{
			__instance.Dash.ScreenProjection.Value = false;
		}
	}
}
