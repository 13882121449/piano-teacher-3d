using System;
using System.Collections.Generic;
using UnityEngine;
using PianoTeacher3D.Characters;

namespace PianoTeacher3D.Customization
{
    /// <summary>
    /// 外观应用器（方案 9.5 / 11.2 / P1-5）：负责「按槽位备份 → 应用 → 失败回滚 → 可撤销」。
    /// <para>
    /// <b>槽位兼容性</b>：只有 <see cref="ICharacterView.Supports"/> 为真的槽位才允许应用，
    /// 否则返回「该角色不支持此后外观槽位」，UI 不应给不兼容角色显示「应用」。
    /// </para>
    /// <para>
    /// <b>资源所有权</b>（最容易出错的地方）：
    /// ① 传入 <see cref="TryApply"/> 的纹理，成功后所有权转移给本类；失败时仍归调用方（本类不销毁）。
    /// ② 替换成功后才销毁上一份<b>本类拥有的</b>纹理——此时视图已指向新纹理，
    ///    先换引用再释放旧资源，绝不销毁视图仍在使用的纹理。
    /// ③ 视图原本的默认外观不是本类创建的，回滚时用 <see cref="ICharacterView.ClearAppearance"/>，不碰其资源。
    /// </para>
    /// </summary>
    public sealed class GeneratedAppearanceApplier
    {
        /// <summary>可撤销点上限（内存里只保留最近几次，避免长会话累积纹理）。</summary>
        public const int MaxUndoDepth = 4;

        private ICharacterView _view;
        private readonly Dictionary<AppearanceSlot, Texture2D> _applied =
            new Dictionary<AppearanceSlot, Texture2D>();

        /// <summary>本类拥有的纹理（已从调用方接手所有权）。只有「无人引用」时才会被销毁。</summary>
        private readonly HashSet<Texture2D> _owned = new HashSet<Texture2D>();

        private readonly List<UndoEntry> _undo = new List<UndoEntry>();

        private struct UndoEntry
        {
            public AppearanceSlot Slot;
            public Texture2D Previous;   // null = 此前是角色默认外观
        }

        /// <summary>构造。<paramref name="view"/> 允许为 null（用于「先构建服务、角色稍后加载」的时序）。</summary>
        public GeneratedAppearanceApplier(ICharacterView view)
        {
            _view = view;
        }

        /// <summary>当前绑定的角色视图。</summary>
        public ICharacterView View => _view;

        /// <summary>是否有可撤销点。</summary>
        public bool CanUndo => _undo.Count > 0;

        /// <summary>可撤销点数量。</summary>
        public int UndoDepth => _undo.Count;

        /// <summary>该槽位当前是否由本类持有外观（用于 UI 显示「恢复默认」是否可用）。</summary>
        public bool HasApplied(AppearanceSlot slot) => _applied.ContainsKey(slot);

        /// <summary>取该槽位当前由本类持有的纹理（可能为 null；不转移所有权）。</summary>
        public Texture2D GetApplied(AppearanceSlot slot)
            => _applied.TryGetValue(slot, out var texture) ? texture : null;

        /// <summary>
        /// 应用外观。失败时视图外观保持不变（实现方契约保证），并给出可读原因。
        /// 成功时 <paramref name="texture"/> 的所有权转移给本类。
        /// </summary>
        public bool TryApply(AppearanceSlot slot, Texture2D texture, out string error)
        {
            if (texture == null)
            {
                error = "没有可应用的外观图片。请先生成预览。";
                return false;
            }

            if (_view == null)
            {
                error = "还没有可应用外观的角色。请先选择角色再应用。";
                return false;
            }

            if (!_view.Supports(slot))
            {
                error = "该角色不支持此后外观槽位。请换一个支持该槽位的角色，或改选「缩略图」槽位。";
                return false;
            }

            bool hasPrevious = _applied.TryGetValue(slot, out Texture2D previous);

            bool applied;
            string failure;
            try
            {
                applied = _view.ApplyAppearance(slot, texture, out failure);
            }
            catch (Exception)
            {
                RestorePreviousQuietly(slot, hasPrevious, previous);
                error = "应用外观时角色视图抛出异常，已保持原外观。请换一个角色后重试。";
                return false;
            }

            if (!applied)
            {
                // 视图承诺失败时不改外观；若它已应用过一份，再兜一次底，保证「失败 = 原样」
                RestorePreviousQuietly(slot, hasPrevious, previous);
                error = string.IsNullOrEmpty(failure)
                    ? "应用外观失败（角色拒绝了这次替换）。当前外观保持不变。"
                    : failure;
                return false;
            }

            // 所有权转移：从这一刻起由本类负责释放
            _owned.Add(texture);
            // 首份应用也压一个 null 撤销点：撤销链的终点是「角色默认外观」，
            // 这样「应用 → 撤销 → 撤销」能一路退回默认，而不是停在第一份贴图上。
            PushUndo(slot, hasPrevious ? previous : null);
            _applied[slot] = texture;

            // 旧纹理仍被「撤销点」引用，必须保留到该撤销点被挤出历史之后才释放
            // （先换引用 → 再由撤销历史决定何时释放，绝不销毁视图或撤销点还要用的纹理）

            error = string.Empty;
            return true;
        }

