using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// UI 层临时特效实例的通用处理。粒子挂到 UI 下不吃 Canvas 的层级，得手动写 sortingOrder；
    /// 从对象池或 prefab 复用出来的实例还得把上一次留下的粒子、拖尾清干净。
    /// </summary>
    public static class UiFx
    {
        public static void ApplySorting(GameObject go, int order)
        {
            if (go == null)
            {
                return;
            }

            var particles = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (var i = 0; i < particles.Length; i++)
            {
                particles[i].sortingOrder = order;
            }

            var trails = go.GetComponentsInChildren<TrailRenderer>(true);
            for (var i = 0; i < trails.Length; i++)
            {
                trails[i].sortingOrder = order;
            }
        }

        public static void RestartParticles(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }

        public static void ClearTrails(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var trails = go.GetComponentsInChildren<TrailRenderer>(true);
            for (var i = 0; i < trails.Length; i++)
            {
                trails[i].Clear();
            }
        }
    }
}
