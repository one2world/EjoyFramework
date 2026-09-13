//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.ObjectPool;
using EjoyFramework.Core.UI;
using EjoyFramework.Core.UI.Mvvm;
using EjoyFramework.Tests.TestSupport;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// UIController + UIStack + UIFormRegistry 集成测试。
    /// 覆盖 Push/Pop/Back/PopUntil/Replace/Modal/AllowMultiple 全部语义。
    /// </summary>
    public class UIControllerStackTests
    {
        // ===== fakes =====
        private class FakeUIForm : IUIForm
        {
            public int SerialId { get; private set; }
            public string UIFormAssetName { get; private set; }
            public object Handle { get; private set; }
            public IUIGroup UIGroup { get; private set; }
            public int DepthInUIGroup { get; private set; }
            public bool PauseCoveredUIForm { get; private set; }
            public bool RefocusCalled, CloseCalled;

            public void OnInit(int serialId, string assetName, IUIGroup g, bool pause, bool isNew, object userData)
            {
                SerialId = serialId; UIFormAssetName = assetName; UIGroup = g;
                PauseCoveredUIForm = pause; Handle = new object();
            }
            public void OnRecycle() { }
            public void OnOpen(object userData) { }
            public void OnClose(bool s, object userData) { CloseCalled = true; }
            public void OnPause() { }
            public void OnResume() { }
            public void OnCover() { }
            public void OnReveal() { }
            public void OnRefocus(object userData) { RefocusCalled = true; }
            public void OnUpdate(float a, float b) { }
            public void OnDepthChanged(int gd, int dig) { DepthInUIGroup = dig; }
        }

        private class FakeUIFormHelper : IUIFormHelper
        {
            public object InstantiateUIForm(object asset) { return new object(); }
            public IUIForm CreateUIForm(object instance, IUIGroup g, object userData) { return new FakeUIForm(); }
            public void ReleaseUIForm(object asset, object instance) { }
        }
        private class FakeUIGroupHelper : IUIGroupHelper
        {
            // Test fixture: no container side effects. Real Unity helper reparents Handle.
            public void AttachUIForm(IUIForm form) { }
        }

        // ===== fixture =====
        private UIManager m_UI;
        private ObjectPoolManager m_OP;
        private UIFormRegistry m_Reg;
        private UIController m_Ctl;

        private const int IdMain = 1001;
        private const int IdSettings = 1002;
        private const int IdConfirmModal = 1003;
        private const int IdToast = 1004;
        private static readonly ScreenRoute<ScreenViewModel, ScreenArguments> MainRoute = new ScreenRoute<ScreenViewModel, ScreenArguments>(IdMain);
        private static readonly DialogRoute<ConfirmViewModel, MvvmNoArguments, int> ConfirmRoute = new DialogRoute<ConfirmViewModel, MvvmNoArguments, int>(IdConfirmModal);

        private readonly struct ScreenArguments
        {
            public ScreenArguments(string title) { Title = title; }
            public string Title { get; }
        }

        private sealed class ScreenViewModel : BindableObject
        {
            public ScreenViewModel(string title) { Title = title; }
            public string Title { get; }
        }

        private sealed class ConfirmViewModel : BindableObject
        {
            public ConfirmViewModel(DialogControl<int> dialog) { Dialog = dialog; }
            public DialogControl<int> Dialog { get; }
        }

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
            m_OP = new ObjectPoolManager();
            var res = new MockResourceManager { ThrowOnHandleApi = true };
            m_UI = new UIManager(res, m_OP);
            m_UI.SetUIFormHelper(new FakeUIFormHelper());
            m_UI.AddUIGroup("Default", 0, new FakeUIGroupHelper());
            m_UI.AddUIGroup("Modal", 100, new FakeUIGroupHelper());
            m_UI.AddUIGroup("Tip", 200, new FakeUIGroupHelper());

            m_Reg = new UIFormRegistry();
            m_Reg.Register(new UIFormDef(IdMain,         "UI/Main",     "Default", priority: 0));
            m_Reg.Register(new UIFormDef(IdSettings,     "UI/Settings", "Default", priority: 0, allowMultiple: false));
            m_Reg.Register(new UIFormDef(IdConfirmModal, "UI/Confirm",  "Modal",   priority: 0, modal: true, pauseCoveredUIForm: true));
            m_Reg.Register(new UIFormDef(IdToast,        "UI/Toast",    "Tip",     priority: 0, allowMultiple: true));

            m_Ctl = new UIController(m_UI, m_Reg);
        }

        [TearDown]
        public void Teardown()
        {
            m_UI.Shutdown();
            m_OP.Shutdown();
        }

        // ===== tests =====

        [Test]
        public void Open_PushOntoCorrectGroupStack()
        {
            m_Ctl.Open(IdMain);
            var stack = m_Ctl.GetStack("Default");
            Assert.AreEqual(1, stack.Count);
            Assert.AreEqual(IdMain, stack.Top.FormDefId);
        }

        [Test]
        public void Back_ClosesTopFormAndRefocusesNewTop()
        {
            m_Ctl.Open(IdMain);
            m_Ctl.Open(IdSettings);
            var stack = m_Ctl.GetStack("Default");
            Assert.AreEqual(2, stack.Count);

            var mainEntry = stack.Top; // Settings 在顶
            Assert.AreEqual(IdSettings, mainEntry.FormDefId);

            bool popped = m_Ctl.Back("Default");
            Assert.IsTrue(popped);
            Assert.AreEqual(1, stack.Count);
            Assert.AreEqual(IdMain, stack.Top.FormDefId);
            // 新栈顶被 Refocus
            Assert.IsTrue(((FakeUIForm)stack.Top.Form).RefocusCalled);
        }

        [Test]
        public void Open_AllowMultipleFalse_RefocusesExistingInsteadOfReopening()
        {
            m_Ctl.Open(IdMain);
            var firstSettings = m_Ctl.Open(IdSettings);
            int beforeCount = m_UI.GetAllLoadedUIForms().Length;

            // 再次 Open Settings (AllowMultiple=false) → 不创建新实例，而是 Refocus
            var secondSettings = m_Ctl.Open(IdSettings);
            int afterCount = m_UI.GetAllLoadedUIForms().Length;

            Assert.AreEqual(beforeCount, afterCount, "Should NOT create a second instance");
            Assert.AreEqual(firstSettings.SerialId, secondSettings.SerialId,
                "Push should return a snapshot of the existing entry.");
            Assert.AreSame(firstSettings.Form, secondSettings.Form,
                "Refocus must preserve the existing form instance.");
            Assert.IsTrue(((FakeUIForm)firstSettings.Form).RefocusCalled);
        }

        [Test]
        public void ModalForm_TracksInModalSet()
        {
            m_Ctl.Open(IdMain);
            Assert.IsFalse(m_Ctl.IsAnyModalOpen);

            m_Ctl.Open(IdConfirmModal);
            Assert.IsTrue(m_Ctl.IsAnyModalOpen);

            m_Ctl.Back("Modal");
            Assert.IsFalse(m_Ctl.IsAnyModalOpen);
        }

        [Test]
        public void PopAllModal_ClosesEveryModalForm()
        {
            m_Ctl.Open(IdMain);
            m_Ctl.Open(IdConfirmModal);
            m_Ctl.Open(IdConfirmModal); // AllowMultiple defaults true? — IdConfirmModal allowMultiple defaults false in our setup
            // 实际上 IdConfirmModal 没显式 allowMultiple → 默认 false → 第二次 Open 走 Refocus
            // 所以 modal 只有 1 个
            Assert.AreEqual(1, m_Ctl.GetStack("Modal").Count);

            m_Ctl.PopAllModal();
            Assert.IsFalse(m_Ctl.IsAnyModalOpen);
            Assert.AreEqual(0, m_Ctl.GetStack("Modal").Count,
                "PopAllModal must remove the closed form from its owning stack.");
        }

        [Test]
        public void CloseUIForm_OutsideStackOperation_RemovesStackEntry()
        {
            UIStack.StackEntry entry = m_Ctl.Open(IdMain);
            m_Ctl.CloseUIForm(entry.SerialId);
            Assert.AreEqual(0, m_Ctl.GetStack("Default").Count);
        }

        [Test]
        public void MvvmNavigator_Push_CreatesTypedViewModelAsFormUserData()
        {
            var registry = new MvvmRouteRegistry(m_Reg);
            registry.Register(MainRoute,
                (in ScreenArguments arguments) => new ScreenViewModel(arguments.Title));

            using (var router = new UIRouter(m_Ctl, registry))
            {
                var arguments = new ScreenArguments("Inventory");
                ScreenHandle<ScreenViewModel> handle = router.Open(MainRoute, in arguments);

                Assert.AreEqual("Inventory", handle.ViewModel.Title);
                Assert.IsNull(m_Ctl.GetStack("Default").Top.UserData,
                    "Routed MVVM must not transport the ViewModel through object userData.");
                ScreenViewModel resolved;
                Assert.IsTrue(router.TryResolve(handle.SerialId, out resolved));
                Assert.AreSame(handle.ViewModel, resolved);
            }
        }

        [Test]
        public void MvvmNavigator_ModalClose_CompletesTypedResultAndRemovesStackEntry()
        {
            var registry = new MvvmRouteRegistry(m_Reg);
            registry.RegisterDialog(ConfirmRoute,
                (in MvvmNoArguments arguments, DialogControl<int> dialog) =>
                    new ConfirmViewModel(dialog));

            using (var router = new UIRouter(m_Ctl, registry))
            {
                var arguments = new MvvmNoArguments();
                DialogHandle<int> handle = router.Show(ConfirmRoute, in arguments);
                ConfirmViewModel viewModel;
                Assert.IsTrue(router.TryResolve(handle.SerialId, out viewModel));
                int result = 7;
                viewModel.Dialog.Complete(in result);

                DialogCompletion<int> completion;
                Assert.IsTrue(handle.TryConsume(out completion));
                Assert.AreEqual(DialogState.Completed, completion.State);
                Assert.IsTrue(completion.HasResult);
                Assert.AreEqual(7, completion.Result);
                Assert.AreEqual(0, m_Ctl.GetStack("Modal").Count);
            }
        }

        [Test]
        public void MvvmNavigator_Back_CancelsPendingModalResult()
        {
            var registry = new MvvmRouteRegistry(m_Reg);
            registry.RegisterDialog(ConfirmRoute,
                (in MvvmNoArguments arguments, DialogControl<int> dialog) =>
                    new ConfirmViewModel(dialog));

            using (var router = new UIRouter(m_Ctl, registry))
            {
                var arguments = new MvvmNoArguments();
                DialogHandle<int> handle = router.Show(ConfirmRoute, in arguments);
                router.Back("Modal");

                DialogCompletion<int> completion;
                Assert.IsTrue(handle.TryConsume(out completion));
                Assert.AreEqual(DialogState.Canceled, completion.State);
                Assert.IsFalse(completion.HasResult);
            }
        }

        [Test]
        public void Replace_ClosesTopAndPushesNew()
        {
            m_Ctl.Open(IdMain);
            var stack = m_Ctl.GetStack("Default");
            stack.Replace(IdSettings);
            Assert.AreEqual(1, stack.Count);
            Assert.AreEqual(IdSettings, stack.Top.FormDefId);
        }

        [Test]
        public void PopUntil_PopsToButNotIncludingTarget()
        {
            m_Ctl.Open(IdMain);     // Default stack: [Main]
            m_Ctl.Open(IdSettings); // Default stack: [Main, Settings]
            m_Ctl.Open(IdSettings); // AllowMultiple=false → no-op (refocus existing)
            // To make a deeper stack, open another distinct form. But we only have Main/Settings in Default.
            // So just do the basic case:
            var stack = m_Ctl.GetStack("Default");
            int popped = stack.PopUntil(IdMain);
            Assert.AreEqual(1, popped); // 弹出 Settings
            Assert.AreEqual(IdMain, stack.Top.FormDefId);
        }

        [Test]
        public void AllowMultiple_OpensMultipleInstancesIndependently()
        {
            m_Ctl.Open(IdToast, "first");
            m_Ctl.Open(IdToast, "second");
            m_Ctl.Open(IdToast, "third");

            var stack = m_Ctl.GetStack("Tip");
            Assert.AreEqual(3, stack.Count);
            Assert.AreEqual(3, m_UI.GetAllLoadedUIForms().Length);
        }

        [Test]
        public void Push_ToWrongGroupStack_ThrowsHelpfulError()
        {
            // 把 IdMain (Default group) 推到 Modal stack
            var modalStack = m_Ctl.GetStack("Modal");
            Assert.Throws<FrameworkException>(() => modalStack.Push(IdMain));
        }
    }
}
