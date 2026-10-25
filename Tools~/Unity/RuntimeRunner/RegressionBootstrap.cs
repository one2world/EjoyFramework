using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.Events;
public sealed class RegressionBootstrap : MonoBehaviour
{
    static string Argument(string name) { var a=Environment.GetCommandLineArgs(); int i=Array.IndexOf(a,name); return i>=0&&i+1<a.Length?a[i+1]:null; }
    void Start()
    {
        DontDestroyOnLoad(gameObject);
        try
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var assembly=typeof(UnityEngine.TestTools.UnityTestAttribute).Assembly;
            var type=assembly.GetType("UnityEngine.TestTools.TestRunner.PlaymodeTestsController",true);
            var go=new GameObject("Code-based tests runner");go.SetActive(false);
            var controller=go.AddComponent(type);
            type.GetField("m_AssembliesWithTests",flags).SetValue(controller,new List<string>{"EjoyFramework.Tests.PlayMode"});
            var settings=type.GetField("settings",flags).GetValue(controller);
            var settingType=settings.GetType();
            var filterType=assembly.GetType("UnityEngine.TestTools.TestRunner.GUI.RuntimeTestRunnerFilter",true);
            var filters=Array.CreateInstance(filterType,1);filters.SetValue(Activator.CreateInstance(filterType,true),0);
            settingType.GetField("filters",flags).SetValue(settings,filters);
            var featureField=settingType.GetField("featureFlags",flags);featureField.SetValue(settings,Activator.CreateInstance(featureField.FieldType,true));
            settingType.GetField("automated",flags).SetValue(settings,true);
            type.GetField("RunInfrastructureHasRegistered",flags).SetValue(controller,true);
            ((UnityEvent<ITestResult>)type.GetField("runFinishedEvent",flags).GetValue(controller)).AddListener(Finished);
            go.SetActive(true);
        }
        catch(Exception e) { Debug.LogException(e);Application.Quit(1); }
    }
    void Finished(ITestResult result)
    {
        File.WriteAllText(Argument("-regressionResults"),result.ToXml(true).OuterXml);
        Debug.Log("REGRESSION_RESULT passed="+result.PassCount+" failed="+result.FailCount+" skipped="+result.SkipCount);
        Application.Quit(result.FailCount==0&&result.PassCount==43?0:1);
    }
    void Update() { if(Time.realtimeSinceStartup>120) { Debug.LogError("Regression runner timed out"); Application.Quit(2); } }
}
