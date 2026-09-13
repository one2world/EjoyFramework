# UI Module Guide

End-to-end guide for building UI in **EjoyFramework**. The UI module is **strictly MVVM** — every form is a `MvvmView<TViewModel>` with declarative widget binders. There is no other supported path.

> **TL;DR for impatient developers**
> 1. Drop `Assets/GameMain/Framework/Prefabs/EjoyFramework.prefab` into the startup scene.
> 2. Author a `UIMyForm.prefab` with children named `B_<Property>_<Widget>`.
> 3. Menu: `EjoyFramework/UI/Scaffold Form from Selected Prefab` — generates view + ViewModel and auto-wires binders.
> 4. Open the generated ViewModel, add `SetField(ref m_X, value)` properties / `RelayCommand` commands.
> 5. `GameEntry.UI.OpenUIForm("UIMyForm", "Window", 0, false, null);`

---

## 1. Module overview

```
EjoyFramework.Core.UI               pure C# — interfaces, manager, stack, registry
EjoyFramework.Core.UI.Mvvm          pure C# — observable, command, value converter, path
EjoyFramework.Core.Unity            Unity adapters — UIComponent, MvvmView, binders
EjoyFramework.Core.Unity.Editor.UI        Editor tooling — codegen, prefab scaffold
```

**Only one form base class:**

| Base class | What you write | What the framework writes |
|---|---|---|
| `MvvmView<TViewModel>` | `CreateViewModel`, VM with `[Bindable]` properties / `RelayCommand`s, prefab widget names | binder MonoBehaviours auto-attached to `B_*_<Widget>` children; Bind/Unbind lifecycle |

> `UIFormBehaviour` is now `abstract` and Framework-internal infrastructure. **Do not inherit it directly** — always extend `MvvmView<TViewModel>`.

---

## 2. Architecture map

```
[EjoyFramework.prefab]
└─ UI                            UIComponent (registry of UIGroups + facade for UIManager)
   ├─ [UICamera]                 UICameraHelper          Orthographic, depth=10, UI-layer only
   ├─ [EventSystem]              EventSystem + InputSystemUIInputModule (or StandaloneInputModule)
   ├─ Canvas:Background          UIGroupCanvasHelper (sortingOrder=0)
   ├─ Canvas:Scene               …    (100)
   ├─ Canvas:HUD                 …    (200)
   ├─ Canvas:Window              …    (300)
   ├─ Canvas:Modal               …    (400)
   ├─ Canvas:Tip                 …    (500)
   ├─ Canvas:System              …    (600)
   └─ Canvas:Top                 …    (700)
```

Standard groups enumerated in `UIGroupKind`. Step 100 reserved between groups so custom intermediate groups can be inserted by business code without renumbering.

---

## 3. Form lifecycle

```
Open                         ↓
UIManager.OpenUIForm
   resource loaded → instantiate prefab → MvvmView.OnInit
   ├─ first-time:  CreateViewModel(userData) → IndexBinders → Bind all → OnViewModelBound
   └─ reused-from-pool: Unbind old → CreateViewModel(userData) → Bind all → OnViewModelBound
   ↓
OnOpen(userData)                        SetActive(true)
   ↓
( OnPause / OnCover  / OnResume / OnReveal )      ← binders survive Pause/Resume; subscriptions kept
   ↓
Close
   OnViewModelUnbinding → Unbind all → OnClose(isShutdown, userData)
   ↓
OnRecycle                              ViewModel = null; prefab returns to instance pool
```

Business hooks (all `virtual`, default impl is no-op):

| Hook | When | Use for |
|---|---|---|
| `CreateViewModel(userData)` | first init + every reuse | construct VM, optionally seed from userData |
| `OnViewModelBound()` | after all binders attached | subscribe to VM events, kick off first data load |
| `OnViewModelUnbinding()` | before binders detach | unsubscribe from VM events |
| `OnOpen / OnClose / OnPause / OnResume` | standard form lifecycle | animations, save state |

> `MvvmView.OnFirstInit / OnReuseInit` are `sealed`. Use the hooks above.

---

## 4. Building a form — end-to-end

### 4.1 Step 1 — author the prefab with `B_<Property>_<Widget>` names

