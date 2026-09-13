//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Sequencing
{
    /// <summary>
    /// 时间线上的一个关键标记（Marker）。
    ///
    /// 表示在某条轨道（Track）上、相对序列起点的某个时间点要触发的一次事件。
    /// 游戏层通过 <see cref="Key"/> 进行分发（例如 "camera.shake"、"dialogue.line1"），
    /// 并可携带任意 <see cref="Payload"/>（如抖动强度、台词文本等）。
    ///
    /// 不可变值类型（readonly struct）：构造后字段不可更改，便于在播放过程中安全复制与回放。
    /// </summary>
    public readonly struct SequenceMarker
    {
        /// <summary>
        /// 相对序列起点的触发时间（秒）。
        /// </summary>
        public double Time { get; }

        /// <summary>
        /// 事件键，游戏层据此 switch 分发（例如 "camera.shake"、"dialogue.line1"）。
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 随事件携带的任意负载，可为 null。
        /// </summary>
        public object Payload { get; }

        /// <summary>
        /// 该标记所属轨道的 Id。
        /// </summary>
        public string TrackId { get; }

        /// <summary>
        /// 构造一个标记。
        /// </summary>
        /// <param name="time">相对序列起点的触发时间（秒）。</param>
        /// <param name="key">事件键。</param>
        /// <param name="payload">任意负载，可为 null。</param>
        /// <param name="trackId">所属轨道 Id。</param>
        public SequenceMarker(double time, string key, object payload, string trackId)
        {
            Time = time;
            Key = key;
            Payload = payload;
            TrackId = trackId;
        }
    }
}
