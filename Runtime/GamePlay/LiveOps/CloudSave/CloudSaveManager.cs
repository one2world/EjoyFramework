//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.CloudSave
{
    /// <summary>
    /// 云存档管理器。封装一个 <see cref="ICloudSaveBackend"/>，提供同步（拉取 + 冲突解决 + 回写）流程，
    /// 并暴露纯静态、确定性的冲突解决算法 <see cref="ResolveConflict"/> 作为可独立测试的核心逻辑。
    /// 作为 <see cref="FrameworkModule"/> 通过 <see cref="Framework.GetModule{T}"/> 获取；使用前须先
    /// 调用 <see cref="SetBackend"/> 注入真实后端。与引擎无关，仅依赖 System.*。
    /// </summary>
    public sealed class CloudSaveManager : FrameworkModule, ICloudSaveManager
    {
        // 默认使用空后端，保证未注入真实后端时 GetModule 取到的实例仍可安全调用（下载恒 null、上传恒失败）。
        private ICloudSaveBackend m_Backend = new NullCloudSaveBackend();

        /// <summary>
        /// 构造云存档管理器。默认策略为 <see cref="ConflictResolution.PreferNewerTimestamp"/>，
        /// 后端默认为 <see cref="NullCloudSaveBackend"/>；请通过 <see cref="SetBackend"/> 注入真实后端。
        /// </summary>
        public CloudSaveManager()
        {
            DefaultPolicy = ConflictResolution.PreferNewerTimestamp;
        }

        /// <summary>
        /// 当前使用的后端；未注入时为默认的 <see cref="NullCloudSaveBackend"/>。
        /// </summary>
        public ICloudSaveBackend Backend
        {
            get { return m_Backend; }
        }

        /// <summary>
        /// 注入云存档后端实现；传入 null 将回退为默认的 <see cref="NullCloudSaveBackend"/>，
        /// 保证调用方始终可安全使用，对应核心模块 SetLoader/SetHelper 的注入惯例。
        /// </summary>
        /// <param name="backend">后端实现；为 null 时回退为 <see cref="NullCloudSaveBackend"/>。</param>
        public void SetBackend(ICloudSaveBackend backend)
        {
            m_Backend = backend ?? new NullCloudSaveBackend();
        }

        /// <summary>
        /// 该模块要求外部注入后端才能正常工作。
        /// </summary>
        public override bool RequiresConfiguration
        {
            get { return true; }
        }

        /// <summary>
        /// 是否已注入可用后端（默认 NullCloudSaveBackend 视为未配置真实后端）。
        /// </summary>
        public override bool IsModuleConfigured
        {
            get { return m_Backend != null && !(m_Backend is NullCloudSaveBackend); }
        }

        /// <summary>
        /// 未配置时的修复提示。
        /// </summary>
        public override string ConfigurationHint
        {
            get { return "Call SetBackend(ICloudSaveBackend) before use."; }
        }

        /// <summary>
        /// 模块优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 云存档同步为回调驱动，无每帧工作，留空。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理运行时状态：清空回调与解析器，并恢复为默认空后端与默认策略。
        /// </summary>
        public override void Shutdown()
        {
            OnConflict = null;
            ManualResolver = null;
            m_Backend = new NullCloudSaveBackend();
            DefaultPolicy = ConflictResolution.PreferNewerTimestamp;
        }

        /// <summary>
        /// 默认冲突解决策略，<see cref="Sync"/> 使用此策略。
        /// </summary>
        public ConflictResolution DefaultPolicy { get; set; }

        /// <summary>
        /// <see cref="ConflictResolution.Manual"/> 策略下使用的自定义解析器：(local, remote) =&gt; winner。
        /// 若为 Manual 策略却未设置此解析器，则回退为保留本地。
        /// </summary>
        public Func<CloudSaveData, CloudSaveData, CloudSaveData> ManualResolver { get; set; }

        /// <summary>
        /// 当本地与远端同时存在且内容相异时触发：(local, remote)。在解析出胜者之前回调。
        /// </summary>
        public event Action<CloudSaveData, CloudSaveData> OnConflict;

        /// <summary>
        /// 纯静态、确定性的冲突解决。规则：
        /// <list type="bullet">
        /// <item>任一方为 null，则另一方胜出（两者皆 null 返回 null）。</item>
        /// <item><see cref="ConflictResolution.PreferLocal"/> / <see cref="ConflictResolution.PreferRemote"/>：固定保留对应一方。</item>
        /// <item><see cref="ConflictResolution.PreferNewerTimestamp"/>：时间戳更大者胜出，相等则本地。</item>
        /// <item><see cref="ConflictResolution.PreferHigherVersion"/>：版本号更大者胜出，相等则本地。</item>
        /// <item><see cref="ConflictResolution.Manual"/>：调用 <paramref name="manualResolver"/>(local, remote)；
        /// 若解析器为 null，则回退为保留本地。</item>
        /// </list>
        /// </summary>
        /// <param name="local">本地存档，可为 null。</param>
        /// <param name="remote">远端存档，可为 null。</param>
        /// <param name="policy">冲突解决策略。</param>
        /// <param name="manualResolver">Manual 策略下的解析器；其他策略忽略此参数。</param>
        /// <returns>胜出的存档；两者皆 null 时返回 null。</returns>
        public static CloudSaveData ResolveConflict(CloudSaveData local, CloudSaveData remote,
            ConflictResolution policy,
            Func<CloudSaveData, CloudSaveData, CloudSaveData> manualResolver = null)
        {
            // 任一方缺失：另一方直接胜出（含两者皆 null）。
            if (local == null)
            {
                return remote;
            }

            if (remote == null)
            {
                return local;
            }

            switch (policy)
            {
                case ConflictResolution.PreferLocal:
                    return local;

                case ConflictResolution.PreferRemote:
                    return remote;

                case ConflictResolution.PreferNewerTimestamp:
                    // 时间戳更大者胜出；相等（含并列）保留本地。
                    return remote.TimestampMs > local.TimestampMs ? remote : local;

                case ConflictResolution.PreferHigherVersion:
                    // 版本号更大者胜出；相等保留本地。
                    return remote.Version > local.Version ? remote : local;

                case ConflictResolution.Manual:
                    // 未提供解析器时回退为保留本地，避免抛异常打断同步流程。
                    return manualResolver != null ? manualResolver(local, remote) : local;

                default:
                    // 未知策略：保守地保留本地。
                    return local;
            }
        }

        /// <summary>
        /// 判断两份存档是否相异：版本号不同，或字节内容不同。
        /// 仅在两者均非 null 时由调用方使用。
        /// </summary>
        /// <param name="a">存档 A，可为 null。</param>
        /// <param name="b">存档 B，可为 null。</param>
        /// <returns>相异返回 true。</returns>
        public static bool Differ(CloudSaveData a, CloudSaveData b)
        {
            if (ReferenceEquals(a, b))
            {
                return false;
            }

            if (a == null || b == null)
            {
                return true;
            }

            if (a.Version != b.Version)
            {
                return true;
            }

            return !BytesEqual(a.Data, b.Data);
        }

        /// <summary>
        /// 同步流程：下载本地键对应的远端存档，与本地解析冲突，必要时回写胜者，并回调胜者。
        /// 步骤：
        /// <list type="number">
        /// <item>Download(local.Key)。</item>
        /// <item>远端为 null：胜者 = 本地，并上传本地。</item>
        /// <item>远端存在且与本地相异：触发 <see cref="OnConflict"/>，按 <see cref="DefaultPolicy"/> 解析；
        /// 若胜者不同于远端（远端需更新），则上传胜者。</item>
        /// <item>最终回调 <paramref name="onResolved"/>(winner)。</item>
        /// </list>
        /// 使用后端的同步式回调，回调完成顺序由后端保证。
        /// </summary>
        /// <param name="local">本地存档，不可为空（其 Key 用于定位远端）。</param>
        /// <param name="onResolved">解析完成回调，参数为最终胜出的存档。</param>
        public void Sync(CloudSaveData local, Action<CloudSaveData> onResolved)
        {
            if (local == null)
            {
                throw new ArgumentNullException(nameof(local));
            }

            m_Backend.Download(local.Key, remote =>
            {
                if (remote == null)
                {
                    // 远端无数据：本地即胜者，上传后回调。
                    m_Backend.Upload(local, _ => { });
                    onResolved?.Invoke(local);
                    return;
                }

                if (Differ(local, remote))
                {
                    Action<CloudSaveData, CloudSaveData> conflictHandler = OnConflict;
                    if (conflictHandler != null)
                    {
                        conflictHandler(local, remote);
                    }
                }

                CloudSaveData winner = ResolveConflict(local, remote, DefaultPolicy, ManualResolver);

                // 仅当胜者不同于远端（即远端需要被更新）时才回写，避免无谓上传。
                if (Differ(winner, remote))
                {
                    m_Backend.Upload(winner, _ => { });
                }

                onResolved?.Invoke(winner);
            });
        }

        /// <summary>
        /// 逐字节比较两个字节数组是否相等（含 null 处理）。
        /// </summary>
        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
