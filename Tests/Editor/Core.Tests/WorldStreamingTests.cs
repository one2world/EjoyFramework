//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Streaming;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class WorldStreamingTests
    {
        private WorldStreamingManager m_WSM;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_WSM = new WorldStreamingManager();
            m_WSM.SetPolicy(new StreamingPolicy { LoadRadius = 50, UnloadRadius = 70, MaxChunksPerFrame = 100 });
        }

        [Test]
        public void RegisterChunk_PlayerInRadius_FiresLoadEvent()
        {
            var loaded = new List<string>();
            m_WSM.ChunkLoadRequested += (c, lod) => loaded.Add(c.ChunkId);
            m_WSM.RegisterChunk(new ChunkInfo { ChunkId = "A", Center = new Vector3Lite(0, 0, 0), Radius = 10 });
            m_WSM.SetPlayerPosition(new Vector3Lite(0, 0, 0));
            m_WSM.ForceReevaluate();
            CollectionAssert.Contains(loaded, "A");
        }

        [Test]
        public void ChunkOutOfRadius_NotLoaded()
        {
            var loaded = new List<string>();
            m_WSM.ChunkLoadRequested += (c, lod) => loaded.Add(c.ChunkId);
            m_WSM.RegisterChunk(new ChunkInfo { ChunkId = "Far", Center = new Vector3Lite(1000, 0, 0), Radius = 5 });
            m_WSM.SetPlayerPosition(new Vector3Lite(0, 0, 0));
            m_WSM.ForceReevaluate();
            Assert.AreEqual(0, loaded.Count);
        }

        [Test]
        public void Player_MovesAway_TriggersUnload()
        {
            int loadCount = 0, unloadCount = 0;
            m_WSM.ChunkLoadRequested += (c, lod) => loadCount++;
            m_WSM.ChunkUnloadRequested += c => unloadCount++;
            m_WSM.RegisterChunk(new ChunkInfo { ChunkId = "A", Center = new Vector3Lite(0, 0, 0), Radius = 5 });
            m_WSM.SetPlayerPosition(new Vector3Lite(0, 0, 0));
            m_WSM.ForceReevaluate();
            Assert.AreEqual(1, loadCount);

            m_WSM.SetPlayerPosition(new Vector3Lite(200, 0, 0));
            m_WSM.ForceReevaluate();
            Assert.AreEqual(1, unloadCount);
        }

        [Test]
        public void Hysteresis_NoUnload_Within_UnloadRadius()
        {
            int loadCount = 0, unloadCount = 0;
            m_WSM.ChunkLoadRequested += (c, lod) => loadCount++;
            m_WSM.ChunkUnloadRequested += c => unloadCount++;
            m_WSM.RegisterChunk(new ChunkInfo { ChunkId = "A", Center = new Vector3Lite(0, 0, 0), Radius = 0 });
            m_WSM.SetPlayerPosition(new Vector3Lite(0, 0, 0));
            m_WSM.ForceReevaluate();   // 加载

            m_WSM.SetPlayerPosition(new Vector3Lite(60, 0, 0));   // 在 LoadRadius(50) 外，UnloadRadius(70) 内
            m_WSM.ForceReevaluate();
            Assert.AreEqual(0, unloadCount, "滞后区不应卸载");
        }

        [Test]
        public void ActiveChunkCount_TracksLoadUnload()
        {
            m_WSM.RegisterChunk(new ChunkInfo { ChunkId = "A", Center = new Vector3Lite(0, 0, 0), Radius = 0 });
            m_WSM.RegisterChunk(new ChunkInfo { ChunkId = "B", Center = new Vector3Lite(20, 0, 0), Radius = 0 });
            m_WSM.SetPlayerPosition(new Vector3Lite(0, 0, 0));
            m_WSM.ForceReevaluate();
            Assert.AreEqual(2, m_WSM.ActiveChunkCount);
        }

        [Test]
        public void LodChange_FiresEvent_WhenCrossingDistance()
        {
            int lodChangeCount = 0;
            int lastFrom = -1, lastTo = -1;
            m_WSM.ChunkLodChanged += (c, fr, to) => { lodChangeCount++; lastFrom = fr; lastTo = to; };

            m_WSM.RegisterChunk(new ChunkInfo
            {
                ChunkId = "A",
                Center = new Vector3Lite(0, 0, 0),
                Radius = 0,
                LodDistances = new[] { 10f, 30f }   // <10 = LOD0, <30 = LOD1, else LOD2
            });
            m_WSM.SetPlayerPosition(new Vector3Lite(5, 0, 0));
            m_WSM.ForceReevaluate();   // 加载 LOD0
            Assert.AreEqual(0, lodChangeCount, "初次加载不算 lod change");

            m_WSM.SetPlayerPosition(new Vector3Lite(20, 0, 0));   // LOD1
            m_WSM.ForceReevaluate();
            Assert.AreEqual(1, lodChangeCount);
            Assert.AreEqual(0, lastFrom);
            Assert.AreEqual(1, lastTo);
        }

        [Test]
        public void Policy_InvalidUnloadRadius_Throws()
        {
            var bad = new StreamingPolicy { LoadRadius = 100, UnloadRadius = 50 };
            Assert.Throws<FrameworkException>(() => m_WSM.SetPolicy(bad));
        }

        [Test]
        public void UnregisterChunk_ActiveChunk_FiresUnloadEvent()
        {
            int unloadCount = 0;
            m_WSM.ChunkUnloadRequested += c => unloadCount++;
            m_WSM.RegisterChunk(new ChunkInfo { ChunkId = "A", Center = new Vector3Lite(0, 0, 0), Radius = 0 });
            m_WSM.SetPlayerPosition(new Vector3Lite(0, 0, 0));
            m_WSM.ForceReevaluate();   // active

            Assert.IsTrue(m_WSM.UnregisterChunk("A"));
            Assert.AreEqual(1, unloadCount);
            Assert.AreEqual(0, m_WSM.RegisteredChunkCount);
        }
    }
}