```
UIShop  (RectTransform)
├─ B_Title_Text             (Text)              ← VM.Title           (string)
├─ B_Coins_Text             (Text)              ← VM.Coins           (int, formatted)
├─ B_Buy_Button             (Button)            ← VM.BuyCommand      (ICommand)
├─ B_Loading_Visibility     (any GameObject)    ← VM.IsLoading       (bool → SetActive)
├─ B_Volume_Slider          (Slider)            ← VM.Volume          (float, two-way)
└─ B_Items_List             (ScrollRect.content)← VM.Items           (ObservableList<ItemVM>)
```

The widget tag after the last `_` selects the binder:

| Tag | Binder | Default mode |
|---|---|---|
| `Text` | `TextBinder` (or `TMPTextBinder` if a TMP component is present) | OneWay |
| `Image`, `Img` | `ImageBinder` | OneWay |
| `RawImage` | `RawImageBinder` | OneWay |
| `Button`, `Btn` | `ButtonBinder` (auto-suffixes property → `Command`) | OneWay |
| `Toggle` | `ToggleBinder` | TwoWay |
| `Slider` | `SliderBinder` | TwoWay |
| `Dropdown` | `DropdownBinder` | TwoWay |
| `Input`, `InputField` | `InputFieldBinder` | TwoWay |
| `Visible`, `Visibility` | `VisibilityBinder` | OneWay |
| `Interactable` | `InteractableBinder` | OneWay |
| `Color` | `ColorBinder` | OneWay |
| `List` | `ListBinder` (item prefab set in Inspector) | OneWay |

### 4.2 Step 2 — scaffold

`EjoyFramework/UI/Scaffold Form from Selected Prefab` generates two .cs files **and** auto-attaches the right binder on each `B_*` child with the right `PropertyPath`. No manual wiring.

Generated:

```csharp
// Assets/GameMain/Scripts/UI/Forms/UIShop.cs
public sealed class UIShop : MvvmView<UIShopViewModel>
{
    protected override UIShopViewModel CreateViewModel(object userData) => new UIShopViewModel();
    protected override void OnViewModelBound() { /* TODO subscribe / kick-off */ }
}

// Assets/GameMain/Scripts/UI/ViewModels/UIShopViewModel.cs
public sealed class UIShopViewModel : BindableObject
{
    private string m_Title;
    public string Title { get { return m_Title; } set { SetField(ref m_Title, value); } }

    private ICommand m_CloseCommand;
    public ICommand CloseCommand => m_CloseCommand ??= new RelayCommand(OnClose);
    private void OnClose() { /* TODO */ }
}
```

### 4.3 Step 3 — fill the ViewModel

Use `SetField(ref m_X, value)` for properties to get automatic `PropertyChanged` notification. For observable collections use `ObservableList<T>`.

```csharp
public sealed class UIShopViewModel : BindableObject
{
    private string m_Title;
    private int m_Coins;
    private bool m_IsLoading;
    private float m_Volume;

    public string Title { get { return m_Title; } set { SetField(ref m_Title, value); } }
    public int Coins   { get { return m_Coins; } set { SetField(ref m_Coins, value); } }
    public bool IsLoading { get { return m_IsLoading; } set { SetField(ref m_IsLoading, value); } }
    public float Volume   { get { return m_Volume; } set { SetField(ref m_Volume, value); } }

    public ObservableList<ItemVM> Items { get; } = new ObservableList<ItemVM>();

    public ICommand BuyCommand { get; }

    public UIShopViewModel()
    {
        BuyCommand = new AsyncRelayCommand(BuyAsync, canExecute: () => !IsLoading);
    }

    private async Task BuyAsync()
    {
        IsLoading = true;
        try { /* call backend */ }
        finally { IsLoading = false; }
    }
}
```

The UI reacts to every property change automatically.

### 4.4 Step 4 — register + open

```csharp
// One-time registration (e.g. in ProcedureLaunch)
GameEntry.UI.Registry.Register(new UIFormDef(
    id: 1001,
    assetPath: "Assets/GameMain/UI/Prefabs/UIShop.prefab",
    groupName: "Window",
    priority: 0));

// Open
GameEntry.UI.Open(1001);
// or by asset name:
GameEntry.UI.OpenUIForm("UIShop", "Window", 0, false, null);
```

Closing is symmetric: `GameEntry.UI.CloseUIForm(serialId)`.

---

## 5. Observable primitives

