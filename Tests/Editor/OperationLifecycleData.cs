namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;

    public sealed class OperationLifecycleData : BaseDataObject
    {
        /*
            Test-only shared hooks observe callback order and reentrancy. Each fixture
            resets them through its cleanup scope, including captured operation owners.
        */
        public static Action Callback;
        public static int HookCount;
        public static bool ThrowAfterCreate;
        public string marker;
        public string payload;

        public override void BeforeCreate()
        {
            HookCount++;
            Callback?.Invoke();
        }

        public override void AfterCreate()
        {
            base.AfterCreate();
            HookCount++;
            marker = "created";
            if (ThrowAfterCreate)
            {
                throw new InvalidOperationException("Expected operation lifecycle failure.");
            }
        }

        public override void BeforeClone(ScriptableObject previous)
        {
            base.BeforeClone(previous);
            HookCount++;
        }

        public override void AfterClone(ScriptableObject previous)
        {
            base.AfterClone(previous);
            HookCount++;
            marker = "cloned";
        }

        public override void BeforeRename(string newName)
        {
            HookCount++;
        }

        public override void AfterRename(string newName)
        {
            HookCount++;
            marker = newName;
        }
    }
}
