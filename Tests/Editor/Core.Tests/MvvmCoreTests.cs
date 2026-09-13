//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EjoyFramework.Core.UI.Mvvm;
using NUnit.Framework;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class MvvmCoreTests
    {
        // ===== BindableObject =====
        private sealed class SampleVM : BindableObject
        {
            private string m_Name;
            private int m_Score;

            public string Name { get { return m_Name; } set { SetField(ref m_Name, value); } }
            public int Score { get { return m_Score; } set { SetField(ref m_Score, value); } }

            public void MultiUpdate(string n, int s)
            {
                m_Name = n; m_Score = s;
                RaisePropertyChangedMany(nameof(Name), nameof(Score));
            }
        }

        [Test]
        public void BindableObject_SetField_RaisesOnceWhenChanged_NotWhenSame()
        {
            var vm = new SampleVM();
            int count = 0;
            string lastName = null;
            vm.PropertyChanged += (s, n) => { count++; lastName = n; };

            vm.Name = "alice";
            Assert.AreEqual(1, count);
            Assert.AreEqual("Name", lastName);

            vm.Name = "alice"; // 同值
            Assert.AreEqual(1, count, "Same value must not raise.");

            vm.Name = "bob";
            Assert.AreEqual(2, count);
        }

        [Test]
        public void BindableObject_HandlerException_OtherSubscribersStillFire()
        {
            // FastEvent 隔离每个订阅者 —— 第一个抛异常不阻止第二个被调用。
            // FrameworkLog 在测试上下文未安装 ILogHelper，错误被静默吞掉（不发 Debug.LogError），
            // 因此不需要 LogAssert.Expect；直接断言副作用即可。
            var vm = new SampleVM();
            int otherCalls = 0;
            vm.PropertyChanged += (s, n) => throw new InvalidOperationException("boom");
            vm.PropertyChanged += (s, n) => otherCalls++;

            Assert.DoesNotThrow(() => vm.Name = "x");
            Assert.AreEqual(1, otherCalls, "Second subscriber must receive the notification even though first threw.");
        }

        [Test]
        public void BindableObject_Unsubscribe_RemovesOnlyTargetHandler()
        {
            var vm = new SampleVM();
            int first = 0;
            int second = 0;
            Action<IBindable, string> firstHandler = (s, n) => first++;
            Action<IBindable, string> secondHandler = (s, n) => second++;

            vm.PropertyChanged += firstHandler;
            vm.PropertyChanged += secondHandler;
            vm.PropertyChanged -= firstHandler;

            vm.Name = "x";
            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second);
        }

        [Test]
        public void BindableObject_SubscriptionMutationDuringNotify_UsesStableSnapshot()
        {
            var vm = new SampleVM();
            int first = 0;
            int second = 0;
            Action<IBindable, string> secondHandler = (s, n) => second++;
            Action<IBindable, string> firstHandler = null;
            firstHandler = (s, n) =>
            {
                first++;
                vm.PropertyChanged -= secondHandler;
            };

            vm.PropertyChanged += firstHandler;
            vm.PropertyChanged += secondHandler;
            vm.Name = "x";

            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second, "Current notification must use the pre-mutation subscriber snapshot.");

            vm.Name = "y";
            Assert.AreEqual(2, first);
            Assert.AreEqual(1, second);
        }

        [Test]
        public void BindableObject_RaisePropertyChangedMany_FiresEachName()
        {
            var vm = new SampleVM();
            int count = 0;
            vm.PropertyChanged += (s, n) => count++;
            vm.MultiUpdate("z", 99);
            Assert.AreEqual(2, count);
            Assert.AreEqual("z", vm.Name);
            Assert.AreEqual(99, vm.Score);
        }

        // ===== BindableProperty<T> =====
        [Test]
        public void BindableProperty_ValueChanged_FiresOnDiff_NotOnSame()
        {
            var p = new BindableProperty<int>(0);
            int last = -1, calls = 0;
            p.ValueChanged += v => { last = v; calls++; };

            p.Value = 0;
            Assert.AreEqual(0, calls);
            p.Value = 5;
            Assert.AreEqual(1, calls);
            Assert.AreEqual(5, last);
            p.Value = 5;
            Assert.AreEqual(1, calls);
            p.Value = 7;
            Assert.AreEqual(2, calls);
        }

        [Test]
        public void BindableProperty_ImplicitOperator_ReturnsCurrentValue()
        {
            var p = new BindableProperty<string>("init");
            string s = p;
            Assert.AreEqual("init", s);
        }

        [Test]
        public void BindableProperty_HandlerException_OtherSubscribersStillFire()
        {
            var p = new BindableProperty<int>(0);
            int others = 0;
            p.ValueChanged += v => throw new InvalidOperationException("boom");
            p.ValueChanged += v => others++;
            p.Value = 1;
            Assert.AreEqual(1, others, "Isolation: bad subscriber must not block others.");
        }

        [Test]
        public void ObservableList_HandlerException_OtherSubscribersStillFire()
        {
            var list = new ObservableList<int>();
            int others = 0;
            ((IObservableList<int>)list).ItemInserted += (i, v) => throw new InvalidOperationException("boom");
            ((IObservableList<int>)list).ItemInserted += (i, v) => others++;
            list.Add(7);
            Assert.AreEqual(1, others);
        }

        [Test]
        public void RelayCommand_CanExecuteHandlerException_OtherSubscribersStillFire()
        {
            var cmd = new RelayCommand(() => { });
            int others = 0;
            cmd.CanExecuteChanged += () => throw new InvalidOperationException("boom");
            cmd.CanExecuteChanged += () => others++;
            cmd.RaiseCanExecuteChanged();
            Assert.AreEqual(1, others);
        }

        [Test]
        public void RelayCommand_Unsubscribe_RemovesCanExecuteHandler()
        {
            var cmd = new RelayCommand(() => { });
            int calls = 0;
            Action handler = () => calls++;

            cmd.CanExecuteChanged += handler;
            cmd.RaiseCanExecuteChanged();
            cmd.CanExecuteChanged -= handler;
            cmd.RaiseCanExecuteChanged();

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void BindableProperty_ForceNotify_FiresEvenWithSameValue()
        {
            var p = new BindableProperty<int>(10);
            int calls = 0;
            p.ValueChanged += _ => calls++;
            p.ForceNotify();
            Assert.AreEqual(1, calls);
        }

        // ===== ObservableList<T> =====
        [Test]
        public void ObservableList_BasicEvents_AddRemoveReplaceMove()
        {
            var list = new ObservableList<int>();
            int inserted = 0, removed = 0, replaced = 0, moved = 0, reset = 0;
            ((IObservableList<int>)list).ItemInserted += (i, v) => inserted++;
            ((IObservableList<int>)list).ItemRemoved += (i, v) => removed++;
            ((IObservableList<int>)list).ItemReplaced += (i, o, n) => replaced++;
            list.ItemMoved += (a, b) => moved++;
            list.Reset += () => reset++;

            list.Add(1); list.Add(2); list.Add(3);
            Assert.AreEqual(3, inserted);
            Assert.AreEqual(3, list.Count);

            list[1] = 20;
            Assert.AreEqual(1, replaced);
            Assert.AreEqual(20, list[1]);

            list[1] = 20; // 同值不触发
            Assert.AreEqual(1, replaced);

            list.Move(0, 2);
            Assert.AreEqual(1, moved);
            Assert.AreEqual(new[] { 20, 3, 1 }, list);

            list.RemoveAt(0);
            Assert.AreEqual(1, removed);
            Assert.AreEqual(2, list.Count);

            list.Clear();
            Assert.AreEqual(1, reset);
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void ObservableList_BatchMode_CollapsesToSingleReset()
        {
            var list = new ObservableList<int>();
            int inserted = 0, reset = 0;
            ((IObservableList<int>)list).ItemInserted += (i, v) => inserted++;
            list.Reset += () => reset++;

            using (list.BeginBatch())
            {
                for (int i = 0; i < 100; i++) list.Add(i);
            }
            Assert.AreEqual(0, inserted, "Batch suppresses individual events.");
            Assert.AreEqual(1, reset);
            Assert.AreEqual(100, list.Count);
        }

        [Test]
        public void ObservableList_NestedBatch_OuterChangesAreNotLost()
        {
            // 回归：早先的 BeginBatch 误在每次入 batch 都重置 hasChanges → 内嵌空 batch 之后
            // 外层的修改被吞掉。修复后嵌套 batch 仅依赖 depth 计数，hasChanges 始终单向累积。
            var list = new ObservableList<int>();
            int reset = 0;
            list.Reset += () => reset++;

            using (list.BeginBatch())
            {
                list.Add(1);                    // 外层修改：必须不丢
                using (list.BeginBatch())
                {
                    // 内层空 batch
                }
                // 退出内层后，外层 hasChanges 依然为 true
            }
            Assert.AreEqual(1, reset, "嵌套空 batch 不能吞掉外层 batch 的修改。");
        }

        [Test]
        public void ObservableList_ReplaceAll_FiresSingleReset()
        {
            var list = new ObservableList<int>(new[] { 1, 2, 3 });
            int reset = 0;
            list.Reset += () => reset++;
            list.ReplaceAll(new[] { 10, 20, 30, 40 });
            Assert.AreEqual(1, reset);
            Assert.AreEqual(4, list.Count);
            Assert.AreEqual(40, list[3]);
        }

        // ===== RelayCommand =====
        [Test]
        public void RelayCommand_CanExecute_GovernsExecute()
        {
            int calls = 0;
            bool gate = false;
            var cmd = new RelayCommand(() => calls++, () => gate);

            Assert.IsFalse(cmd.CanExecute(null));
            cmd.Execute(null);
            Assert.AreEqual(0, calls);

            gate = true;
            Assert.IsTrue(cmd.CanExecute(null));
            cmd.Execute(null);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void RelayCommand_RaiseCanExecuteChanged_NotifiesSubscribers()
        {
            var cmd = new RelayCommand(() => { });
            int calls = 0;
            cmd.CanExecuteChanged += () => calls++;
            cmd.RaiseCanExecuteChanged();
            cmd.RaiseCanExecuteChanged();
            Assert.AreEqual(2, calls);
        }

        [Test]
        public void RelayCommandT_TypedParameter_Routed()
        {
            string captured = null;
            var cmd = new RelayCommand<string>(s => captured = s);
            cmd.Execute("hello");
            Assert.AreEqual("hello", captured);
        }

        // ===== AsyncRelayCommand =====
        [Test]
        public async Task AsyncRelayCommand_BlocksReentryUntilComplete()
        {
            var tcs = new TaskCompletionSource<bool>();
            int starts = 0;
            var cmd = new AsyncRelayCommand(async () => { starts++; await tcs.Task; });

            Assert.IsTrue(cmd.CanExecute(null));
            cmd.Execute(null);
            Assert.IsTrue(cmd.IsExecuting);
            Assert.IsFalse(cmd.CanExecute(null), "Cannot execute while running.");

            cmd.Execute(null); // 应当被门挡住，不会再次启动
            Assert.AreEqual(1, starts);

            tcs.SetResult(true);
            await Task.Yield();
            await Task.Delay(10);
            Assert.IsFalse(cmd.IsExecuting);
            Assert.IsTrue(cmd.CanExecute(null));
        }

        // ===== PropertyAccessor =====
        [Test]
        public void PropertyAccessor_GetterSetter_PropertyAndField()
        {
            var vm = new SampleVM();
            var get = PropertyAccessor.GetGetter(typeof(SampleVM), "Score");
            var set = PropertyAccessor.GetSetter(typeof(SampleVM), "Score");
            Assert.IsNotNull(get); Assert.IsNotNull(set);
            set(vm, 42);
            Assert.AreEqual(42, vm.Score);
            Assert.AreEqual(42, get(vm));
        }

        [Test]
        public void PropertyAccessor_UnknownMember_ReturnsNull()
        {
            var get = PropertyAccessor.GetGetter(typeof(SampleVM), "DoesNotExist");
            Assert.IsNull(get);
        }

        [Test]
        public void PropertyAccessor_CachesPerMember()
        {
            var g1 = PropertyAccessor.GetGetter(typeof(SampleVM), "Name");
            var g2 = PropertyAccessor.GetGetter(typeof(SampleVM), "Name");
            Assert.AreSame(g1, g2);
        }

        // ===== BindingPath =====
        private sealed class Outer : BindableObject
        {
            public Inner Inner { get; set; }
        }

        private sealed class AlternateOuter : BindableObject
        {
            public AlternateInner Inner { get; set; }
        }

        private sealed class Inner : BindableObject
        {
            private string m_Name;
            public string Name { get { return m_Name; } set { SetField(ref m_Name, value); } }
        }

        private sealed class AlternateInner : BindableObject
        {
            private string m_Name;
            public string Name { get { return m_Name; } set { SetField(ref m_Name, value); } }
        }

        [Test]
        public void BindingPath_NestedResolveAndAssign()
        {
            var o = new Outer { Inner = new Inner { Name = "orig" } };
            var path = new BindingPath("Inner.Name");
            Assert.AreEqual("orig", path.Resolve(o));

            Assert.IsTrue(path.TryAssign(o, "new"));
            Assert.AreEqual("new", o.Inner.Name);
        }

        [Test]
        public void BindingPath_MidNullReturnsNull_AssignFalse()
        {
            var o = new Outer { Inner = null };
            var path = new BindingPath("Inner.Name");
            Assert.IsNull(path.Resolve(o));
            Assert.IsFalse(path.TryAssign(o, "x"));
        }

        [Test]
        public void BindingPath_ReusedAcrossCompatibleRuntimeTypes_ResolvesCorrectly()
        {
            var path = new BindingPath("Inner.Name");
            var first = new Outer { Inner = new Inner { Name = "first" } };
            var second = new AlternateOuter { Inner = new AlternateInner { Name = "second" } };

            Assert.AreEqual("first", path.Resolve(first));
            Assert.AreEqual("second", path.Resolve(second));
            Assert.IsTrue(path.TryAssign(first, "updated"));
            Assert.AreEqual("updated", first.Inner.Name);
        }

        [Test]
        public void BindingPath_RootSegment_FirstName()
        {
            Assert.AreEqual("Inner", new BindingPath("Inner.Name").RootSegment);
            Assert.AreEqual("Name", new BindingPath("Name").RootSegment);
            Assert.AreEqual("", new BindingPath("").RootSegment);
        }
    }
}