### 5.1 `BindableObject`

Base class for every VM. `SetField` is the idiomatic setter — short-circuits on no-change and raises `PropertyChanged` automatically.

```csharp
private string m_Name;
public string Name { get { return m_Name; } set { SetField(ref m_Name, value); } }
```

For derived properties (one source change → multiple notifications):

```csharp
public string Display => $"{Name} ({Age})";

private string m_Name;
public string Name { get { return m_Name; } set {
    if (SetField(ref m_Name, value)) RaisePropertyChanged(nameof(Display));
}}
```

### 5.2 `BindableProperty<T>`

Standalone observable value — useful when a single value is shared across multiple VMs, or when a model doesn't inherit `BindableObject`.

```csharp
public readonly BindableProperty<int> Score = new BindableProperty<int>(0);

vm.Score.Value = 42;
vm.Score.ValueChanged += v => Debug.Log($"Score = {v}");
int currentScore = vm.Score;          // implicit conversion
```

### 5.3 `ObservableList<T>`

Drop-in replacement for `List<T>` that publishes granular events. `ListBinder` consumes them for incremental UI updates.

```csharp
var list = new ObservableList<ItemVM>();
list.Add(new ItemVM());            // → ItemInserted
list[0] = new ItemVM();            // → ItemReplaced
list.RemoveAt(0);                  // → ItemRemoved
list.Move(0, 2);                   // → ItemMoved
list.Clear();                      // → Reset (single event, not N Removed)
list.ReplaceAll(newItems);         // → Reset

// Bulk update without N events:
using (list.BeginBatch())
{
    for (int i = 0; i < 1000; i++) list.Add(...);
}
// → single Reset on dispose, no per-item events
```

Nested batches are supported and depth-counted — only the outermost exit fires.

---

## 6. Commands

### 6.1 `RelayCommand` — sync, no params

```csharp
public ICommand CloseCommand => m_Close ??= new RelayCommand(OnClose);
public ICommand SaveCommand  => m_Save  ??= new RelayCommand(OnSave, canExecute: () => IsDirty);
```

When `CanExecute` becomes stale (e.g., `IsDirty` flips), the VM calls `((RelayCommand)SaveCommand).RaiseCanExecuteChanged()` — the bound Button updates its `interactable` automatically.

### 6.2 `RelayCommand<T>` — typed parameter

```csharp
public ICommand PickItemCommand => m_PickItem ??= new RelayCommand<int>(OnPickItem);
private void OnPickItem(int itemId) { … }
```

`ButtonBinder.CommandParameter` ships the string parameter to the command; for non-string params bind via business code in `OnViewModelBound`.

### 6.3 `AsyncRelayCommand` — Task-returning, auto-reentrancy guard

```csharp
public ICommand LoginCommand { get; }

public LoginVm()
{
    LoginCommand = new AsyncRelayCommand(LoginAsync, canExecute: () => !string.IsNullOrEmpty(Username));
}

private async Task LoginAsync()
{
    // While the Task is running, IsExecuting=true → CanExecute=false → Button greys out.
    await api.Login(Username, Password);
}
```

No need to manage "isExecuting" by hand. Exceptions inside the Task are caught and logged via `FrameworkLog.Error`.

---

## 7. Value converters

Drop a converter MonoBehaviour onto any GameObject (often the form root) and reference it from a binder's **Converter** field.

| Built-in | Effect |
|---|---|
| `BoolNegateConverter` | `!bool` |
| `NumberFormatConverter` | `(int/float/decimal).ToString("F2"/"N0"/"P0"/...)` with invariant culture |
| `NullToBoolConverter` | null / Unity-fake-null / empty-string → false (invert optional) |
| `EnumToStringConverter` | enum ↔ name with optional `KeyPrefix` for localization |

Write your own — implement `IValueConverter`:

```csharp
public sealed class HealthToColorConverter : MonoBehaviour, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter)
    {
        float pct = System.Convert.ToSingle(value);
        return pct switch
        {
            > 0.66f => Color.green,
            > 0.33f => Color.yellow,
            _       => Color.red,
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter) => null;
}
```

Then in Inspector: `ColorBinder.Converter` → `HealthToColorConverter` instance.

---

## 8. Lists with `ListBinder`

`ListBinder` consumes an `ObservableList<T>` and instantiates one item view per element. Two ways to populate item view widgets:

