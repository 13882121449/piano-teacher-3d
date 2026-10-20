using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// <see cref="CharacterSwitchService"/> 的 MonoBehaviour 宿主：用协程把「逐帧 Tick」推到主线程上。
    /// <para>
    /// 之所以要宿主而不是让服务自己继承 MonoBehaviour：服务是纯 C# 状态机，
    /// 测试可以直接 <c>new</c> 出来逐步 <c>Tick()</c>，不需要进 PlayMode、不依赖对象生命周期。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterSwitchHost : MonoBehaviour
    {
        private CharacterSwitchService _service;
        private Coroutine _running;

        /// <summary>内部服务实例（未初始化时为 null）。</summary>
        public CharacterSwitchService Service => _service;

        /// <summary>初始化（可重复调用，只生效一次）。</summary>
        public void Initialize(ICharacterViewFactory factory, CharacterRegistry registry = null)
        {
            if (_service != null) return;
            _service = new CharacterSwitchService(factory, registry);
        }

        /// <summary>发起切换并按需启动协程。</summary>
        public void Request(CharacterProfile profile)
        {
            if (_service == null)
            {
                Debug.LogWarning("[CharacterSwitchHost] 未初始化，忽略切换请求。");
                return;
            }
            if (!_service.Request(profile)) return;

            if (!_service.IsBusy) return;

            if (isActiveAndEnabled)
            {
                if (_running == null) _running = StartCoroutine(Drive());
                return;
            }

            // 宿主未启用（对象被隐藏）时协程不会跑：退回同步步进，避免切换卡在 Loading
            while (_service.Tick()) { }
        }

        private System.Collections.IEnumerator Drive()
        {
            yield return _service.RunToCompletion();
            _running = null;
        }

        private void OnDestroy()
        {
            if (_running != null) StopCoroutine(_running);
            _running = null;
            _service?.Dispose();
            _service = null;
        }
    }
}
