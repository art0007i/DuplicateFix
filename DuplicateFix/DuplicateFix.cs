using HarmonyLib;
using ResoniteModLoader;
using System;
using FrooxEngine;
using FrooxEngine.Undo;
using Elements.Core;
using System.Collections.Generic;
using System.Linq;

namespace DuplicateFix;

public class DuplicateFix : ResoniteMod
{
    public override string Name => "DuplicateFix";
    public override string Author => "art0007i";
    public override string Version => "1.0.4";
    public override string Link => "https://github.com/art0007i/DuplicateFix/";

    [AutoRegisterConfigKey]
    public static ModConfigurationKey<bool> KEY_ENABLED = new("enabled", "When true the mod will be enabled.", () => true);

    public static ModConfiguration config;

    public override void OnEngineInit()
    {
        config = GetConfiguration();
        Harmony harmony = new Harmony("me.art0007i.DuplicateFix");
        harmony.PatchAll();
    }

    [HarmonyPatch(typeof(Grabber), nameof(Grabber.OnFocusChanged))]
    class UserspaceTransferPatch
    {
        public static bool Prefix(Grabber __instance, World.WorldFocus focus)
        {
            if (!config.GetValue(KEY_ENABLED)) return true;

            if (!__instance.World.CanTransferObjectsOut())
            {
                return false;
            }
            __instance.BeforeUserspaceTransfer?.Invoke();
            Traverse.Create(__instance).Method("CleanupGrabbed").GetValue();
            if (focus != 0 || !__instance.IsHoldingObjects)
            {
                return false;
            }
            foreach (IGrabbable grabbedObject in __instance.GrabbedObjects)
            {
                if (grabbedObject.Slot.GetComponentInChildren((IItemPermissions p) => !p.CanSave) != null)
                {
                    return false;
                }
            }
            float3 a = __instance.HolderSlot.LocalPosition;
            float3 b = float3.Zero;
            if (a != b)
            {
                float3 b2 = __instance.HolderSlot.LocalPosition;
                __instance.HolderSlot.LocalPosition = float3.Zero;
                foreach (Slot child in __instance.HolderSlot.Children)
                {
                    a = child.LocalPosition;
                    child.LocalPosition = a + b2;
                }
            }

            if (Userspace.TryTransferToUserspaceGrabber(__instance.HolderSlot, __instance.LinkingKey))
            {
                __instance.DestroyGrabbed();
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(Userspace), nameof(Userspace.Paste))]
    class UserspacePastePatch
    {
        public static bool Prefix(Userspace __instance, ref Job<Slot> __result, SavedGraph data, Slot source, float3 userspacePos, floatQ userspaceRot, float3 userspaceScale, World targetWorld)
        {
            if (!config.GetValue(KEY_ENABLED)) return true;

            Job<Slot> task = new Job<Slot>();
            World world = targetWorld ?? Engine.Current.WorldManager.FocusedWorld;
            world.RunSynchronously(delegate
            {
                Slot slot = null;
                if (world.CanSpawnObjects())
                {
                    float3 globalPosition = WorldManager.TransferPoint(userspacePos, Userspace.Current.World, world);
                    floatQ globalRotation = WorldManager.TransferRotation(userspaceRot, Userspace.Current.World, world);
                    float3 globalScale = WorldManager.TransferScale(userspaceScale, Userspace.Current.World, world);
                    if (source?.World == world && !source.IsDestroyed)
                    {
                        source.ActiveSelf = true;
                        source.GlobalPosition = globalPosition;
                        source.GlobalRotation = globalRotation;
                        source.GlobalScale = globalScale;
                        task.SetResultAndFinish(source);
                    }
                    else
                    {
                        slot = world.AddSlot("Paste");
                        slot.LoadObject(data.Root, null);
                        slot.GlobalPosition = globalPosition;
                        slot.GlobalRotation = globalRotation;
                        slot.GlobalScale = globalScale;
                        Traverse.Create(slot).Method("RunOnPaste").GetValue();
                        if(slot.Name == "Holder")
                        {
                            slot.Destroy(slot.Parent, false);
                        }
                    }
                }
                if (source != null && !source.IsDestroyed)
                {
                    source.World.RunSynchronously(source.Destroy);
                }
                task.SetResultAndFinish(slot);
            });
            __result = task;
            return false;
        }
    }

    [HarmonyPatch(typeof(InteractionHandler), "DuplicateGrabbed", new Type[] { })]
    class DuplicateFixPatch
    {
        public static bool Prefix(InteractionHandler __instance)
        {
            if (!config.GetValue(KEY_ENABLED)) return true;

            __instance.World.BeginUndoBatch("Undo.DuplicateGrabbed".AsLocaleKey());
            List<Slot> toDuplicate = Pool.BorrowList<Slot>();
            List<Slot> newSlots = Pool.BorrowList<Slot>();
            try
            {
                foreach (IGrabbable grabbedObject in __instance.Grabber.GrabbedObjects)
                {
                    if (__instance.Grabber.GrabbableGetComponentInParents<IDuplicateBlock>(grabbedObject.Slot, null, excludeDisabled: true) != null)
                    {
                        continue;
                    }
                    toDuplicate.Add(grabbedObject.Slot.GetObjectRoot(__instance.Grabber.Slot));
                }

                toDuplicate.MultiDuplicate(newSlots);
                newSlots.Do(x => x.CreateSpawnUndoPoint());
                newSlots.SelectMany(x => x.GetComponentsInChildren<IGrabbable>()).Do(x => { if (x.IsGrabbed) x.Release(x.Grabber); });
            }
            catch (Exception ex)
            {
                __instance.Debug.Error("Exception duplicating items!\n" + ex);
            }
            Pool.Return(ref newSlots);
            Pool.Return(ref toDuplicate);
            __instance.World.EndUndoBatch();

            return false;
        }
    }
}