        /// <summary>恢复该槽位的角色默认外观；成功后才释放本类持有的纹理。</summary>
        public bool TryRestoreDefault(AppearanceSlot slot, out string error)
        {
            if (_view == null)
            {
                error = "还没有可恢复外观的角色。请先选择角色。";
                return false;
            }

            if (!_view.Supports(slot))
            {
                error = "该角色不支持此后外观槽位，没有需要恢复的内容。";
                return false;
            }

            bool hasApplied = _applied.TryGetValue(slot, out Texture2D current);
            if (!hasApplied)
            {
                error = "这个槽位当前就是角色默认外观，无需恢复。";
                return false;
            }

            try
            {
                if (!_view.ClearAppearance(slot, out string failure))
                {
                    error = string.IsNullOrEmpty(failure)
                        ? "恢复默认外观失败。当前外观保持不变。"
                        : failure;
                    return false;
                }
            }
            catch (Exception)
            {
                error = "恢复默认外观时角色视图抛出异常，已保持当前外观。";
                return false;
            }

            _applied.Remove(slot);

            // 恢复默认后，该槽位的撤销历史没有意义了：一并清掉，并释放只被它们引用的纹理
            PurgeUndoForSlot(slot);
            ReleaseIfUnreferenced(current);
            error = string.Empty;
            return true;
        }

        /// <summary>撤销上一次应用（恢复上一份纹理，或回到角色默认外观）。</summary>
        public bool TryUndo(out string error)
        {
            if (_view == null)
            {
                error = "还没有可撤销外观的角色。";
                return false;
            }

            if (_undo.Count == 0)
            {
                error = "没有可撤销的外观修改。";
                return false;
            }

            UndoEntry entry = _undo[_undo.Count - 1];
            bool hasApplied = _applied.TryGetValue(entry.Slot, out Texture2D current);

            try
            {
                bool ok;
                if (entry.Previous != null) ok = _view.ApplyAppearance(entry.Slot, entry.Previous, out _);
                else ok = _view.ClearAppearance(entry.Slot, out _);

                if (!ok)
                {
                    error = "撤销失败，当前外观保持不变。请稍后再试。";
                    return false;
                }
            }
            catch (Exception)
            {
                error = "撤销时角色视图抛出异常，当前外观保持不变。";
                return false;
            }

            _undo.RemoveAt(_undo.Count - 1);

            if (entry.Previous != null) _applied[entry.Slot] = entry.Previous;
            else _applied.Remove(entry.Slot);

            // 撤销后视图已不再引用当前纹理：只有确认无人引用时才释放
            if (hasApplied) ReleaseIfUnreferenced(current);

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 逐槽位恢复默认并释放自有纹理（删除照片 / 换角色前调用）。
        /// 返回成功恢复的槽位数；任一槽位失败时 <paramref name="error"/> 给出第一个原因。
        /// </summary>
        public int TryRestoreAll(out string error)
        {
            error = string.Empty;
            if (_applied.Count == 0) return 0;

            var slots = new List<AppearanceSlot>(_applied.Keys);
            int restored = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (TryRestoreDefault(slots[i], out string failure)) restored++;
                else if (string.IsNullOrEmpty(error)) error = failure;
            }
            return restored;
        }

