//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 一次 StringHash 碰撞：两个不同字符串得到同一个哈希值（或某个字符串哈希到保留值 0）。
    /// </summary>
    public readonly struct StringHashCollision
    {
        /// <summary>碰撞的哈希值。</summary>
        public readonly StringHash Hash;

        /// <summary>先登记的名字；与保留值 0 碰撞时为 <see cref="StringHashRegistry.NoneName"/>。</summary>
        public readonly string First;

        /// <summary>后登记、与 <see cref="First"/> 碰撞的名字。</summary>
        public readonly string Second;

        public StringHashCollision(StringHash hash, string first, string second)
        {
            Hash = hash;
            First = first;
            Second = second;
        }

        /// <summary>可直接放进日志的说明（含修复办法）。</summary>
        /// <returns>说明文本。</returns>
        public override string ToString()
        {
            return "StringHash collision 0x" + Hash.Value.ToString("X8") + ": '" + First + "' vs '" + Second
                   + "'. Rename one of them — hash-keyed lookups cannot tell them apart.";
        }
    }

    /// <summary>
    /// StringHash 的"哈希 → 原始名字"登记表与碰撞检测。
    ///
    /// 两种用法：
    ///   - 运行期（默认只在编辑器 / Development Build 开启）：<see cref="StringHash.Of(string)"/> 自动登记，
    ///     同哈希不同名立即报错一次（<see cref="FrameworkLog.Error(object)"/>，<see cref="ThrowOnCollision"/> 时抛出），
    ///     碰撞记录可经 <see cref="GetCollisions"/> 取回；调试面板 / 日志经 <see cref="TryGetName"/> 把哈希还原成名字。
    ///   - 编辑期批量体检：<see cref="FindCollisions"/> 对任意键集合做纯检查，不触碰全局表
    ///     （配置导出、编辑期脚本用它）。
    ///
    /// 同一个名字重复登记不分配也不报错；同一对碰撞只报一次。登记数达到 <see cref="MaxEntries"/> 后停止登记并告警一次
    /// （防止有人把动态数据也走 StringHash.Of 导致开发版内存无限增长——动态数据请用 <see cref="StringHash.Compute(string)"/>）。
    ///
    /// 线程契约：全部方法加锁，任意线程安全；碰撞日志与异常在锁外发出。
    /// </summary>
    public static class StringHashRegistry
    {
        /// <summary>默认登记上限。</summary>
        public const int DefaultMaxEntries = 1 << 16;

        /// <summary>保留值 0 在碰撞记录里的显示名。</summary>
        public const string NoneName = "<StringHash.None>";

        private const int MaxCollisionsKept = 256;

        private static readonly object s_Lock = new object();
        private static readonly Dictionary<uint, string> s_Names = new Dictionary<uint, string>();
        private static readonly List<StringHashCollision> s_Collisions = new List<StringHashCollision>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static volatile bool s_Enabled = true;
#else
        private static volatile bool s_Enabled = false;
#endif
        private static volatile bool s_ThrowOnCollision;
        private static int s_MaxEntries = DefaultMaxEntries;
        private static bool s_CapacityWarned;

        /// <summary>
        /// 是否登记。编辑器与 Development Build 默认开启，Release 默认关闭（可在 QA 包里手动打开）。
        /// </summary>
        public static bool Enabled
        {
            get { return s_Enabled; }
            set { s_Enabled = value; }
        }

        /// <summary>
        /// 检测到碰撞时是否抛出 <see cref="FrameworkException"/>（默认 false：只报错不中断）。
        /// 测试与编辑期校验可打开，让碰撞立刻失败。
        /// </summary>
        public static bool ThrowOnCollision
        {
            get { return s_ThrowOnCollision; }
            set { s_ThrowOnCollision = value; }
        }

        /// <summary>登记上限（至少 1）。</summary>
        public static int MaxEntries
        {
            get { lock (s_Lock) { return s_MaxEntries; } }
            set
            {
                if (value < 1)
                {
                    throw new FrameworkException("StringHashRegistry.MaxEntries must be at least 1.");
                }

                lock (s_Lock)
                {
                    s_MaxEntries = value;
                    s_CapacityWarned = false;
                }
            }
        }

        /// <summary>已登记的名字数。</summary>
        public static int Count
        {
            get { lock (s_Lock) { return s_Names.Count; } }
        }

        /// <summary>已记录的碰撞数（同一对只计一次）。</summary>
        public static int CollisionCount
        {
            get { lock (s_Lock) { return s_Collisions.Count; } }
        }

        /// <summary>
        /// 查询哈希对应的原始名字。
        /// </summary>
        /// <param name="hash">哈希值。</param>
        /// <param name="name">原始名字。</param>
        /// <returns>登记过返回 true。</returns>
        public static bool TryGetName(uint hash, out string name)
        {
            lock (s_Lock)
            {
                return s_Names.TryGetValue(hash, out name);
            }
        }

        /// <summary>
        /// 把已记录的碰撞追加到 results（不清空 results）。
        /// </summary>
        /// <param name="results">接收结果的列表。</param>
        public static void GetCollisions(List<StringHashCollision> results)
        {
            if (results == null)
            {
                throw new FrameworkException("StringHashRegistry.GetCollisions: results is null.");
            }

            lock (s_Lock)
            {
                results.AddRange(s_Collisions);
            }
        }

        /// <summary>
        /// 清空登记与碰撞记录（测试、编辑器域重载后重新体检用）。
        /// </summary>
        public static void Clear()
        {
            lock (s_Lock)
            {
                s_Names.Clear();
                s_Collisions.Clear();
                s_CapacityWarned = false;
            }
        }

        /// <summary>
        /// 编辑期批量体检：对 names 做纯碰撞检查，不读写全局登记表。null 与完全相同的重复名被忽略；
        /// 哈希到保留值 0 的名字也作为碰撞报告。
        /// </summary>
        /// <param name="names">待检查的名字集合。</param>
        /// <param name="results">接收碰撞的列表（追加，不清空）。</param>
        /// <returns>本次发现的碰撞数。</returns>
        public static int FindCollisions(IEnumerable<string> names, List<StringHashCollision> results)
        {
            if (names == null)
            {
                throw new FrameworkException("StringHashRegistry.FindCollisions: names is null.");
            }

            if (results == null)
            {
                throw new FrameworkException("StringHashRegistry.FindCollisions: results is null.");
            }

            int found = 0;
            Dictionary<uint, string> seen = new Dictionary<uint, string>();
            HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (name == null)
                {
                    continue;
                }

                uint hash = StringHash.Compute(name);
                if (hash == 0u)
                {
                    if (reported.Add(name))
                    {
                        results.Add(new StringHashCollision(new StringHash(0u), NoneName, name));
                        found++;
                    }

                    continue;
                }

                string existing;
                if (!seen.TryGetValue(hash, out existing))
                {
                    seen.Add(hash, name);
                    continue;
                }

                if (string.Equals(existing, name, StringComparison.Ordinal) || !reported.Add(name))
                {
                    continue;
                }

                results.Add(new StringHashCollision(new StringHash(hash), existing, name));
                found++;
            }

            return found;
        }

        internal static void Record(uint hash, string text)
        {
            StringHashCollision collision;
            bool capacityReached = false;
            lock (s_Lock)
            {
                string existing;
                if (hash != 0u && s_Names.TryGetValue(hash, out existing))
                {
                    if (string.Equals(existing, text, StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (IsReported(hash, text))
                    {
                        return;
                    }

                    collision = new StringHashCollision(new StringHash(hash), existing, text);
                }
                else if (hash == 0u)
                {
                    if (IsReported(0u, text))
                    {
                        return;
                    }

                    collision = new StringHashCollision(new StringHash(0u), NoneName, text);
                }
                else
                {
                    if (s_Names.Count >= s_MaxEntries)
                    {
                        if (s_CapacityWarned)
                        {
                            return;
                        }

                        s_CapacityWarned = true;
                        capacityReached = true;
                        collision = default(StringHashCollision);
                    }
                    else
                    {
                        s_Names.Add(hash, text);
                        return;
                    }
                }

                if (!capacityReached && s_Collisions.Count < MaxCollisionsKept)
                {
                    s_Collisions.Add(collision);
                }
            }

            if (capacityReached)
            {
                FrameworkLog.Warning("StringHashRegistry reached MaxEntries ({0}); further names are not recorded. " +
                                     "Hash dynamic data with StringHash.Compute instead of StringHash.Of, or raise StringHashRegistry.MaxEntries.",
                    s_MaxEntries);
                return;
            }

            string message = collision.ToString();
            FrameworkLog.Error(message);
            if (s_ThrowOnCollision)
            {
                throw new FrameworkException(message);
            }
        }

        private static bool IsReported(uint hash, string text)
        {
            for (int i = 0; i < s_Collisions.Count; i++)
            {
                StringHashCollision c = s_Collisions[i];
                if (c.Hash.Value == hash && string.Equals(c.Second, text, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
