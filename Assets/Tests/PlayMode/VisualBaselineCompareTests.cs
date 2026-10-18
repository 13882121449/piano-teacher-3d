using System;
using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 视觉基线的自动像素比较（对应**方案 11.2**「自动像素差异可作为辅助，但不能因为光照细微变化导致所有
    /// 截图测试失去意义；需设定容差或关注区域」与**审查 6.1**「像素差异只能作为辅助，不能完全取代人工看图」）。
    ///
    /// <para>
    /// <b>重要：像素差异只作辅助，不能替代人工看图。</b>
    /// 光照、材质、抗锯齿、显卡驱动与窗口尺寸的任何变化都会让同一构图的像素值发生变化；
    /// 因此本测试明确声明：
    /// <list type="number">
    /// <item>它是<b>人工核对之后</b>的第二道护栏，不是「画面对不对」的判据；</item>
    /// <item>默认带 <see cref="ExplicitAttribute"/>，<b>不随常规 PlayMode 套件执行</b>，避免默认拖慢以及
    ///       因光照/驱动的细微差异造成 flaky；</item>
    /// <item>绝不会把「当前截图」自动当成基线 —— 基线必须由人看图确认后手动复制进
    ///       <c>Docs/screenshots/baseline/</c>；缺基线时只 <see cref="Assert.Ignore(string)"/>。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// 依赖 <c>Tools/visual_diff.py</c>（纯 Python 3 标准库，不依赖 numpy / Pillow）。
    /// 找不到 python 解释器时同样只 <c>Ignore</c>，不让测试因环境缺失而失败。
    /// </para>
    ///
    /// <para><b>运行方式</b>（须在已建立基线之后）：<br/>
    /// <c>python Tools/verify.py --compare-visual</c>（该阶段不加 <c>-nographics</c>，需 GPU）<br/>
    /// 或直跑
    /// <c>Unity.exe -batchmode -runTests -testPlatform PlayMode -testFilter CompareAgainstBaseline</c>。</para>
    /// </summary>
    [Category("Visual")]
    public class VisualBaselineCompareTests
    {
        /// <summary>默认逐像素容差（RGB 通道平均绝对差，0-255）。</summary>
        private const float DefaultTolerance = 6f;

        /// <summary>默认允许的超差像素比例。</summary>
        private const float DefaultMaxDiffRatio = 0.02f;

        /// <summary>外部进程（python 比较脚本）的超时时间（毫秒）。</summary>
        private const int DiffTimeoutMs = 300000;

        /// <summary>协程跑一次外部比较需要几步（避免 UnityTest 被判定为“无 yield”）。</summary>
        private sealed class DiffRun
        {
            public bool Started;
            public bool TimedOut;
            public int ExitCode = -1;
            public string StdOut = "";
            public string StdErr = "";
            public string SkipReason;
        }

        [UnityTest]
        [Explicit]
        public IEnumerator CompareAgainstBaseline()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string shotsDir = Path.Combine(projectRoot, "Docs", "screenshots");
            string baselineDir = Path.Combine(shotsDir, "baseline");
            string scriptPath = Path.Combine(projectRoot, "Tools", "visual_diff.py");
            string reportPath = Path.Combine(projectRoot, "_verify_out", "visual_diff.json");

            // ---- 1. 基线存在性（缺基线 → Ignore，绝不自动把当前截图当基线） ----
            if (!Directory.Exists(baselineDir))
            {
                Assert.Ignore("未建立基线，先运行 CaptureLessonViews 生成基线（缺少目录：" + baselineDir + "）");
                yield break;
            }

            string[] baselineFiles = Directory.GetFiles(baselineDir, "*.png");
            if (baselineFiles.Length == 0)
            {
                Assert.Ignore("基线目录里没有任何 PNG，先运行 CaptureLessonViews 生成基线（" + baselineDir + "）");
                yield break;
            }
            if (!File.Exists(scriptPath))
            {
                Assert.Ignore("未找到像素比较脚本：" + scriptPath);
                yield break;
            }

            // ---- 2. python 解释器（缺失 → Ignore 并说明） ----
            string python = FindPython();
            if (python == null)
            {
                Assert.Ignore("未找到 python / python3 解释器，无法执行 Tools/visual_diff.py（可安装 Python 3.8+ 或把 python 加入 PATH）");
                yield break;
            }

            // ---- 3. 执行比较（阻塞式调用外部进程，结果一次性读回） ----
            string arguments = string.Format(
                "{0} {1} --tolerance {2} --max-diff-ratio {3} --json {4}",
                Quote(scriptPath), Quote(baselineDir), DefaultTolerance,
                DefaultMaxDiffRatio.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                Quote(reportPath));
            // 注意：--roi / --exclude-roi 可按需在此追加（例如排除底部控制栏与右上角状态区），
            // 用法见 Tools/visual_diff.py 的文档字符串。

            DiffRun run = RunDiff(python, arguments, projectRoot);
            if (run.SkipReason != null)
            {
                Assert.Ignore(run.SkipReason);
                yield break;
            }

            Debug.Log("[VisualBaseline] 基线目录：" + baselineDir + "（" + baselineFiles.Length + " 张）");
            Debug.Log("[VisualBaseline] python：" + python + " - " + arguments);
            Debug.Log("[VisualBaseline] visual_diff.py 输出：\n" + run.StdOut);
            if (!string.IsNullOrEmpty(run.StdErr))
                Debug.LogWarning("[VisualBaseline] visual_diff.py stderr：\n" + run.StdErr);

            yield return null; // 协程让出一帧（同时保证本方法是一个合法迭代器）

            // ---- 4. 断言（失败信息里带文件名与 diff 比例） ----
            if (run.TimedOut)
            {
                Assert.Fail(string.Format("visual_diff.py 运行超时（>{0}s）。日志：\n{1}", DiffTimeoutMs / 1000, run.StdOut));
            }

            switch (run.ExitCode)
            {
                case 0:
                    Debug.Log(string.Format(
                        "[VisualBaseline] 像素比较通过（{0} 张，容差 {1}，允许超差 {2:P1}）。" +
                        "提醒：像素通过不等于画面对，仍需人工看图（方案 11.2 / 审查 6.1）。",
                        baselineFiles.Length, DefaultTolerance, DefaultMaxDiffRatio));
                    break;
                case 2:
                    Assert.Fail("visual_diff.py 报告环境/文件问题（退出码 2）。请检查当前截图是否齐全、" +
                                "PNG 是否为 8 位非隔行 RGB/RGBA。详细输出：\n" + run.StdOut);
                    break;
                default:
                    Assert.Fail(string.Format(
                        "视觉基线超差（退出码 {0}，{1} 处超差标记）。文件名与 diff 比例见上方 Console 输出，" +
                        "JSON 明细：{2}\n{3}", run.ExitCode, CountFailures(run.StdOut), reportPath, run.StdOut));
                    break;
            }
        }

        // ==================== 外部进程 ====================

        private static DiffRun RunDiff(string pythonExe, string arguments, string workingDirectory)
        {
            var run = new DiffRun();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    if (process == null)
                    {
                        run.SkipReason = "无法启动 python 进程（Process.Start 返回 null）";
                        return run;
                    }
                    run.Started = true;
                    // 先读 stdout/stderr 再等待退出，避免管道缓冲区写满造成死锁
                    run.StdOut = process.StandardOutput.ReadToEnd();
                    run.StdErr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(DiffTimeoutMs))
                    {
                        run.TimedOut = true;
                        try { process.Kill(); } catch (Exception) { /* 已在退出，忽略 */ }
                    }
                    else
                    {
                        run.ExitCode = process.ExitCode;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                run.SkipReason = "无法启动 python 比较脚本（" + ex.Message + "）";
            }
            catch (Exception ex)
            {
                run.SkipReason = "执行 python 比较脚本时出错：" + ex.GetType().Name + " " + ex.Message;
            }
            return run;
        }

        /// <summary>按 PATH 依次探测 python / python3。</summary>
        private static string FindPython()
        {
            string[] candidates = { "python", "python3", "py" };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (CanRun(candidates[i])) return candidates[i];
            }
            return null;
        }

        private static bool CanRun(string executable)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    if (process == null) return false;
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    return process.WaitForExit(20000) && process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>按 POSIX/Windows 通用方式给参数加引号（路径可能含空格或中文）。</summary>
        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";

        private static int CountFailures(string stdout)
        {
            if (string.IsNullOrEmpty(stdout)) return 0;
            int count = 0;
            int index = 0;
            while ((index = stdout.IndexOf("[FAIL]", index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += 6;
            }
            return count;
        }
    }
}
