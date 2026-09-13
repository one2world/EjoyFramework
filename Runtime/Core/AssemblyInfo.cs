//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Runtime.CompilerServices;

// EjoyFramework.Tests 直接构造 EntityManager / ObjectPoolManager 等 internal sealed 类型；
// 通过 InternalsVisibleTo 暴露给测试程序集，无需把这些类型改为 public。
[assembly: InternalsVisibleTo("EjoyFramework.Tests")]