### 8.1 `IListItemBinder` (fast, no reflection)

```csharp
public sealed class ItemView : MonoBehaviour, IListItemBinder
{
    [SerializeField] private Text NameLabel;
    [SerializeField] private Image Icon;

    public void OnBindListItem(object itemData)
    {
        var item = (ItemVM)itemData;
        NameLabel.text = item.Name;
        Icon.sprite = item.Icon;
    }
}
```

### 8.2 Nested VM (recursive binder discovery)

If `itemData` is itself a `BindableObject`, ListBinder treats every `BinderBase` inside the item view as bound to that item VM. So you can build the item view like any other MVVM form with `B_*_<Widget>` children.

> Performance: ListBinder Instantiates per item. For long lists, use `RecyclableScrollList` instead — it's a virtualized list that reuses a fixed pool of cells.

---

## 9. Nested data contexts — `MvvmContext`

Use when a sub-tree needs its own VM independent of the form's root VM. Common case: master/detail panel where the detail half shows the currently selected item.

```
UIInventory
├─ MvvmContext (DetailPanel)            DataContext = ShopViewModel.SelectedItem
│   ├─ B_Name_Text                       ← SelectedItem.Name
│   └─ B_Description_Text                ← SelectedItem.Description
└─ B_Items_List                           ← ShopViewModel.Items
```

In code:

```csharp
[SerializeField] private MvvmContext m_DetailContext;

protected override void OnViewModelBound()
{
    ViewModel.PropertyChanged += (s, n) => {
        if (n == nameof(UIShopViewModel.SelectedItem))
            m_DetailContext.DataContext = ViewModel.SelectedItem;
    };
}
```

`MvvmView.IndexBinders` automatically skips binders inside any nested `MvvmContext` subtree — they're managed by the context itself, never bound twice.

---

## 10. Editor menus

| Menu | What it does |
|---|---|
| `EjoyFramework/UI/Generate UIFormId` | Generates an enum of all form IDs |
| `EjoyFramework/UI/Add SafeAreaFitter to Selection` | Attaches notch/round-corner safe-area handler |
| `EjoyFramework/UI/Create UI Root in Scene` | Builds the UI Root hierarchy directly in the current scene |
| `EjoyFramework/UI/Export UI Root Prefab` | Saves a standalone `UIRoot.prefab` (for UI-only debug scenes) |
| `EjoyFramework/UI/Auto-Wire Bindings on Selected Prefab` | Attaches MVVM binders to `B_*_<Widget>` children of an existing prefab |
| `EjoyFramework/UI/Scaffold Form from Selected Prefab` | Full scaffold: `<Form>.cs` + `<Form>ViewModel.cs` + auto-wired binders |

---

## 11. Performance notes

- **Property change reflection cache** — `PropertyAccessor` compiles `Expression<Func<…>>` per property on first access, then caches the delegate. Subsequent reads/writes are within 2× of native field access (∼10× faster than `PropertyInfo.GetValue`).
- **Per-handler exception isolation** — `SafeInvoker.Invoke` walks the multicast invocation list and catches per subscriber. A bad binder no longer kills the others. Cost: a `GetInvocationList()` allocation only when there are 2+ subscribers (fast path for the common 1-subscriber case).
- **`ObservableList.BeginBatch`** — collapse N adds/removes into a single `Reset`. Use for bulk initialization or server diff application.
- **`MvvmView.IndexBinders`** — runs once per form lifetime. Recycled prefab instances reuse the cached binder list with zero re-scan.
- **`UIGroupCanvasHelper.AutoSubCanvasPerForm`** — opt-in per group. Adds a Canvas + GraphicRaycaster per form so mesh rebuilds inside one form don't dirty siblings. Trade-off: slightly higher draw call count.
- **Visibility binder** vs **enabling components** — `VisibilityBinder` flips `SetActive`, which suspends the whole subtree (cheapest). Use `InteractableBinder` if you need to keep the visuals but disable input.

---

## 12. Common pitfalls

**Two-way property doesn't write back to VM.**
Likely cause: binder's `Mode` is `OneWay`. Set to `TwoWay` in the binder Inspector, or the scaffold tool already does this for Toggle/Slider/Dropdown/InputField.

