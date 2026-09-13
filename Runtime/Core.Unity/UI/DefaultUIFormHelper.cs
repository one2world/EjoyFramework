//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认界面辅助器（Unity GameObject）。
    /// InstantiateUIForm: GameObject.Instantiate prefab。
    /// CreateUIForm: 在 instance 上找 UIForm MonoBehaviour 组件（业务自行挂载）。
    /// ReleaseUIForm: GameObject.Destroy instance。
    /// </summary>
    public class DefaultUIFormHelper : MonoBehaviour, IUIFormHelper
    {
        public object InstantiateUIForm(object uiFormAsset)
        {
            if (uiFormAsset == null) return null;
            return Instantiate(uiFormAsset as UnityEngine.Object);
        }

        public IUIForm CreateUIForm(object uiFormInstance, IUIGroup uiGroup, object userData)
        {
            GameObject go = uiFormInstance as GameObject;
            if (go == null) return null;
            // 业务的 UIForm 实现需要继承 UnityEngine.MonoBehaviour 并实现 IUIForm
            IUIForm form = go.GetComponent(typeof(IUIForm)) as IUIForm;
            if (form == null)
            {
                // 兜底：自动加挂一个空的具体 UIForm。
                // 注意：UIFormBehaviour 是抽象类，AddComponent<UIFormBehaviour>() 会在运行时抛 ArgumentException，
                // 必须挂可实例化的具体子类 DefaultUIForm。
                form = go.AddComponent<DefaultUIForm>();
            }
            return form;
        }

        public void ReleaseUIForm(object uiFormAsset, object uiFormInstance)
        {
            if (uiFormInstance is GameObject go && go != null)
            {
                Destroy(go);
            }
        }
    }
}
