//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 测试用临时路径：统一放在工程内 <c>Temp/EjoyTests/</c>（EditMode 工作目录即工程根，<c>Temp/</c> 已被 git 忽略），
    /// 不写系统临时目录。调用方负责删除自己创建的文件/目录。
    /// </summary>
    internal static class TestTempPaths
    {
        /// <summary>根目录（按需创建）。</summary>
        public static string Root
        {
            get
            {
                string root = Path.GetFullPath(Path.Combine("Temp", "EjoyTests"));
                Directory.CreateDirectory(root);
                return root;
            }
        }

        /// <summary>创建一个唯一的空文件并返回其绝对路径（对应 Path.GetTempFileName 的语义）。</summary>
        public static string NewFile()
        {
            string path = Path.Combine(Root, "tmp_" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(path, new byte[0]);
            return path;
        }
    }
}