        /// <summary>
        /// 换到<b>同一角色</b>的新视图实例（内置包装在切换后会被重建，实例变了但角色 ID 不变）。
        /// <para>
        /// 顺序：先让旧视图同帧解引用（<see cref="ICharacterView.ClearAppearance"/>，不销毁纹理），
        /// 再把同一批纹理重新贴到新视图上——纹理所有权始终在本类，全程不销毁、不复制。
        /// </para>
        /// <para>
        /// 返回 false 只表示「无法迁移」（视图为空 / 角色不同，调用方应改走
        /// <see cref="ReleaseForCharacterSwitch"/> 后新建应用器）；
        /// 部分槽位被新视图拒绝时返回 true，但 <paramref name="warning"/> 给出第一条可读提示。
        /// </para>
        /// </summary>
        public bool TryRetarget(ICharacterView newView, out string warning)
        {
            warning = string.Empty;

            if (newView == null)
            {
                warning = "新的角色视图为空，无法迁移外观。";
                return false;
            }

            if (ReferenceEquals(newView, _view)) return true;

            string oldId = _view != null ? _view.CharacterId : string.Empty;
            if (!string.Equals(oldId, newView.CharacterId, StringComparison.Ordinal))
            {
                warning = "角色已更换，不能把旧角色的外观迁移到新角色。";
                return false;
            }

            DetachFromView();
            _view = newView;

            var slots = new List<AppearanceSlot>(_applied.Keys);
            for (int i = 0; i < slots.Count; i++)
            {
                AppearanceSlot slot = slots[i];
                Texture2D texture = _applied[slot];

                if (!newView.Supports(slot))
                {
                    DropSlot(slot, texture);
                    if (string.IsNullOrEmpty(warning))
                        warning = $"「{SlotName(slot)}」未能迁移：新角色实例不支持该槽位，已恢复默认。";
                    continue;
                }

                bool ok;
                string failure = string.Empty;
                try
                {
                    ok = newView.ApplyAppearance(slot, texture, out failure);
                }
                catch (Exception)
                {
                    ok = false;
                }

                if (!ok)
                {
                    DropSlot(slot, texture);
                    if (string.IsNullOrEmpty(warning))
                    {
                        warning = string.IsNullOrEmpty(failure)
                            ? $"「{SlotName(slot)}」未能迁移：新角色实例拒绝了该外观，已恢复默认。"
                            : $"「{SlotName(slot)}」未能迁移：{failure}";
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 换角色或解绑前调用：先让旧视图同帧解引用，再释放本类全部自有纹理。
        /// 调用后本实例不应继续使用（service 会新建应用器）。
        /// </summary>
        public void ReleaseForCharacterSwitch()
        {
            DetachFromView();
            ReleaseAppliedResources();
            _view = null;
        }

        /// <summary>
        /// 释放本类持有的全部纹理引用（不调用视图）。
        /// <b>调用前必须已通过 ClearAppearance / 角色销毁让视图不再引用这些纹理</b>，否则会销毁在用资源。
        /// </summary>
        public void ReleaseAppliedResources()
        {
            var textures = new List<Texture2D>(_owned);
            _owned.Clear();
            _applied.Clear();
            _undo.Clear();

            for (int i = 0; i < textures.Count; i++) DestroyTexture(textures[i]);
        }

        // ==================== 内部 ====================

        /// <summary>槽位的中文名（用于可读提示，避免把枚举名抛给用户）。</summary>
        private static string SlotName(AppearanceSlot slot) => slot switch
        {
            AppearanceSlot.Thumbnail => "缩略图",
            AppearanceSlot.FaceTexture => "脸部贴图",
            AppearanceSlot.HeadMaterial => "头部材质",
            AppearanceSlot.BodyTint => "整体色调",
            _ => "外观"
        };

        /// <summary>让旧视图逐槽位解引用（同帧生效），但不销毁纹理——纹理仍归本类。</summary>
        private void DetachFromView()
        {
            if (_view == null || _applied.Count == 0) return;

            var slots = new List<AppearanceSlot>(_applied.Keys);
            for (int i = 0; i < slots.Count; i++)
            {
                try
                {
                    _view.ClearAppearance(slots[i], out _);
                }
                catch (Exception)
                {
                    // 旧视图已不可用也没关系：下面照常回收纹理
                }
            }
        }

        /// <summary>丢弃某槽位外观：移除引用、清该槽位撤销历史、按需释放纹理。</summary>
        private void DropSlot(AppearanceSlot slot, Texture2D texture)
        {
            _applied.Remove(slot);
            PurgeUndoForSlot(slot);
            ReleaseIfUnreferenced(texture);
        }

        /// <summary>清掉某槽位的撤销历史，并释放只被这些历史引用的纹理。</summary>
        private void PurgeUndoForSlot(AppearanceSlot slot)
        {
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                if (_undo[i].Slot != slot) continue;
                Texture2D previous = _undo[i].Previous;
                _undo.RemoveAt(i);
                ReleaseIfUnreferenced(previous);
            }
        }

        /// <summary>没有任何槽位或撤销点引用该纹理时释放它（避免销毁在用资源）。</summary>
        private void ReleaseIfUnreferenced(Texture2D texture)
        {
            if (texture == null) return;

            foreach (var pair in _applied)
            {
                if (ReferenceEquals(pair.Value, texture)) return;
            }

            for (int i = 0; i < _undo.Count; i++)
            {
                if (ReferenceEquals(_undo[i].Previous, texture)) return;
            }

            if (!_owned.Remove(texture)) return;
            DestroyTexture(texture);
        }

        private void RestorePreviousQuietly(AppearanceSlot slot, bool hasPrevious, Texture2D previous)
        {
            if (!hasPrevious || previous == null) return;
            try
            {
                _view.ApplyAppearance(slot, previous, out _);
            }
            catch (Exception)
            {
                // 兜底还原失败时不再向上抛：外层已经把失败原因返回给调用方
            }
        }

        private void PushUndo(AppearanceSlot slot, Texture2D previous)
        {
            _undo.Add(new UndoEntry { Slot = slot, Previous = previous });

            // 超出上限的旧撤销点被挤出：它引用的纹理如果无人再用，就在这里释放（内存有界）
            while (_undo.Count > MaxUndoDepth)
            {
                Texture2D dropped = _undo[0].Previous;
                _undo.RemoveAt(0);
                ReleaseIfUnreferenced(dropped);
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
