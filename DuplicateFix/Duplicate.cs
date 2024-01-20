using Elements.Core;
using FrooxEngine;
using FrooxEngine.Undo;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DuplicateFix;

public static class DuplicateExtensions
{
    // Literally just a copy paste of Slot.Duplicate but with a tiny snippet added to the middle
    // would probably do this using a transpiler but it would require some kind of additional function argument to whether it should create undo steps
    // or I could check stack traces to see if it's called from within my code but that's a little jank

    public static Slot UndoableChildrenDuplicate(this Slot toDuplicate, Slot duplicateRoot = null, bool keepGlobalTransform = true)
    {
        if (toDuplicate.IsRootSlot)
        {
            throw new Exception("Cannot duplicate root slot");
        }
        if (duplicateRoot == null)
        {
            duplicateRoot = toDuplicate.Parent ?? toDuplicate.World.RootSlot;
        }
        else if (duplicateRoot.IsChildOf(toDuplicate))
        {
            throw new Exception("Target for the duplicate hierarchy cannot be within the hierarchy of the source");
        }
        HashSet<ISyncRef> hashSet = Pool.BorrowHashSet<ISyncRef>();
        HashSet<Slot> hashSet2 = Pool.BorrowHashSet<Slot>();
        List<Action> postDuplication = Pool.BorrowList<Action>();
        toDuplicate.ForeachComponentInChildren(delegate (IDuplicationHandler h)
        {
            h.OnBeforeDuplicate(toDuplicate, out var onDuplicated);
            if (onDuplicated != null)
            {
                postDuplication.Add(onDuplicated);
            }
        }, includeLocal: false, cacheItems: true);
        toDuplicate.GenerateHierarchy(hashSet2);
        //InternalReferences internalReferences = new InternalReferences();
        var internalReferences = typeof(Worker).GetNestedType("InternalReferences", AccessTools.all).GetConstructor(new Type[] { }).Invoke(null);
        DuplicateFix.Msg("traverse1");
        Traverse.Create(toDuplicate).Method("CollectInternalReferences", toDuplicate, internalReferences, hashSet, hashSet2).GetValue();
        DuplicateFix.Msg("traverse2");
        Slot slot = (Slot)Traverse.Create(toDuplicate).Method("InternalDuplicate", duplicateRoot, internalReferences, hashSet).GetValue();
        if (keepGlobalTransform)
        {
            slot.CopyTransform(toDuplicate);
        }
        DuplicateFix.Msg("traverse3");
        Traverse.Create(internalReferences).Method("TransferReferences", false).GetValue();
        List<Component> list = Pool.BorrowList<Component>();
        slot.GetComponentsInChildren(list);
        var runDuplicateMethod = typeof(Component).GetMethod("RunDuplicate", AccessTools.all);
        // arti stuff begin
        foreach (var child in slot.Children)
        {
            child.CreateSpawnUndoPoint();
        }
        // arti stuff end
        foreach (Component item in list)
        {
            runDuplicateMethod.Invoke(item, null);
        }
        Pool.Return(ref list);
        Pool.Return(ref hashSet);
        DuplicateFix.Msg("traverse4");
        Traverse.Create(internalReferences).Method("Dispose").GetValue();
        foreach (Action item2 in postDuplication)
        {
            item2();
        }
        Pool.Return(ref postDuplication);
        return slot;
    }
}
