using System;
using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 角色独占资源的登记与释放（方案 11.2 / P1-5）。
    /// <para>
    /// <b>所有权规则</b>（这是本类唯一需要记住的约定）：
    /// ① 只有<b>角色自己创建</b>的对象才登记进来：<c>new Material/new Mesh/new Texture2D</c>、
    ///    <c>Object.Instantiate</c> 出来的 GameObject、运行时生成的 <c>RenderTexture</c>；
    /// ② <b>通过 <c>Resources.Load</c> / 预制体引用拿到的资产属于共享资源，禁止登记</b>——
    ///    它们由 AssetBundle / Resources 管理器统一释放，重复销毁会导致其他角色材质变紫；
    /// ③ 若某个资源先被登记、后来发现是共享的，用 <see cref="Release"/> 撤销登记（不改所有权，只是不再销毁）；
    /// ④ <see cref="Dispose"/> 只释放自己登记过的对象，且可重复调用（第二次是空操作）。
    /// </para>
    /// <para>
    /// 释放顺序为<b>登记顺序的逆序</b>（后创建的对象通常引用先创建的对象，逆序销毁不会留下悬空引用）。
    /// </para>
    /// </summary>
    public sealed class CharacterResourceScope : IDisposable
    {
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly string _ownerName;

        private bool _disposed;
        private int _releasedCount;

        /// <summary>创建一个资源作用域。<paramref name="ownerName"/> 仅用于日志定位（可为 null）。</summary>
        public CharacterResourceScope(string ownerName = null)
        {
            _ownerName = ownerName;
        }

        /// <summary>当前登记的对象数量（含已释放但仍在列表中的条目）。</summary>
        public int OwnedCount => _owned.Count;

        /// <summary>已被实际销毁 / 移交的对象数量（供测试断言「不重复释放」）。</summary>
        public int ReleasedCount => _releasedCount;

        /// <summary>是否已释放。</summary>
        public bool IsDisposed => _disposed;

        /// <summary>登记一个独占对象。null 与重复登记会被静默忽略。</summary>
        public void Own(UnityEngine.Object obj)
        {
            if (obj == null) return;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (ReferenceEquals(_owned[i], obj)) return;
            }
            _owned.Add(obj);
        }

        /// <summary>批量登记。</summary>
        public void OwnRange(IEnumerable<UnityEngine.Object> objects)
        {
            if (objects == null) return;
            foreach (var obj in objects) Own(obj);
        }

        /// <summary>
        /// 撤销登记：把对象从「我拥有」列表里移除，<see cref="Dispose"/> 不会再销毁它。
        /// 用于「先登记后确认是共享资产」以及资源转交的场景。返回是否确实移除了。
        /// </summary>
        public bool Release(UnityEngine.Object obj)
        {
            if (obj == null) return false;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (!ReferenceEquals(_owned[i], obj)) continue;
                _owned.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>释放全部独占资源。可重复调用。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                var obj = _owned[i];
                if (obj == null) continue;   // 已被 Unity 因场景卸载销毁

                if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
                else UnityEngine.Object.DestroyImmediate(obj);

                _releasedCount++;
            }
            _owned.Clear();
        }

        /// <summary>便于日志输出。</summary>
        public override string ToString() =>
            $"CharacterResourceScope({_ownerName ?? "unknown"}) owned={OwnedCount} released={ReleasedCount}";
    }
}
