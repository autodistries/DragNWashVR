using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace WalkNWash.VRCompanion
{
    // Manual Camera.Render in script LateUpdate precedes Unity's skinning stage.
    // Keep the current loop (including other plugins), and add only our callback.
    internal sealed class VrRenderStage : IDisposable
    {
        internal VrRenderStage(PlayerLoopSystem.UpdateFunction render)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            if (!Insert(ref loop, render)) throw new InvalidOperationException("Unity skinning stage not found");
            PlayerLoop.SetPlayerLoop(loop);
        }

        internal static bool Insert(ref PlayerLoopSystem loop, PlayerLoopSystem.UpdateFunction render)
        {
            if (loop.subSystemList == null) return false;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == typeof(PostLateUpdate.UpdateAllSkinnedMeshes))
                {
                    var list = new List<PlayerLoopSystem>(loop.subSystemList);
                    list.Insert(i + 1, new PlayerLoopSystem { type = typeof(VrRenderStage), updateDelegate = render });
                    loop.subSystemList = list.ToArray();
                    return true;
                }
                if (Insert(ref loop.subSystemList[i], render)) return true;
            }
            return false;
        }

        internal static void Remove(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return;
            var list = new List<PlayerLoopSystem>();
            foreach (var child in loop.subSystemList)
            {
                if (child.type == typeof(VrRenderStage)) continue;
                var copy = child; Remove(ref copy); list.Add(copy);
            }
            loop.subSystemList = list.ToArray();
        }

        public void Dispose()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop); PlayerLoop.SetPlayerLoop(loop);
        }
    }
}
