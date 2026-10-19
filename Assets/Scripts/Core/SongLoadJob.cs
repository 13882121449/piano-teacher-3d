using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 曲目加载阶段（优化方案 §7-8 / P1-03「明确状态 Queued → Reading → Validating → Analyzing → Ready / Failed / Cancelled」）。
    /// <para>
    /// 只表达「现在在做什么」，<b>不提供百分比</b>：解析进度无法可靠估算，虚构百分比会误导用户。
    /// </para>
    /// </summary>
    public enum SongLoadStage
    {
        /// <summary>没有进行中的加载。</summary>
        Idle = 0,

        /// <summary>已排队，后台线程尚未开始。</summary>
        Queued = 1,

        /// <summary>读取文件信息（存在性 / 大小 / 扩展名）。</summary>
        Reading = 2,

        /// <summary>校验与解析（读取字节、SMF 解析、上限检查）。</summary>
        Validating = 3,

        /// <summary>曲目分析（统计指标与练习建议）。</summary>
        Analyzing = 4,

        /// <summary>解析与分析完成，等待主线程提交。</summary>
        Ready = 5,

        /// <summary>校验/解析失败（结果里带结构化 <see cref="ImportCheck"/>）。</summary>
        Failed = 6,

        /// <summary>用户取消（结果被丢弃，旧曲不受影响）。</summary>
        Cancelled = 7
    }

    /// <summary>
    /// 可取消的分阶段曲目加载作业（优化方案 §7-8）。
    /// <para>
    /// <b>线程边界</b>：本类只在后台线程执行<b>纯 C#</b> 的文件读取、MIDI 校验/解析与曲目分析
    /// （<see cref="MidiFileValidator"/> / <see cref="MidiParser"/> / <see cref="SongAnalyzer"/> 都不依赖 UnityEngine），
    /// <b>绝不</b>触碰 GameObject、Material、UI、<c>JsonUtility</c> 或任何场景对象；
    /// 这些提交动作全部由 <see cref="LessonSessionController"/> 在主线程完成。
    /// </para>
    /// <para>
    /// <b>取消语义</b>：取消在阶段边界生效（解析单个文件时无法中途打断——解析器不是协作式的）。
    /// 取消后结果被丢弃，会话保持旧曲与旧播放状态；不会产生半加载状态。
    /// </para>
    /// <para>
    /// <b>不抛异常</b>：后台线程里的任何异常都被收进 <see cref="FailureDetail"/> 并落到
    /// <see cref="SongLoadStage.Failed"/>，避免 <c>Task</c> 的未观察异常污染后续运行。
    /// </para>
    /// </summary>
    public sealed class SongLoadJob : IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        // 后台线程写、主线程读：volatile 保证可见性；_done 最后写，保证前面的结果字段已发布
        private volatile SongLoadStage _stage = SongLoadStage.Queued;
        private volatile bool _done;
        private int _disposed;

        /// <summary>待加载的源文件路径。</summary>
        public string SourcePath { get; }

        /// <summary>当前阶段（可随时从主线程读取）。</summary>
        public SongLoadStage Stage => _stage;

        /// <summary>作业是否已结束（Ready / Failed / Cancelled 之一）。</summary>
        public bool IsDone => _done;

        /// <summary>校验结果（仅 <see cref="SongLoadStage.Ready"/> / <see cref="SongLoadStage.Failed"/> 时有效）。</summary>
        public ImportCheck Check { get; private set; }

        /// <summary>曲目分析结果（仅 <see cref="SongLoadStage.Ready"/> 时非 null）。</summary>
        public SongAnalysis Analysis { get; private set; }

        /// <summary>后台异常的类型名（仅用于诊断，不含用户路径）。</summary>
        public string FailureDetail { get; private set; }

        public SongLoadJob(string sourcePath)
        {
            SourcePath = sourcePath ?? string.Empty;
        }

        /// <summary>启动后台作业。同一实例只应调用一次（重复调用会被 <see cref="IsDone"/> 之前的阶段忽略）。</summary>
        public void Start()
        {
            if (_done) return;
            var token = _cts.Token;

            // 说明：不使用 async/await，直接在线程池上跑同步流程，避免 Unity 主线程同步上下文的干扰
            Task.Run(() => Run(token), token);
        }

        /// <summary>
        /// 请求取消。作业可能已经完成（此时不会有任何效果），调用方应以
        /// <see cref="IsDone"/> 为准决定是否立即收尾。
        /// </summary>
        public void Cancel()
        {
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { /* 已释放：视为已完成，无需再取消 */ }
        }

        private void Run(CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested) { Finish(SongLoadStage.Cancelled); return; }

                // ---- Reading：只做轻量文件信息预检（真正的字节读取在校验内部，失败由它给出结构化原因）----
                _stage = SongLoadStage.Reading;
                TryReadFileInfo(out _);
                if (token.IsCancellationRequested) { Finish(SongLoadStage.Cancelled); return; }

                // ---- Validating：读取字节 + SMF 解析 + 上限检查 ----
                _stage = SongLoadStage.Validating;
                var check = MidiFileValidator.Validate(SourcePath);
                if (token.IsCancellationRequested) { Finish(SongLoadStage.Cancelled); return; }

                if (!check.Ok || check.Parse == null)
                {
                    Check = check;
                    Finish(SongLoadStage.Failed);
                    return;
                }

                // ---- Analyzing：纯统计与规则建议 ----
                _stage = SongLoadStage.Analyzing;
                var analysis = SongAnalyzer.Analyze(
                    check.Parse.Notes, check.Parse.TempoChanges, check.Parse.TotalDurationSeconds);
                if (token.IsCancellationRequested) { Finish(SongLoadStage.Cancelled); return; }

                Check = check;
                Analysis = analysis;
                Finish(SongLoadStage.Ready);
            }
            catch (Exception ex)
            {
                FailureDetail = ex.GetType().Name;
                Finish(SongLoadStage.Failed);
            }
        }

        /// <summary>
        /// 记录最终阶段。<b>最后一次写 <c>_done</c></b>，因此主线程看到 <see cref="IsDone"/> 时
        /// <see cref="Check"/> / <see cref="Analysis"/> 一定已经写入。
        /// </summary>
        private void Finish(SongLoadStage stage)
        {
            _stage = stage;
            _done = true;
        }

        /// <summary>读取文件信息（大小 / 存在性）；失败不抛异常（错误由校验阶段统一映射）。</summary>
        private bool TryReadFileInfo(out long size)
        {
            size = 0;
            try
            {
                if (!File.Exists(SourcePath)) return false;
                size = new FileInfo(SourcePath).Length;
                return size > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>阶段的中文文案（UI 直接显示，不编造百分比）。</summary>
        public static string StageLabel(SongLoadStage stage) => stage switch
        {
            SongLoadStage.Queued => "排队等待…",
            SongLoadStage.Reading => "读取文件…",
            SongLoadStage.Validating => "校验并解析 MIDI…",
            SongLoadStage.Analyzing => "分析曲目…",
            SongLoadStage.Ready => "已就绪",
            SongLoadStage.Failed => "加载失败",
            SongLoadStage.Cancelled => "已取消",
            _ => string.Empty
        };

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cts.Dispose(); }
            catch (Exception) { /* 释放失败不影响会话状态 */ }
        }
    }
}
