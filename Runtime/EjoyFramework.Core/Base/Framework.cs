//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 游戏框架入口（旧称 EjoyFrameworkEntry）。
    ///
    /// 修复：
    ///   1) GetModule 加锁，避免多线程首次访问创建重复模块。
    ///   2) Update 前对模块列表做数组快照，允许 Update 内部懒加载新模块。
    ///   3) Shutdown 中每个模块用 try/catch 隔离，单点失败不阻塞清理与 ReferencePool.ClearAll。
    ///   4) 模块创建只走 RegisterFactory 登记的工厂（静态 new，零反射，IL2CPP 不裁剪类型/构造函数）；
    ///      已移除按 'I&lt;Module&gt;' 命名约定的反射兜底——未登记工厂即抛异常（Editor 与 Player 行为一致）。
    ///   5) 模块缓存通过 static class ModuleCache{T} 实现，连续调用 GetModule{T}() 是 O(1) 无锁。
    ///   6) 记录主线程 ID，提供 EnsureMainThread 给业务模块自检。
    /// </summary>
    public static class Framework
    {
        private static readonly object s_Lock = new object();
        private static readonly GameLinkedList<FrameworkModule> s_Modules = new GameLinkedList<FrameworkModule>();
        private static int s_MainThreadId = 0;

        // 模块快照：每帧 Update 时构建，用于安全遍历。
        private static FrameworkModule[] s_UpdateSnapshot = Array.Empty<FrameworkModule>();
        private static int s_UpdateSnapshotCount = 0;
        private static int s_ModulesVersion = 0;
        private static int s_SnapshotVersion = -1;

        // 每个 ModuleCache<T> 首次写入时登记一个清空委托；Shutdown / ResetForEnterPlayMode 时统一调用，
        // 避免"关闭 Domain Reload"下第二次 Play 从 GetModule<T> 拿到上一次已 Shutdown 的陈旧模块实例。
        private static readonly List<Action> s_CacheClearers = new List<Action>();

        // 接口类型 → 模块工厂委托。由生成代码 (FrameworkModuleRegistrations.RegisterAll) 在启动时填充。
        // 使 GetModule<T> 走静态 new（IL2CPP 可见、构造函数不被裁剪、零反射）。仅当无对应工厂时才回退反射查找。
        // 注册是应用生命周期级元数据，跨 Shutdown 保留（Shutdown 只清模块实例与 per-T 缓存）。
        private static readonly Dictionary<Type, Func<FrameworkModule>> s_Factories = new Dictionary<Type, Func<FrameworkModule>>();

        // GetModule 在可重入锁内执行工厂。显式跟踪创建链，避免 A 构造依赖 B、B 又依赖 A 时无限递归。
        private static readonly HashSet<Type> s_CreatingModules = new HashSet<Type>();
        private static readonly List<Type> s_ModuleCreationStack = new List<Type>();

        // 记录每个接口工厂的来源程序集，用于侦测「两个不同程序集都为同一接口注册工厂」的真冲突
        // （区别于领域重载下同一程序集的幂等重注册）。仅用于诊断告警，不影响行为。
        private static readonly Dictionary<Type, System.Reflection.Assembly> s_FactoryOrigin = new Dictionary<Type, System.Reflection.Assembly>();

        /// <summary>
        /// 标记当前线程为主线程。BaseComponent.Awake 会调用一次。
        /// </summary>
        public static void MarkMainThread()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// 获取主线程 ID（0 表示未标记）。
        /// </summary>
        public static int MainThreadId
        {
            get { return s_MainThreadId; }
        }

        /// <summary>
        /// 当前线程是否是主线程。在 Editor/DEV 构建中作业务模块自检用。
        /// </summary>
        public static bool IsMainThread()
        {
            return s_MainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == s_MainThreadId;
        }

        /// <summary>
        /// 断言当前线程是主线程，否则抛异常。仅在 Editor/DEV 构建中起作用。
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("DEBUG")]
        public static void EnsureMainThread(string apiName)
        {
            if (s_MainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != s_MainThreadId)
            {
                throw new FrameworkException(string.Format(
                    "API '{0}' must be called from main thread (id={1}, current={2}).",
                    apiName, s_MainThreadId, Thread.CurrentThread.ManagedThreadId));
            }
        }

        /// <summary>
        /// 显式注册模块实例。优先走此 API；GetModule 仅作懒加载兜底。
        /// </summary>
        internal static void RegisterModule(FrameworkModule module)
        {
            if (module == null)
            {
                throw new FrameworkException("Module is invalid.");
            }

            lock (s_Lock)
            {
                Type t = module.GetType();
                LinkedListNode<FrameworkModule> existing = s_Modules.First;
                while (existing != null)
                {
                    if (existing.Value.GetType() == t)
                    {
                        throw new FrameworkException(string.Format("Module '{0}' already registered.", t.FullName));
                    }
                    existing = existing.Next;
                }

                InsertByPriority(module);
                s_ModulesVersion++;
            }
        }

        /// <summary>
        /// 注册模块工厂：接口类型 → 创建该模块实例的委托（通常为 <c>static () =&gt; new XxxManager()</c>）。
        /// 由生成代码 <c>FrameworkModuleRegistrations.RegisterAll</c> 在启动时调用，使 <see cref="GetModule{T}"/>
        /// 以静态 new 创建模块，彻底避免反射 + IL2CPP 代码裁剪导致的 "Can not find module type" 崩溃。
        /// 幂等：重复注册同一接口将覆盖旧工厂。线程安全。
        /// </summary>
        public static void RegisterFactory(Type interfaceType, Func<FrameworkModule> factory)
        {
            if (interfaceType == null)
            {
                throw new FrameworkException("Interface type is invalid.");
            }
            if (factory == null)
            {
                throw new FrameworkException("Module factory is invalid.");
            }

            lock (s_Lock)
            {
                // 侦测真冲突：同一接口被「不同程序集」注册（重复 FQN / 跨程序集同名实现），会导致后者静默覆盖前者。
                // 领域重载下同一程序集的幂等重注册来源相同，不告警。
                System.Reflection.Assembly origin = factory.Method?.DeclaringType?.Assembly;
                if (origin != null
                    && s_FactoryOrigin.TryGetValue(interfaceType, out System.Reflection.Assembly existingOrigin)
                    && existingOrigin != null && !ReferenceEquals(existingOrigin, origin))
                {
                    FrameworkLog.Error(
                        "模块工厂冲突：接口 '{0}' 同时被程序集 '{1}' 与 '{2}' 注册，后者覆盖前者。" +
                        "请确保每个模块接口仅由一个程序集的生成注册表登记（检查重复 FQN / 跨程序集同名实现）。",
                        interfaceType.FullName, existingOrigin.GetName().Name, origin.GetName().Name);
                }

                s_Factories[interfaceType] = factory;
                if (origin != null)
                {
                    s_FactoryOrigin[interfaceType] = origin;
                }
            }
        }

        /// <summary>
        /// Validates every currently created framework module that requires external configuration.
        /// </summary>
        /// <exception cref="FrameworkException">
        /// Thrown when one or more modules have not received their required helper, loader, or backend.
        /// </exception>
        public static void ValidateModuleConfigurations()
        {
            EnsureMainThread("Framework.ValidateModuleConfigurations");
            EnsureSnapshot();

            List<string> failures = null;
            int count = s_UpdateSnapshotCount;
            for (int i = 0; i < count; i++)
            {
                FrameworkModule module = s_UpdateSnapshot[i];
                if (module == null)
                {
                    continue;
                }

                string failure = GetModuleConfigurationFailure(module);
                if (failure == null)
                {
                    continue;
                }

                failures ??= new List<string>();
                failures.Add(failure);
            }

            if (failures != null)
            {
                throw new FrameworkException(
                    "Framework module configuration validation failed:\n - "
                    + string.Join("\n - ", failures));
            }
        }

        /// <summary>
        /// 所有游戏框架模块轮询。
        /// </summary>
        public static void Update(float elapseSeconds, float realElapseSeconds)
        {
            EnsureMainThread("Framework.Update");
            EnsureSnapshot();

            int count = s_UpdateSnapshotCount;
            for (int i = 0; i < count; i++)
            {
                FrameworkModule m = s_UpdateSnapshot[i];
                if (m == null) continue;
                try
                {
                    // 配置自检放进 try 内：自检会调用模块的虚成员（RequiresConfiguration 等），
                    // 万一被业务重写抛异常，也不能逃逸而中断其余模块的轮询（保持逐模块异常隔离）。
                    ValidateModuleConfiguration(m);
                    m.Update(elapseSeconds, realElapseSeconds);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("Module '{0}' Update threw: {1}", m.GetType().FullName, ex);
                }
            }
        }

        /// <summary>
        /// 仅 Editor 下的一次性配置自检：模块若声明 RequiresConfiguration 却尚未配置（如未注入 Helper/Loader），
        /// 在其首帧轮询时以 WARNING 级别提示一次（非 Error，避免触发 EditMode 测试的 LogAssert 失败）。
        /// [Conditional("UNITY_EDITOR")] 保证该调用在 Player 构建中被编译期完全裁剪，零开销。
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void ValidateModuleConfiguration(FrameworkModule module)
        {
            if (module.m_ConfigChecked)
            {
                return;
            }
            // 无论是否告警都置位，确保至多检查/告警一次，不逐帧刷屏。
            module.m_ConfigChecked = true;
            string failure = GetModuleConfigurationFailure(module);
            if (failure != null)
            {
                FrameworkLog.Warning("{0}", failure);
            }
        }

        private static string GetModuleConfigurationFailure(FrameworkModule module)
        {
            if (!module.RequiresConfiguration || module.IsModuleConfigured)
            {
                return null;
            }

            return string.Format(
                "Module '{0}' requires configuration but is not configured. {1}",
                module.GetType().FullName,
                module.ConfigurationHint);
        }

        /// <summary>
        /// 所有游戏框架模块后期轮询（对应 Unity LateUpdate）。
        /// </summary>
        public static void LateUpdate(float elapseSeconds, float realElapseSeconds)
        {
            EnsureMainThread("Framework.LateUpdate");
            EnsureSnapshot();

            int count = s_UpdateSnapshotCount;
            for (int i = 0; i < count; i++)
            {
                FrameworkModule m = s_UpdateSnapshot[i];
                if (m == null) continue;
                try
                {
                    m.LateUpdate(elapseSeconds, realElapseSeconds);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("Module '{0}' LateUpdate threw: {1}", m.GetType().FullName, ex);
                }
            }
        }

        /// <summary>
        /// 所有游戏框架模块固定步长轮询（对应 Unity FixedUpdate，物理步长）。
        /// </summary>
        public static void FixedUpdate(float fixedElapseSeconds, float realFixedElapseSeconds)
        {
            EnsureMainThread("Framework.FixedUpdate");
            EnsureSnapshot();

            int count = s_UpdateSnapshotCount;
            for (int i = 0; i < count; i++)
            {
                FrameworkModule m = s_UpdateSnapshot[i];
                if (m == null) continue;
                try
                {
                    m.FixedUpdate(fixedElapseSeconds, realFixedElapseSeconds);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("Module '{0}' FixedUpdate threw: {1}", m.GetType().FullName, ex);
                }
            }
        }

        // 重建模块快照（仅在模块集合变更时），用于安全遍历。允许 Update 内部懒加载新模块。
        private static void EnsureSnapshot()
        {
            if (s_SnapshotVersion == s_ModulesVersion)
            {
                return;
            }

            lock (s_Lock)
            {
                if (s_UpdateSnapshot.Length < s_Modules.Count)
                {
                    s_UpdateSnapshot = new FrameworkModule[Math.Max(8, s_Modules.Count * 2)];
                }

                int idx = 0;
                foreach (FrameworkModule m in s_Modules)
                {
                    s_UpdateSnapshot[idx++] = m;
                }
                s_UpdateSnapshotCount = idx;
                s_SnapshotVersion = s_ModulesVersion;
            }
        }

        /// <summary>
        /// 关闭并清理所有游戏框架模块。每个 Shutdown 隔离，单点失败不阻塞后续。
        /// </summary>
        public static void Shutdown()
        {
            // 倒序 Shutdown（高优先级后关）
            FrameworkModule[] toShutdown;
            lock (s_Lock)
            {
                toShutdown = new FrameworkModule[s_Modules.Count];
                int idx = toShutdown.Length - 1;
                for (LinkedListNode<FrameworkModule> n = s_Modules.First; n != null; n = n.Next)
                {
                    toShutdown[idx--] = n.Value;
                }
                s_Modules.Clear();
                s_ModulesVersion++;
                s_SnapshotVersion = -1;
                s_UpdateSnapshotCount = 0;
                // 清空 per-T 静态缓存：Shutdown 后再 GetModule<T> 应惰性重建新实例，而非返回已关闭的旧实例。
                ClearModuleCachesLocked();
            }

            for (int i = 0; i < toShutdown.Length; i++)
            {
                try
                {
                    toShutdown[i].Shutdown();
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("Module '{0}' Shutdown threw: {1}", toShutdown[i].GetType().FullName, ex);
                }
            }

            try { ReferencePool.ClearAll(); } catch (Exception ex) { FrameworkLog.Error("ReferencePool.ClearAll: {0}", ex); }
            FrameworkLog.SetLogHelper(null);
        }

        /// <summary>
        /// 获取游戏框架模块（懒加载创建）。
        /// 使用 ModuleCache{T} 静态缓存，热路径 O(1) 无锁。
        /// </summary>
        public static T GetModule<T>() where T : class
        {
            T cached = ModuleCache<T>.Instance;
            if (cached != null) return cached;

            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
            {
                throw new FrameworkException(string.Format("You must get module by interface, but '{0}' is not.", interfaceType.FullName));
            }

            FrameworkModule moduleInstance;
            lock (s_Lock)
            {
                // 双重检查
                cached = ModuleCache<T>.Instance;
                if (cached != null) return cached;

                if (!s_CreatingModules.Add(interfaceType))
                {
                    throw CreateCircularModuleDependencyException(interfaceType);
                }
                s_ModuleCreationStack.Add(interfaceType);

                try
                {
                    // 首选：生成代码登记的工厂（静态 new，零反射，IL2CPP 不会裁剪类型/构造函数）。
                    if (s_Factories.TryGetValue(interfaceType, out Func<FrameworkModule> factory))
                    {
                        FrameworkModule created = factory();
                        if (created == null)
                        {
                            throw new FrameworkException(string.Format("Module factory for '{0}' returned null.", interfaceType.FullName));
                        }
                        if (!(created is T))
                        {
                            throw new FrameworkException(string.Format(
                                "Module factory for '{0}' returned '{1}', which does not implement it.",
                                interfaceType.FullName, created.GetType().FullName));
                        }
                        moduleInstance = InsertOrGetExistingLocked(created);
                    }
                    else
                    {
                        // 不再提供反射兜底：所有模块必须由生成的 FrameworkModuleRegistrations 工厂登记（静态 new，
                        // 零反射、IL2CPP 不裁剪类型/构造函数）。未登记即硬失败——把「忘了重跑 codegen / 忘了接 bootstrap」
                        // 从设备端崩溃前移为此处的明确异常（Editor 与 Player 行为一致）。
                        throw new FrameworkException(string.Format(
                            "No generated factory registered for module '{0}'. The reflection fallback has been removed. " +
                            "Run 'EjoyFramework/Core/CodeGen/Generate All' and ensure the generated FrameworkModuleRegistrations " +
                            "is compiled and its RegisterAll() runs at startup (see *ModuleBootstrap). " +
                            "In EditMode tests, call the relevant FrameworkModuleRegistrations.RegisterAll() in setup.",
                            interfaceType.FullName));
                    }

                    ModuleCache<T>.Instance = (T)(object)moduleInstance;
                    if (!ModuleCache<T>.ClearerRegistered)
                    {
                        ModuleCache<T>.ClearerRegistered = true;
                        s_CacheClearers.Add(static () => ModuleCache<T>.Instance = null);
                    }
                }
                finally
                {
                    s_ModuleCreationStack.RemoveAt(s_ModuleCreationStack.Count - 1);
                    s_CreatingModules.Remove(interfaceType);
                }
            }

            return (T)(object)moduleInstance;
        }

        private static FrameworkException CreateCircularModuleDependencyException(Type repeatedType)
        {
            int start = s_ModuleCreationStack.IndexOf(repeatedType);
            if (start < 0) start = 0;
            string chain = string.Empty;
            for (int i = start; i < s_ModuleCreationStack.Count; i++)
            {
                if (chain.Length > 0) chain += " -> ";
                chain += s_ModuleCreationStack[i].FullName;
            }
            if (chain.Length > 0) chain += " -> ";
            chain += repeatedType.FullName;
            return new FrameworkException("Circular module dependency detected: " + chain);
        }

        /// <summary>
        /// 检查模块是否已存在（不触发懒加载）。
        /// </summary>
        public static bool HasModule<T>() where T : class
        {
            return ModuleCache<T>.Instance != null;
        }

        /// <summary>
        /// 显式以接口 <typeparamref name="TInterface"/> 注册一个模块实例并绑定其缓存。
        /// 解耦"实现类与接口同命名空间去 I 前缀"的约定，便于<b>测试注入 mock</b> 或第三方扩展替换实现。
        /// 若该实例尚未在更新列表中，则按 Priority 插入。
        /// </summary>
        public static void RegisterModule<TInterface>(TInterface module) where TInterface : class
        {
            if (module == null) throw new FrameworkException("Module is invalid.");
            if (!(module is FrameworkModule fm))
                throw new FrameworkException(string.Format("Module '{0}' must derive from FrameworkModule.", typeof(TInterface).FullName));

            lock (s_Lock)
            {
                bool present = false;
                for (LinkedListNode<FrameworkModule> n = s_Modules.First; n != null; n = n.Next)
                {
                    if (ReferenceEquals(n.Value, fm)) { present = true; break; }
                }
                if (!present)
                {
                    InsertByPriority(fm);
                    s_ModulesVersion++;
                }

                ModuleCache<TInterface>.Instance = module;
                if (!ModuleCache<TInterface>.ClearerRegistered)
                {
                    ModuleCache<TInterface>.ClearerRegistered = true;
                    s_CacheClearers.Add(static () => ModuleCache<TInterface>.Instance = null);
                }
            }
        }

        // 把已创建的模块实例去重插入：若同类型模块已存在（如先前 RegisterModule 过）则返回旧实例、丢弃 created；
        // 否则按优先级插入并返回。必须在 lock(s_Lock) 内调用。
        private static FrameworkModule InsertOrGetExistingLocked(FrameworkModule created)
        {
            Type moduleType = created.GetType();
            for (LinkedListNode<FrameworkModule> n = s_Modules.First; n != null; n = n.Next)
            {
                if (n.Value.GetType() == moduleType) return n.Value;
            }

            InsertByPriority(created);
            s_ModulesVersion++;
            return created;
        }

        // 必须在 lock(s_Lock) 内调用
        private static void InsertByPriority(FrameworkModule module)
        {
            LinkedListNode<FrameworkModule> current = s_Modules.First;
            while (current != null)
            {
                if (module.Priority > current.Value.Priority) break;
                current = current.Next;
            }

            if (current != null)
            {
                s_Modules.AddBefore(current, module);
            }
            else
            {
                s_Modules.AddLast(module);
            }
        }

        /// <summary>
        /// 模块缓存。每个接口 T 拥有独立静态字段，连续 GetModule{T}() 调用直接走静态读，零锁零反射。
        /// </summary>
        private static class ModuleCache<T> where T : class
        {
            internal static T Instance;
            // 该 T 的清空委托是否已登记到 s_CacheClearers（每类型仅登记一次）。
            internal static bool ClearerRegistered;
        }

        // 调用所有已登记的缓存清空委托。必须在 s_Lock 内调用。
        private static void ClearModuleCachesLocked()
        {
            for (int i = 0; i < s_CacheClearers.Count; i++)
            {
                try { s_CacheClearers[i](); }
                catch (Exception ex) { FrameworkLog.Error("ModuleCache clear threw: {0}", ex); }
            }
        }

        /// <summary>
        /// Editor "Domain Reload Disabled" 模式下，由 Unity/Editor 层调用以重置静态状态。
        /// 不直接走 [UnityEditor.InitializeOnEnterPlayMode] 是因为 EjoyFramework.Core 设了 noEngineReferences:true，
        /// 无法引用 UnityEditor。Unity 层 EditorScript 在合适时机调用本方法即可。
        /// </summary>
        public static void ResetForEnterPlayMode()
        {
            lock (s_Lock)
            {
                s_Modules.Clear();
                s_ModulesVersion++;
                s_SnapshotVersion = -1;
                s_UpdateSnapshotCount = 0;
                s_MainThreadId = 0;
                s_CreatingModules.Clear();
                s_ModuleCreationStack.Clear();
                // 关键：清空 per-T 静态缓存，否则"关闭 Domain Reload"下第二次 Play 会返回上一次的陈旧模块。
                ClearModuleCachesLocked();
            }
        }
    }
}
