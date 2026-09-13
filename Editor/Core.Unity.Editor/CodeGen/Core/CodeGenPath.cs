using System;
using System.IO;
using UnityEditor.PackageManager;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>Resolves package assets without treating a package cache as editable source.</summary>
    public static class CodeGenPath
    {
        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith("Packages/", StringComparison.Ordinal)) return normalized;
            PackageInfo package = PackageInfo.FindForAssetPath(normalized);
            if (package == null) return normalized;
            string root = "Packages/" + package.name;
            return Path.GetFullPath(Path.Combine(package.resolvedPath,
                normalized.Substring(root.Length).TrimStart('/'))).Replace('\\', '/');
        }

        public static void RequireWritable(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Output path is required.", nameof(path));
            string fullPath = Path.GetFullPath(Resolve(path)).Replace('\\', '/').TrimEnd('/');
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
            {
                if (string.IsNullOrEmpty(package.resolvedPath)) continue;
                string root = Path.GetFullPath(package.resolvedPath).Replace('\\', '/').TrimEnd('/');
                if (!fullPath.Equals(root, comparison) && !fullPath.StartsWith(root + "/", comparison)) continue;
                if (package.source != PackageSource.Embedded && package.source != PackageSource.Local)
                    throw new InvalidOperationException("Cannot generate or delete files in immutable package '"
                        + package.name + "': " + path + ". Regenerate in an embedded or local package checkout.");
            }
        }
    }
}
