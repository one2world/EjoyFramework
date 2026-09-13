//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.UI
{
    internal sealed class ViewportManager : FrameworkModule, IViewportManager
    {
        private IViewportHelper m_Helper;
        private ViewportMetrics m_Current;
        private long m_Revision;
        private bool m_HasValue;

        public override int Priority { get { return 65; } }
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Helper != null && m_HasValue; } }
        public override string ConfigurationHint { get { return "UIResolutionAdapter must configure ViewportManager during framework composition."; } }
        public bool HasValue => m_HasValue;
        public ViewportMetrics Current
        {
            get
            {
                if (!m_HasValue) throw new FrameworkException("ViewportManager is not initialized.");
                return m_Current;
            }
        }
        public event ViewportChangedHandler Changed;

        public void SetHelper(IViewportHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetHelper));
            if (helper == null) throw new FrameworkException("Viewport helper is invalid.");
            if (m_Helper != null) throw new FrameworkException("Viewport helper is already configured.");
            m_Helper = helper;
            Refresh();
        }

        public bool Refresh()
        {
            Framework.EnsureMainThread(nameof(Refresh));
            if (m_Helper == null) throw new FrameworkException("Viewport helper is not configured.");
            if (!m_Helper.TryGetMetrics(out ViewportMetrics metrics))
            {
                if (!m_HasValue) throw new FrameworkException("Viewport helper did not provide initial metrics.");
                return false;
            }
            if (m_HasValue && m_Current.IsSameLayout(in metrics)) return false;

            ViewportMetrics previous = m_Current;
            m_Revision++;
            m_Current = metrics.WithRevision(m_Revision);
            m_HasValue = true;
            Changed?.Invoke(in m_Current, in previous);
            return true;
        }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            Refresh();
        }

        public override void Shutdown()
        {
            m_Helper = null;
            m_Current = default;
            m_Revision = 0;
            m_HasValue = false;
            Changed = null;
        }
    }
}
