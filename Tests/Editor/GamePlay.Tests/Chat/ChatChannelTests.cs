//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Chat;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Chat
{
    /// <summary>
    /// 针对 <see cref="ChatChannel"/> 的环形缓冲容量限制、由旧到新顺序、最旧淘汰与 OnMessage 触发的单元测试。
    /// </summary>
    [TestFixture]
    public class ChatChannelTests
    {
        private static ChatMessage Msg(string id, string text)
        {
            return new ChatMessage(id, "world", "sender_" + id, "Name", text, 1000L);
        }

        [Test]
        public void Constructor_RejectsEmptyId()
        {
            Assert.Throws<System.ArgumentException>(() => new ChatChannel("", ChatChannelType.World));
        }

        [Test]
        public void Constructor_RejectsNonPositiveCapacity()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ChatChannel("c", ChatChannelType.World, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ChatChannel("c", ChatChannelType.World, -5));
        }

        [Test]
        public void NewChannel_HasEmptyHistory()
        {
            ChatChannel channel = new ChatChannel("world", ChatChannelType.World, 10);
            Assert.AreEqual("world", channel.Id);
            Assert.AreEqual(ChatChannelType.World, channel.Type);
            Assert.AreEqual(10, channel.Capacity);
            Assert.AreEqual(0, channel.History.Count);
        }

        [Test]
        public void Append_BelowCapacity_PreservesOldestToNewestOrder()
        {
            ChatChannel channel = new ChatChannel("world", ChatChannelType.World, 5);
            channel.Append(Msg("1", "a"));
            channel.Append(Msg("2", "b"));
            channel.Append(Msg("3", "c"));

            IReadOnlyList<ChatMessage> history = channel.History;
            Assert.AreEqual(3, history.Count);
            Assert.AreEqual("a", history[0].Text);
            Assert.AreEqual("b", history[1].Text);
            Assert.AreEqual("c", history[2].Text);
        }

        [Test]
        public void Append_BeyondCapacity_EvictsOldestAndKeepsOrder()
        {
            ChatChannel channel = new ChatChannel("world", ChatChannelType.World, 3);
            channel.Append(Msg("1", "a"));
            channel.Append(Msg("2", "b"));
            channel.Append(Msg("3", "c"));
            channel.Append(Msg("4", "d")); // 淘汰 "a"
            channel.Append(Msg("5", "e")); // 淘汰 "b"

            IReadOnlyList<ChatMessage> history = channel.History;
            Assert.AreEqual(3, history.Count);
            // 最旧 -> 最新：c, d, e
            Assert.AreEqual("c", history[0].Text);
            Assert.AreEqual("d", history[1].Text);
            Assert.AreEqual("e", history[2].Text);
        }

        [Test]
        public void Append_ExactlyAtCapacity_KeepsAll()
        {
            ChatChannel channel = new ChatChannel("world", ChatChannelType.World, 2);
            channel.Append(Msg("1", "a"));
            channel.Append(Msg("2", "b"));

            IReadOnlyList<ChatMessage> history = channel.History;
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("a", history[0].Text);
            Assert.AreEqual("b", history[1].Text);
        }

        [Test]
        public void CapacityOne_AlwaysHoldsLatestOnly()
        {
            ChatChannel channel = new ChatChannel("sys", ChatChannelType.System, 1);
            channel.Append(Msg("1", "a"));
            channel.Append(Msg("2", "b"));
            channel.Append(Msg("3", "c"));

            IReadOnlyList<ChatMessage> history = channel.History;
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("c", history[0].Text);
        }

        [Test]
        public void Append_FiresOnMessageWithChannelAndMessage()
        {
            ChatChannel channel = new ChatChannel("world", ChatChannelType.World, 5);
            int fired = 0;
            ChatChannel captured = null;
            string capturedText = null;
            channel.OnMessage += (ch, msg) =>
            {
                fired++;
                captured = ch;
                capturedText = msg.Text;
            };

            channel.Append(Msg("1", "hello"));

            Assert.AreEqual(1, fired);
            Assert.AreSame(channel, captured);
            Assert.AreEqual("hello", capturedText);
        }

        [Test]
        public void History_ReturnsIndependentSnapshot()
        {
            ChatChannel channel = new ChatChannel("world", ChatChannelType.World, 5);
            channel.Append(Msg("1", "a"));

            IReadOnlyList<ChatMessage> snapshot = channel.History;
            Assert.AreEqual(1, snapshot.Count);

            // 追加后旧快照不应改变。
            channel.Append(Msg("2", "b"));
            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual(2, channel.History.Count);
        }
    }
}