**Form re-shown after pause shows stale data.**
Should not happen — `OnDisable` no longer unbinds (this was a v1 bug; fixed). If it recurs, ensure the binder subclass override of `OnDisable` calls `base.OnDisable()`.

**`B_Loading_Visibility` doesn't hide the GameObject.**
The bound property must be a `bool`. If it's `int`/`object`, wrap with a `NullToBoolConverter` or convert in the VM.

**Button stays interactable even after `CanExecute` returns false.**
You forgot to call `RaiseCanExecuteChanged()` on the command after changing the condition. `AsyncRelayCommand` does this automatically; `RelayCommand` requires the VM to invoke it.

**ListBinder re-instantiates everything on every change.**
Only on `Reset` (Clear/ReplaceAll). `Add`/`Remove`/`Replace` events are incremental. If you see full rebuilds elsewhere, you're probably reassigning the whole list reference (`vm.Items = new ObservableList…`) — keep the same instance and mutate it.

**TextMeshPro binder logs "TextMeshPro package not installed".**
Project doesn't include `com.unity.textmeshpro`. Install via Package Manager, or use plain `TextBinder` with `UnityEngine.UI.Text`.

**`MvvmView.OnFirstInit` / `OnReuseInit` can't be overridden.**
Intentional — they are `sealed`. Use `CreateViewModel`, `OnViewModelBound`, `OnViewModelUnbinding`.

---

## 13. API quick reference

| Type | Namespace | Purpose |
|---|---|---|
| `UIComponent` | `EjoyFramework.Runtime` | Framework facade; lives on `[EjoyFramework]/UI` |
| `UIController`, `UIStack` | `EjoyFramework.UI` | Stack-based navigation, modal handling |
| `UIFormDef`, `UIFormRegistry` | `EjoyFramework.UI` | Form definition + registry |
| `MvvmView<TVM>` | `EjoyFramework.Runtime` | **The** form base class — every business form extends this |
| `BinderBase` | `EjoyFramework.Runtime` | Abstract binder root; 13 built-in subclasses |
| `MvvmContext` | `EjoyFramework.Runtime` | Nested DataContext provider |
| `IListItemBinder` | `EjoyFramework.Runtime` | Optimization interface for list item views |
| `BindableObject` | `EjoyFramework.UI.Mvvm` | ViewModel base |
| `BindableProperty<T>` | `EjoyFramework.UI.Mvvm` | Standalone observable value |
| `ObservableList<T>` | `EjoyFramework.UI.Mvvm` | Observable collection with granular events |
| `RelayCommand`, `RelayCommand<T>`, `AsyncRelayCommand` | `EjoyFramework.UI.Mvvm` | `ICommand` implementations |
| `BindingPath`, `PropertyAccessor` | `EjoyFramework.UI.Mvvm` | Internal — usually not touched by business code |
| `IValueConverter` | `EjoyFramework.UI.Mvvm` | Custom value converter contract |
| `UIRootHelper`, `UIGroupCanvasHelper` | `EjoyFramework.Runtime` | Scene-side helpers (under `EjoyFramework.prefab`) |
| `SafeAreaFitter`, `UIAspectRatioFitter`, `UIResolutionAdapter` | `EjoyFramework.Runtime` | Device adaptation helpers |
| `RecyclableScrollList` | `EjoyFramework.Runtime` | Virtualized list for large datasets |

---

## 14. Where things live

```
Packages/com.ejoy.framework/
├─ Runtime/EjoyFramework.Core/UI/                  core: manager, stack, controller, form-def
│  └─ Mvvm/                                         observable, command, accessor, path, converter
├─ Runtime/EjoyFramework.Core.Unity/UI/             Unity adapters: UIComponent (+ scene helpers)
│  │                                                 safe-area, aspect-fitter, group canvas
│  └─ Mvvm/                                         MvvmView, 13 binders, MvvmContext, IListItemBinder
│     └─ Converters/                                4 built-in value converters
├─ Editor/EjoyFramework.Core.Unity.Editor/UI/             UIFormId generator, UIRoot setup
│  └─ Mvvm/                                         scaffold + auto-wire generator
└─ Tests/Editor/EjoyFramework.Tests/                MvvmCoreTests — 23 NUnit cases
   └─ ../Runtime/EjoyFramework.Tests.PlayMode/      MvvmBinderTests — PlayMode binder lifecycle
```
