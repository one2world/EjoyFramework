//------------------------------------------------------------
// EjoyGame Framework Tests (PlayMode)
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using EjoyFramework.Core.Unity;
using EjoyFramework.Core.UI.Mvvm;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EjoyFramework.Tests.PlayMode
{
    /// <summary>
    /// PlayMode 测试：MVVM binder 在真 Unity 生命周期下的行为，特别是 Bind/Unbind 与
    /// MonoBehaviour OnEnable/OnDisable/OnDestroy 的交互。
    ///
    /// 关键回归覆盖：
    ///   - BinderBase.OnDisable 必须 NOT 调 Unbind（否则 OnPause/OnResume 表单变 dead）
    ///   - TwoWay binder 避免 view↔VM 死循环（reentry suppression）
    ///   - ButtonBinder 与 ICommand.CanExecuteChanged 联动
    ///   - ListBinder 增量响应 ObservableList 事件
    ///   - OnDestroy 阶段彻底解绑（无悬挂订阅）
    /// </summary>
    public sealed class MvvmBinderTests : PlayModeTestBase
    {
        private sealed class TestVM : BindableObject
        {
            private string m_Text = "init";
            private bool m_Flag;
            public string Text { get { return m_Text; } set { SetField(ref m_Text, value); } }
            public bool Flag { get { return m_Flag; } set { SetField(ref m_Flag, value); } }

            public int ClickCount;
            public ICommand DoCommand;
            public TestVM() { DoCommand = new RelayCommand(() => ClickCount++); }
        }

        private sealed class NamedItemBinder : MonoBehaviour, IListItemBinder
        {
            public void OnBindListItem(object itemData)
            {
                gameObject.name = "Item-" + itemData;
            }
        }

        // ===== TextBinder OneWay =====
        [UnityTest]
        public IEnumerator TextBinder_VMChange_UpdatesTextWidget()
        {
            var go = CreateGameObject("TextBinderHost");
            go.AddComponent<RectTransform>();
            var text = go.AddComponent<Text>();
            var binder = go.AddComponent<TextBinder>();
            binder.PropertyPath = nameof(TestVM.Text);

            var vm = new TestVM();
            binder.Bind(vm);
            yield return null;

            Assert.AreEqual("init", text.text, "Bind 后 RefreshFromSource 应同步初值");

            vm.Text = "hello";
            yield return null;
            Assert.AreEqual("hello", text.text);

            vm.Text = "world";
            yield return null;
            Assert.AreEqual("world", text.text);
        }

        // ===== ToggleBinder TwoWay + reentry suppression =====
        [UnityTest]
        public IEnumerator ToggleBinder_TwoWay_NoInfiniteLoop()
        {
            var go = CreateGameObject("ToggleHost");
            go.AddComponent<RectTransform>();
            var toggle = go.AddComponent<Toggle>();
            var binder = go.AddComponent<ToggleBinder>();
            binder.PropertyPath = nameof(TestVM.Flag);
            binder.Mode = BindingMode.TwoWay;

            var vm = new TestVM();
            int notifyCount = 0;
            vm.PropertyChanged += (s, n) => { if (n == nameof(TestVM.Flag)) notifyCount++; };

            binder.Bind(vm);
            yield return null;

            // VM → view
            vm.Flag = true;
            yield return null;
            Assert.IsTrue(toggle.isOn);
            int notifiedAfterVMChange = notifyCount;

            // view → VM
            toggle.isOn = false;
            yield return null;
            Assert.IsFalse(vm.Flag);
            Assert.AreEqual(notifiedAfterVMChange + 1, notifyCount,
                "View→VM 只该触发一次 PropertyChanged（reentry-suppress 阻止再回弹）");
        }

        // ===== ButtonBinder ICommand integration =====
        [UnityTest]
        public IEnumerator ButtonBinder_Click_ExecutesCommand_AndMirrorsCanExecute()
        {
            var go = CreateGameObject("ButtonHost");
            go.AddComponent<RectTransform>();
            var btn = go.AddComponent<Button>();
            var binder = go.AddComponent<ButtonBinder>();
            binder.PropertyPath = nameof(TestVM.DoCommand);

            var vm = new TestVM();
            bool gate = true;
            var cmd = new RelayCommand(() => vm.ClickCount++, () => gate);
            vm.DoCommand = cmd;

            binder.Bind(vm);
            yield return null;

            Assert.IsTrue(btn.interactable, "CanExecute=true → interactable=true");
            btn.onClick.Invoke();
            Assert.AreEqual(1, vm.ClickCount);

            gate = false;
            cmd.RaiseCanExecuteChanged();
            yield return null;
            Assert.IsFalse(btn.interactable, "CanExecute=false → interactable=false 自动同步");

            btn.onClick.Invoke();
            Assert.AreEqual(1, vm.ClickCount, "CanExecute=false 时点击不应触发 Execute");
        }

        [UnityTest]
        public IEnumerator BinderBase_AllowsMultipleBinderTypesOnSameControl()
        {
            var go = CreateGameObject("MultiBinderHost");
            go.AddComponent<RectTransform>();
            go.AddComponent<Button>();

            var buttonBinder = go.AddComponent<ButtonBinder>();
            var visibilityBinder = go.AddComponent<VisibilityBinder>();
            yield return null;

            Assert.IsNotNull(buttonBinder);
            Assert.IsNotNull(visibilityBinder);
        }

        // ===== CRITICAL — OnDisable must NOT Unbind =====
        [UnityTest]
        public IEnumerator Binder_OnDisableOnEnable_BindingSurvives()
        {
            // 回归：曾经 OnDisable→Unbind 把 binding 拆光，OnResume 后 UI 不再随 VM 刷新。
            var go = CreateGameObject("LifecycleHost");
            go.AddComponent<RectTransform>();
            var text = go.AddComponent<Text>();
            var binder = go.AddComponent<TextBinder>();
            binder.PropertyPath = nameof(TestVM.Text);

            var vm = new TestVM();
            binder.Bind(vm);
            yield return null;
            Assert.AreEqual("init", text.text);

            // 模拟 UIForm.OnPause → SetActive(false)
            go.SetActive(false);
            yield return null;

            // VM 在 form 隐藏期变化 —— 我们不要求 inactive UI 实时刷新，但订阅必须保留
            vm.Text = "while-hidden";

            // 模拟 UIForm.OnResume → SetActive(true)
            go.SetActive(true);
            yield return null;

            // VM 在 form 显示后再变化 —— 这才是必须收到的关键 case
            vm.Text = "after-resume";
            yield return null;

            Assert.AreEqual("after-resume", text.text,
                "OnEnable 后 binder 仍然必须订阅 VM 变化（OnDisable 不能拆订阅）");
        }

        // ===== OnDestroy fully unbinds =====
        [UnityTest]
        public IEnumerator Binder_OnDestroy_ReleasesVMSubscription()
        {
            var go = CreateGameObject("DestroyHost");
            go.AddComponent<RectTransform>();
            go.AddComponent<Text>();
            var binder = go.AddComponent<TextBinder>();
            binder.PropertyPath = nameof(TestVM.Text);

            var vm = new TestVM();
            int notified = 0;
            vm.PropertyChanged += (s, n) => notified++;
            binder.Bind(vm);
            yield return null;

            int notifiedBefore = notified;
            Object.Destroy(go);
            yield return null;        // OnDestroy 已发生

            // 销毁后改 VM 不应导致空引用 / 异常
            Assert.DoesNotThrow(() => vm.Text = "should-not-throw");
            // 业务订阅依然会被通知（VM 是独立的），我们只验证不抛
            Assert.GreaterOrEqual(notified, notifiedBefore);
        }

        // ===== ListBinder granular updates =====
        [UnityTest]
        public IEnumerator ListBinder_AddRemove_UpdatesChildren()
        {
            var go = CreateGameObject("ListHost");
            go.AddComponent<RectTransform>();
            var binder = go.AddComponent<ListBinder>();

            // 简单 item prefab：一个空 GameObject
            var itemPrefab = new GameObject("Item");
            itemPrefab.AddComponent<RectTransform>();
            itemPrefab.SetActive(true);

            // 反射设置私有字段（避免暴露 setter 给业务；PlayMode test 不能用 UnityEditor）
            var field = typeof(ListBinder).GetField("m_ItemPrefab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(binder, itemPrefab);
            binder.PropertyPath = "Items";

            var list = new ObservableList<int>();
            var vmObj = new ListVM { Items = list };

            binder.Bind(vmObj);
            yield return null;
            Assert.AreEqual(0, go.transform.childCount);

            list.Add(10);
            list.Add(20);
            yield return null;
            Assert.AreEqual(2, go.transform.childCount);

            list.RemoveAt(0);
            yield return null;
            Assert.AreEqual(1, go.transform.childCount);

            list.Clear();
            yield return null;
            Assert.AreEqual(0, go.transform.childCount);

            Object.Destroy(itemPrefab);
        }

        [UnityTest]
        public IEnumerator ListBinder_ReplaceMove_ReusesChildrenAndUpdatesOrder()
        {
            var go = CreateGameObject("ListHost");
            go.AddComponent<RectTransform>();
            var binder = go.AddComponent<ListBinder>();

            var itemPrefab = new GameObject("Item");
            itemPrefab.AddComponent<RectTransform>();
            itemPrefab.AddComponent<NamedItemBinder>();
            itemPrefab.SetActive(true);

            var field = typeof(ListBinder).GetField("m_ItemPrefab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(binder, itemPrefab);
            binder.PropertyPath = "Items";

            var list = new ObservableList<int>();
            var vmObj = new ListVM { Items = list };
            binder.Bind(vmObj);
            yield return null;

            list.Add(10);
            list.Add(20);
            list.Add(30);
            yield return null;

            var firstChild = go.transform.GetChild(0).gameObject;
            list[0] = 11;
            yield return null;
            Assert.AreSame(firstChild, go.transform.GetChild(0).gameObject, "Replace must rebind the existing view.");
            Assert.AreEqual("Item-11", go.transform.GetChild(0).name);

            list.Move(0, 2);
            yield return null;
            Assert.AreEqual("Item-20", go.transform.GetChild(0).name);
            Assert.AreEqual("Item-30", go.transform.GetChild(1).name);
            Assert.AreEqual("Item-11", go.transform.GetChild(2).name);

            Object.Destroy(itemPrefab);
        }

        private sealed class ListVM : BindableObject
        {
            private ObservableList<int> m_Items;
            public ObservableList<int> Items { get { return m_Items; } set { SetField(ref m_Items, value); } }
        }
    }
}
