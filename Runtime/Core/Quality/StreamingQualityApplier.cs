//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Streaming;

namespace EjoyFramework.Core.Quality
{
    /// <summary>把 <see cref="QualityKnobs.StreamingRadiusScale"/> 写入 <see cref="IWorldStreamingManager.RadiusScale"/>（档位变化时）。</summary>
    public sealed class StreamingQualityApplier : IQualityApplier
    {
        private readonly IWorldStreamingManager m_Streaming;

        public StreamingQualityApplier(IWorldStreamingManager streaming)
        {
            if (streaming == null) throw new FrameworkException("StreamingQualityApplier：streaming 为空。");
            m_Streaming = streaming;
        }

        public void OnQualityChanged(IQualityManager quality, QualityChange change)
        {
            if ((change & QualityChange.Level) == 0 || !quality.HasKnob(QualityKnobs.StreamingRadiusScale)) return;
            float scale = quality.GetKnob(QualityKnobs.StreamingRadiusScale);
            if (scale > 0f) m_Streaming.RadiusScale = scale;
        }
    }
}
