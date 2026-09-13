//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace EjoyFramework.Core.Jobs.Unity
{
    /// <summary>
    /// 现成的 Burst 并行 Job 与便捷调度：简单的逐元素操作（填充/拷贝）无需手写 Job 结构体即可享受多线程 + Burst。
    /// 调度返回 <see cref="JobHandle"/>，可链式 <c>dependsOn</c>，或交给 <see cref="IJobSchedulerManager.CompleteInLateUpdate"/> 延迟完成。
    /// </summary>
    public static class JobUtility
    {
        /// <summary>并行填充：<c>array[i] = value</c>。</summary>
        public static JobHandle Fill<T>(NativeArray<T> array, T value, int innerBatch = 64, JobHandle dependsOn = default)
            where T : unmanaged
        {
            return new FillJob<T> { Array = array, Value = value }.Schedule(array.Length, innerBatch, dependsOn);
        }

        /// <summary>并行拷贝：<c>dest[i] = source[i]</c>（长度需一致）。</summary>
        public static JobHandle Copy<T>(NativeArray<T> source, NativeArray<T> dest, int innerBatch = 64, JobHandle dependsOn = default)
            where T : unmanaged
        {
            if (source.Length != dest.Length)
            {
                throw new Core.FrameworkException("JobUtility.Copy: source/dest length mismatch.");
            }
            return new CopyJob<T> { Source = source, Dest = dest }.Schedule(source.Length, innerBatch, dependsOn);
        }
    }

    /// <summary>逐元素填充（Burst 并行 <see cref="IJobParallelFor"/>）。</summary>
    [BurstCompile]
    public struct FillJob<T> : IJobParallelFor where T : unmanaged
    {
        /// <summary>目标数组（仅写）。</summary>
        [WriteOnly] public NativeArray<T> Array;

        /// <summary>填充值。</summary>
        public T Value;

        /// <inheritdoc />
        public void Execute(int index)
        {
            Array[index] = Value;
        }
    }

    /// <summary>逐元素拷贝（Burst 并行 <see cref="IJobParallelFor"/>）。</summary>
    [BurstCompile]
    public struct CopyJob<T> : IJobParallelFor where T : unmanaged
    {
        /// <summary>源数组（仅读）。</summary>
        [ReadOnly] public NativeArray<T> Source;

        /// <summary>目标数组（仅写）。</summary>
        [WriteOnly] public NativeArray<T> Dest;

        /// <inheritdoc />
        public void Execute(int index)
        {
            Dest[index] = Source[index];
        }
    }
}
