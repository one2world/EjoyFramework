//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Chat;
using NUnit.Framework;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Tests.Chat
{
    /// <summary>
    /// 针对 <see cref="ChatService"/> 的频道管理、发送路由、到达消息路由、屏蔽过滤与未知频道丢弃的单元测试，
    /// 使用 <see cref="FakeChatTransport"/> 手动触发服务器回送。
    /// </summary>
    [TestFixture]
    public class ChatServiceTests
    {
        private static ChatMessage Msg(string id, string channelId, string senderId, string text)
        {
            return new ChatMessage(id, channelId, senderId, "Name_" + senderId, text, 1000L);
        }

        // ---- 频道管理 ----

        [Test]
        public void CreateChannel_ThenGetChannel_ReturnsSameInstance()
        {
            ChatService service = new ChatService();
            ChatChannel created = service.CreateChannel("world", ChatChannelType.World, 50);

            Assert.IsNotNull(created);
            Assert.AreEqual("world", created.Id);
            Assert.AreEqual(50, created.Capacity);
            Assert.AreSame(created, service.GetChannel("world"));
        }

        [Test]
        public void CreateChannel_DuplicateId_Throws()
        {
            ChatService service = new ChatService();
            service.CreateChannel("world", ChatChannelType.World);

            Assert.Throws<System.InvalidOperationException>(
                () => service.CreateChannel("world", ChatChannelType.Guild));
        }

        [Test]
        public void CreateChannel_EmptyId_Throws()
        {
            ChatService service = new ChatService();
            Assert.Throws<System.ArgumentException>(
                () => service.CreateChannel("", ChatChannelType.World));
        }

        [Test]
        public void GetChannel_Missing_ReturnsNull()
        {
            ChatService service = new ChatService();
            Assert.IsNull(service.GetChannel("nope"));
            Assert.IsNull(service.GetChannel(null));
        }

        [Test]
        public void RemoveChannel_RemovesAndReportsResult()
        {
            ChatService service = new ChatService();
            service.CreateChannel("world", ChatChannelType.World);

            Assert.IsTrue(service.RemoveChannel("world"));
            Assert.IsNull(service.GetChannel("world"));
            Assert.IsFalse(service.RemoveChannel("world"));
            Assert.IsFalse(service.RemoveChannel(null));
        }

        // ---- 默认传输 ----

        [Test]
        public void NullTransport_IsUsedWhenNoneInjected()
        {
            ChatService service = new ChatService();
            Assert.IsInstanceOf<NullChatTransport>(service.Transport);
            Assert.IsFalse(service.Transport.IsConnected);

            // 不应抛异常，也不会触发任何事件。
            service.CreateChannel("world", ChatChannelType.World);
            int received = 0;
            service.OnMessageReceived += (s, m) => received++;
            service.Send("world", "hi");
            Assert.AreEqual(0, received);
        }

        [Test]
        public void InjectedTransport_IsExposedViaProperty()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            Assert.AreSame(fake, service.Transport);
        }

        // ---- 发送路由 ----

        [Test]
        public void Send_RoutesToTransport_RecordingLastSent()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);

            service.Send("guild", "hello world");

            Assert.AreEqual(1, fake.SendCount);
            Assert.AreEqual("guild", fake.LastSentChannelId);
            Assert.AreEqual("hello world", fake.LastSentText);
        }

        [Test]
        public void Send_DoesNotLocallyEcho()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);

            service.Send("world", "no echo");

            // 未回送 => 频道历史为空，无本地回显。
            Assert.AreEqual(0, channel.History.Count);
        }

        // ---- 到达路由 ----

        [Test]
        public void IncomingMessage_RoutedToChannel_AndFiresOnMessageReceived()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);

            List<ChatMessage> serviceEvents = new List<ChatMessage>();
            service.OnMessageReceived += (s, m) =>
            {
                Assert.AreSame(service, s);
                serviceEvents.Add(m);
            };

            fake.RaiseReceived(Msg("1", "world", "alice", "hi all"));

            // 入频道历史。
            IReadOnlyList<ChatMessage> history = channel.History;
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("hi all", history[0].Text);

            // 触发服务级事件。
            Assert.AreEqual(1, serviceEvents.Count);
            Assert.AreEqual("hi all", serviceEvents[0].Text);
        }

        [Test]
        public void IncomingMessage_AlsoFiresChannelOnMessage()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);

            int channelFired = 0;
            channel.OnMessage += (ch, m) => channelFired++;

            fake.RaiseReceived(Msg("1", "world", "alice", "hi"));

            Assert.AreEqual(1, channelFired);
        }

        [Test]
        public void IncomingMessage_ToUnknownChannel_DroppedGracefully()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            // 故意不创建 "ghost" 频道。

            int received = 0;
            service.OnMessageReceived += (s, m) => received++;

            Assert.DoesNotThrow(() => fake.RaiseReceived(Msg("1", "ghost", "alice", "lost")));
            Assert.AreEqual(0, received);
        }

        // ---- 屏蔽过滤 ----

        [Test]
        public void MutedSender_IncomingMessage_Dropped_NoAppend_NoEvent()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);

            int received = 0;
            int channelFired = 0;
            service.OnMessageReceived += (s, m) => received++;
            channel.OnMessage += (ch, m) => channelFired++;

            service.Mute("troll");
            Assert.IsTrue(service.IsMuted("troll"));

            fake.RaiseReceived(Msg("1", "world", "troll", "spam"));

            Assert.AreEqual(0, channel.History.Count);
            Assert.AreEqual(0, received);
            Assert.AreEqual(0, channelFired);
        }

        [Test]
        public void Unmute_RestoresDelivery()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);

            int received = 0;
            service.OnMessageReceived += (s, m) => received++;

            service.Mute("bob");
            fake.RaiseReceived(Msg("1", "world", "bob", "blocked"));
            Assert.AreEqual(0, channel.History.Count);
            Assert.AreEqual(0, received);

            service.Unmute("bob");
            Assert.IsFalse(service.IsMuted("bob"));
            fake.RaiseReceived(Msg("2", "world", "bob", "now visible"));

            Assert.AreEqual(1, channel.History.Count);
            Assert.AreEqual("now visible", channel.History[0].Text);
            Assert.AreEqual(1, received);
        }

        [Test]
        public void Mute_OnlyAffectsMatchingSender()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);

            service.Mute("bob");
            fake.RaiseReceived(Msg("1", "world", "alice", "kept"));
            fake.RaiseReceived(Msg("2", "world", "bob", "dropped"));

            Assert.AreEqual(1, channel.History.Count);
            Assert.AreEqual("kept", channel.History[0].Text);
        }

        [Test]
        public void Mute_IsIdempotent_AndIgnoresEmptyIds()
        {
            ChatService service = new ChatService();
            service.Mute("x");
            service.Mute("x");
            Assert.IsTrue(service.IsMuted("x"));

            service.Mute(null);
            service.Mute("");
            Assert.IsFalse(service.IsMuted(null));
            Assert.IsFalse(service.IsMuted(""));
        }

        // ---- Setter 注入（FrameworkModule 装配路径） ----

        [Test]
        public void ParameterlessCtor_DefaultsToNullTransport_ThenSetTransport_RoutesIncoming()
        {
            // 模拟 Framework.GetModule<IChatService>() 的开箱状态：无参构造 + 默认空传输。
            ChatService service = new ChatService();
            Assert.IsInstanceOf<NullChatTransport>(service.Transport);

            FakeChatTransport fake = new FakeChatTransport();
            service.SetTransport(fake);
            Assert.AreSame(fake, service.Transport);

            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);
            int received = 0;
            service.OnMessageReceived += (s, m) =>
            {
                Assert.AreSame(service, s);
                received++;
            };

            fake.RaiseReceived(Msg("1", "world", "alice", "hi"));

            Assert.AreEqual(1, channel.History.Count);
            Assert.AreEqual(1, received);
        }

        [Test]
        public void SetTransport_Replacement_UnsubscribesOldTransport()
        {
            ChatService service = new ChatService();
            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);

            FakeChatTransport oldTransport = new FakeChatTransport();
            FakeChatTransport newTransport = new FakeChatTransport();
            service.SetTransport(oldTransport);
            service.SetTransport(newTransport);

            int received = 0;
            service.OnMessageReceived += (s, m) => received++;

            // 旧传输层的回送不再路由进服务。
            oldTransport.RaiseReceived(Msg("1", "world", "alice", "stale"));
            Assert.AreEqual(0, channel.History.Count);
            Assert.AreEqual(0, received);

            // 新传输层正常工作。
            newTransport.RaiseReceived(Msg("2", "world", "alice", "fresh"));
            Assert.AreEqual(1, channel.History.Count);
            Assert.AreEqual("fresh", channel.History[0].Text);
            Assert.AreEqual(1, received);
        }

        [Test]
        public void SetTransport_Null_FallsBackToNullTransport()
        {
            ChatService service = new ChatService(new FakeChatTransport());
            service.SetTransport(null);
            Assert.IsInstanceOf<NullChatTransport>(service.Transport);
        }

        // ---- FrameworkModule 配置契约 ----

        [Test]
        public void ModuleConfiguration_RequiresRealTransport()
        {
            ChatService service = new ChatService();
            Assert.IsTrue(service.RequiresConfiguration);
            // 默认空传输 => 未配置。
            Assert.IsFalse(service.IsModuleConfigured);
            Assert.IsNotEmpty(service.ConfigurationHint);

            service.SetTransport(new FakeChatTransport());
            // 注入真实传输 => 已配置。
            Assert.IsTrue(service.IsModuleConfigured);
        }

        [Test]
        public void Shutdown_ClearsRuntimeState()
        {
            FakeChatTransport fake = new FakeChatTransport();
            ChatService service = new ChatService(fake);
            ChatChannel channel = service.CreateChannel("world", ChatChannelType.World);
            service.Mute("troll");

            service.Shutdown();

            // 频道与屏蔽名单被清空，传输层复位为空传输。
            Assert.IsNull(service.GetChannel("world"));
            Assert.IsFalse(service.IsMuted("troll"));
            Assert.IsInstanceOf<NullChatTransport>(service.Transport);

            // 关闭后旧传输层回送不再路由。
            int received = 0;
            service.OnMessageReceived += (s, m) => received++;
            fake.RaiseReceived(Msg("1", "world", "alice", "after-shutdown"));
            Assert.AreEqual(0, received);
        }
    }
}
