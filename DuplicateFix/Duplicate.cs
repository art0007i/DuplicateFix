using Elements.Core;
using FrooxEngine;
using FrooxEngine.Undo;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DuplicateFix;

public static class DuplicateExtensions
{
    private static readonly Type InternalReferences = typeof(Worker).GetNestedType("InternalReferences", AccessTools.all);
    private static readonly MethodInfo RunDuplicate = typeof(Component).GetMethod("RunDuplicate", AccessTools.all);

    // Literally just a copy paste of Slot.Duplicate but it duplicates several slots at same time
    public static void MultiDuplicate(this IEnumerable<Slot> toDuplicate, List<Slot> newSlots, Slot duplicateRoot = null, bool keepGlobalTransform = true, DuplicationSettings settings = null)
    {
        if (toDuplicate.Any(x => x.IsRootSlot))
        {
            throw new Exception("Cannot duplicate root slot");
        }
        if (duplicateRoot != null && toDuplicate.Any(x => duplicateRoot.IsChildOf(x)))
        {
            throw new Exception("Target for the duplicate hierarchy cannot be within the hierarchy of the source");
        }
        var internalReferences = InternalReferences.GetConstructor(new Type[] { }).Invoke(null);
        HashSet<ISyncRef> hashSet = Pool.BorrowHashSet<ISyncRef>();
        HashSet<Slot> hashSet2 = Pool.BorrowHashSet<Slot>();
        List<Action> postDuplication = Pool.BorrowList<Action>();
        toDuplicate.Do(x => x.ForeachComponentInChildren(delegate (IDuplicationHandler h)
        {
            h.OnBeforeDuplicate(x, out var onDuplicated);
            if (onDuplicated != null)
            {
                postDuplication.Add(onDuplicated);
            }
        }, includeLocal: false, cacheItems: true));
        toDuplicate.Do(x => x.GenerateHierarchy(hashSet2));
        toDuplicate.Do(x => Traverse.Create(x).Method("CollectInternalReferences", x, internalReferences, hashSet, hashSet2).GetValue());
        foreach (var slot in toDuplicate)
        {
            newSlots.Add((Slot)typeof(Slot).GetMethod("InternalDuplicate", AccessTools.all).Invoke(slot, new object[] { duplicateRoot ?? slot.Parent ?? slot.World.RootSlot, internalReferences, hashSet, settings }));
        }
        if (keepGlobalTransform)
        {
            var enumerator = toDuplicate.GetEnumerator();
            for (int i = 0; i < newSlots.Count; i++)
            {
                if (!enumerator.MoveNext()) break;
                newSlots[i].CopyTransform(enumerator.Current);
            }
        }
        Traverse.Create(internalReferences).Method("TransferReferences", false).GetValue();
        List<Component> list = Pool.BorrowList<Component>();
        newSlots.Do(x => x.GetComponentsInChildren(list));
        foreach (Component item in list)
        {
            RunDuplicate.Invoke(item, null);
        }
        Pool.Return(ref list);
        Pool.Return(ref hashSet);
        Traverse.Create(internalReferences).Method("Dispose");
        foreach (Action item2 in postDuplication)
        {
            item2();
        }
        Pool.Return(ref postDuplication);
    }
}
