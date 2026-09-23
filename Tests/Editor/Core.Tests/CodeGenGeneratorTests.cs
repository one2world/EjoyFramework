//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using EjoyFramework.Core;
using EjoyFramework.Core.Unity.Editor.CodeGen;
using EjoyFramework.Core.Network;
using EjoyFramework.Core.Serialization;
using EjoyFramework.Tests.CodeGenSamples;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Tests
{
    public sealed class CodeGenGeneratorTests
    {
        [Test]
        public void CodeGenTypeUtil_ResolvesGeneratedPathInsideDeclaringAsmdef()
        {
            bool ok = CodeGenTypeUtil.TryGetAssemblyGeneratedPath(
                typeof(CodeGenGeneratorTests),
                "Serialization",
                "Sample.g.cs",
                out string path,
                out string reason);

            Assert.That(ok, Is.True, reason);
            Assert.That(path.Replace('\\', '/'), Is.EqualTo(CodeGenPath.Resolve(
                "Packages/com.ejoy.framework/Tests/Editor/Core.Tests/Generated/Serialization/Sample.g.cs")));
        }

        [Test]
        public void CodeGenTypeUtil_ResolvesGeneratedPathForUnityDefaultAssembly()
        {
            Type type = CreateDynamicPublicType(
                CodeGenTypeUtil.AssemblyCSharpName,
                "EjoyFramework.Tests.CodeGenDefaultAssemblySample");

            bool ok = CodeGenTypeUtil.TryGetAssemblyGeneratedPath(
                type,
                "MonoBehaviourSnapshots",
                "AssemblyCSharpSnapshot.g.cs",
                out string path,
                out string reason);

            Assert.That(ok, Is.True, reason);
            Assert.That(path.Replace('\\', '/'), Is.EqualTo(
                "Assets/Generated/MonoBehaviourSnapshots/AssemblyCSharpSnapshot.g.cs"));
        }

        [Test]
        public void CodeGenTypeUtil_SkipsUnknownNoAsmdefAssemblies()
        {
            Type type = CreateDynamicPublicType(
                "CodeGen.UnknownNoAsmdef",
                "EjoyFramework.Tests.CodeGenUnknownNoAsmdefSample");

            bool ok = CodeGenTypeUtil.TryGetAssemblyGeneratedPath(
                type,
                "Serialization",
                "Unknown.g.cs",
                out string path,
                out string reason);

            Assert.That(ok, Is.False);
            Assert.That(path, Is.Null);
            Assert.That(reason, Does.Contain("has no asmdef"));
        }

        [Test]
        public void CodeGenTypeUtil_GeneratedFileNameUsesFullNameToAvoidCollisions()
        {
            string left = CodeGenTypeUtil.GeneratedFileName(typeof(CodeGenFileNameA.DuplicateName));
            string right = CodeGenTypeUtil.GeneratedFileName(typeof(CodeGenFileNameB.DuplicateName));

            Assert.That(left, Is.Not.EqualTo(right));
            Assert.That(left, Does.Contain("CodeGenFileNameA"));
            Assert.That(right, Does.Contain("CodeGenFileNameB"));
        }

        [Test]
        public void Serializer_SkipsUnsupportedCollectionElementAsWholeMember()
        {
            string dir = NewTempDir(nameof(Serializer_SkipsUnsupportedCollectionElementAsWholeMember));
            try
            {
                var generator = new SerializerGenerator(
                    dir,
                    new[] { typeof(CodeGenSerializerUnsupportedCollectionSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSerializerUnsupportedCollectionSample)));

                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(string.Join("\n", result.Messages), Does.Contain("UnsupportedItems"));
                Assert.That(source, Does.Contain("this.Supported"));
                Assert.That(source, Does.Not.Contain("UnsupportedItems"));
                Assert.That(source, Does.Not.Contain("CodeGenUnsupportedPayload"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void Serializer_SkipsDuplicateInheritedMemberNames()
        {
            string dir = NewTempDir(nameof(Serializer_SkipsDuplicateInheritedMemberNames));
            try
            {
                var generator = new SerializerGenerator(
                    dir,
                    new[] { typeof(CodeGenSerializerDuplicateDerivedSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSerializerDuplicateDerivedSample)));
                string messages = string.Join("\n", result.Messages);
                int writeCount = source.Split(new[] { "buffer.WriteInt(this.Value);" }, StringSplitOptions.None).Length - 1;

                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(messages, Does.Contain("duplicate serialized member name is ambiguous"));
                Assert.That(writeCount, Is.EqualTo(1));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void Serializer_WritesUnityValueTypesAndCommonContainers()
        {
            string dir = NewTempDir(nameof(Serializer_WritesUnityValueTypesAndCommonContainers));
            try
            {
                var generator = new SerializerGenerator(
                    dir,
                    new[] { typeof(CodeGenSerializerUnityContainerSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSerializerUnityContainerSample)));

                Assert.That(result.Skipped, Is.EqualTo(0));
                Assert.That(source, Does.Contain("new UnityEngine.RectInt(buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt())"));
                Assert.That(source, Does.Contain("new UnityEngine.Bounds("));
                Assert.That(source, Does.Contain("new UnityEngine.BoundsInt("));
                Assert.That(source, Does.Contain("new UnityEngine.LayerMask()"));
                Assert.That(source, Does.Contain("new UnityEngine.Matrix4x4()"));
                Assert.That(source, Does.Contain("System.Collections.Generic.KeyValuePair<System.String, UnityEngine.Vector3>"));
                Assert.That(source, Does.Contain(".Sort((_a"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Dictionary<System.String, UnityEngine.Vector3>(_n"));
                Assert.That(source, Does.Contain("System.StringComparer.OrdinalIgnoreCase"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.HashSet<System.Int32>"));
                Assert.That(source, Does.Contain("EqualityComparer<System.Int32>.Default"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Queue<System.String>"));
                Assert.That(source, Does.Contain(".Enqueue(_e"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Stack<System.Int32>"));
                Assert.That(source, Does.Contain(".Push(_items"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void SerializerGeneratedSample_RoundTripsCommonContainersUnityTypesAndComparers()
        {
            var source = new BinarySerializerContainerSample
            {
                Points = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Alpha"] = new Vector3(1f, 2f, 3f),
                    ["beta"] = new Vector3(4f, 5f, 6f),
                },
                Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fire", "Water" },
                Names = new Queue<string>(new[] { "Nezha", "Aobing", "Taiyi" }),
                History = new Stack<int>(),
                Matrix = Matrix4x4.identity,
            };
            source.History.Push(10);
            source.History.Push(20);
            source.History.Push(30);
            source.Matrix[0, 3] = 7f;
            source.Matrix[1, 3] = 8f;
            var mask = new LayerMask();
            mask.value = 1 << 9;
            source.Mask = mask;

            ByteBuffer buffer = ByteBuffer.Acquire();
            try
            {
                source.Serialize(buffer);
                buffer.RewindRead();

                var restored = new BinarySerializerContainerSample();
                restored.Deserialize(buffer);

                Assert.That(restored.Points.ContainsKey("alpha"), Is.True);
                Assert.That(restored.Points["ALPHA"], Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(restored.Tags.Contains("FIRE"), Is.True);
                CollectionAssert.AreEqual(new[] { "Nezha", "Aobing", "Taiyi" }, restored.Names.ToArray());
                CollectionAssert.AreEqual(new[] { 30, 20, 10 }, restored.History.ToArray());
                Assert.That(restored.Matrix[0, 3], Is.EqualTo(7f));
                Assert.That(restored.Matrix[1, 3], Is.EqualTo(8f));
                Assert.That(restored.Mask.value, Is.EqualTo(1 << 9));
            }
            finally
            {
                buffer.Release();
            }
        }

        [Test]
        public void MonoBehaviourSnapshot_WritesUnityValueTypesAndCommonContainers()
        {
            string dir = NewTempDir(nameof(MonoBehaviourSnapshot_WritesUnityValueTypesAndCommonContainers));
            try
            {
                var generator = new MonoBehaviourSnapshotGenerator(
                    dir,
                    new[] { typeof(CodeGenSnapshotUnityContainerSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSnapshotUnityContainerSample)));

                Assert.That(result.Skipped, Is.EqualTo(0));
                Assert.That(source, Does.Contain("new UnityEngine.Bounds("));
                Assert.That(source, Does.Contain("new UnityEngine.LayerMask()"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Dictionary<System.String, UnityEngine.Vector3>(_n"));
                Assert.That(source, Does.Contain("System.StringComparer.OrdinalIgnoreCase"));
                Assert.That(source, Does.Contain(".Sort((_a"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.HashSet<System.Int32>"));
                Assert.That(source, Does.Contain("EqualityComparer<System.Int32>.Default"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Queue<UnityEngine.Vector2Int>"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Stack<UnityEngine.RectInt>"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void MonoBehaviourSnapshot_SkipsOpenGenericTargets()
        {
            string dir = NewTempDir(nameof(MonoBehaviourSnapshot_SkipsOpenGenericTargets));
            try
            {
                var generator = new MonoBehaviourSnapshotGenerator(
                    dir,
                    new[] { typeof(CodeGenSnapshotOpenGenericSample<>) });

                CodeGenResult result = generator.Run();
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Written, Is.EqualTo(0));
                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(messages, Does.Contain("open generic MonoBehaviour snapshot types are not supported"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void Serializer_SkipsNestedGeneratedOnlyCompositeMembers()
        {
            string dir = NewTempDir(nameof(Serializer_SkipsNestedGeneratedOnlyCompositeMembers));
            try
            {
                var generator = new SerializerGenerator(
                    dir,
                    new[] { typeof(CodeGenSerializerNestedGeneratedOnlyContainer) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSerializerNestedGeneratedOnlyContainer)));
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(messages, Does.Contain("nested [GenerateSerializer] types are skipped by SerializerGenerator"));
                Assert.That(source, Does.Not.Contain("this.Payload"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void Serializer_WritesPolymorphicCompositeMembers()
        {
            string dir = NewTempDir(nameof(Serializer_WritesPolymorphicCompositeMembers));
            try
            {
                var generator = new SerializerGenerator(
                    dir,
                    new[] { typeof(CodeGenSerializerPolymorphicContainerSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSerializerPolymorphicContainerSample)));
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Skipped, Is.EqualTo(0), messages);
                Assert.That(source, Does.Contain("this.Payload"));
                Assert.That(source, Does.Contain("this.GenericPayload"));
                Assert.That(source, Does.Contain("this.Payloads"));
                Assert.That(source, Does.Contain("CodeGenSerializerPolymorphicConcretePayload"));
                Assert.That(source, Does.Contain("CodeGenSerializerGenericIntPayload"));
                Assert.That(source, Does.Contain("buffer.WriteInt(0);"));
                Assert.That(source, Does.Contain("switch (_polyTypeId"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void Serializer_UsesExactRuntimeTypeForPolymorphicDiscriminator()
        {
            string dir = NewTempDir(nameof(Serializer_UsesExactRuntimeTypeForPolymorphicDiscriminator));
            try
            {
                var generator = new SerializerGenerator(
                    dir,
                    new[] { typeof(CodeGenSerializerHierarchyContainerSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSerializerHierarchyContainerSample)));
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Skipped, Is.EqualTo(0), messages);
                Assert.That(source, Does.Contain(
                    "_polyType0 == typeof(EjoyFramework.Tests.CodeGenSerializerHierarchyMiddlePayload)"));
                Assert.That(source, Does.Contain(
                    "_polyType0 == typeof(EjoyFramework.Tests.CodeGenSerializerHierarchyLeafPayload)"));
                Assert.That(source, Does.Not.Contain(
                    "_polyValue0 is EjoyFramework.Tests.CodeGenSerializerHierarchyMiddlePayload"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void Serializer_SkipsPolymorphicCandidatesOutsideAsmdefReferenceGraph()
        {
            string dir = NewTempDir(nameof(Serializer_SkipsPolymorphicCandidatesOutsideAsmdefReferenceGraph));
            Type dynamicPayload = CreateDynamicPolymorphicPayloadType();
            try
            {
                var generator = new SerializerGenerator(
                    dir,
                    new[] { typeof(CodeGenSerializerPolymorphicContainerSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSerializerPolymorphicContainerSample)));
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Skipped, Is.EqualTo(0), messages);
                Assert.That(source, Does.Contain("CodeGenSerializerPolymorphicConcretePayload"));
                Assert.That(source, Does.Not.Contain(dynamicPayload.FullName));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void MonoBehaviourSnapshot_SkipsNestedGeneratedOnlyCompositeMembers()
        {
            string dir = NewTempDir(nameof(MonoBehaviourSnapshot_SkipsNestedGeneratedOnlyCompositeMembers));
            try
            {
                var generator = new MonoBehaviourSnapshotGenerator(
                    dir,
                    new[] { typeof(CodeGenSnapshotNestedGeneratedOnlySample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSnapshotNestedGeneratedOnlySample)));
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(messages, Does.Contain("nested [GenerateSerializer] types are skipped by SerializerGenerator"));
                Assert.That(source, Does.Not.Contain("this.Payload"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void MonoBehaviourSnapshot_WritesSerializeReferencePolymorphicFields()
        {
            string dir = NewTempDir(nameof(MonoBehaviourSnapshot_WritesSerializeReferencePolymorphicFields));
            try
            {
                var generator = new MonoBehaviourSnapshotGenerator(
                    dir,
                    new[] { typeof(CodeGenSnapshotSerializeReferenceSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenSnapshotSerializeReferenceSample)));
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Skipped, Is.EqualTo(0), messages);
                Assert.That(source, Does.Contain("this.Managed"));
                Assert.That(source, Does.Contain("this.GenericManaged"));
                Assert.That(source, Does.Contain("CodeGenSnapshotManagedConcrete"));
                Assert.That(source, Does.Contain("CodeGenSnapshotGenericManagedInt"));
                Assert.That(source, Does.Contain("this.ConcreteManaged"));
                Assert.That(source, Does.Contain("this.ConcreteManagedList"));
                Assert.That(source, Does.Contain("CodeGenSnapshotConcreteManagedBase"));
                Assert.That(source, Does.Contain("CodeGenSnapshotConcreteManagedDerived"));
                Assert.That(source, Does.Contain("_polyType"));
                Assert.That(source, Does.Contain("switch (_polyTypeId"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void DeepCopy_WritesCommonContainersThroughSharedShapeSupport()
        {
            string dir = NewTempDir(nameof(DeepCopy_WritesCommonContainersThroughSharedShapeSupport));
            try
            {
                var generator = new DeepCopyGenerator(
                    dir,
                    new[] { typeof(CodeGenDeepCopyContainerSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenDeepCopyContainerSample)));

                Assert.That(result.Skipped, Is.EqualTo(0));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Dictionary<System.String, System.Int32>"));
                Assert.That(source, Does.Contain(".Comparer"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.HashSet<System.Int32>"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Queue<System.String>"));
                Assert.That(source, Does.Contain(".Enqueue(_e"));
                Assert.That(source, Does.Contain("new System.Collections.Generic.Stack<System.Int32>"));
                Assert.That(source, Does.Contain(".Push(_e"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void DeepCopy_SkipsOpenGenericTargets()
        {
            string dir = NewTempDir(nameof(DeepCopy_SkipsOpenGenericTargets));
            try
            {
                var generator = new DeepCopyGenerator(
                    dir,
                    new[] { typeof(CodeGenDeepCopyOpenGenericSample<>) });

                CodeGenResult result = generator.Run();
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Written, Is.EqualTo(0));
                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(messages, Does.Contain("泛型定义暂不支持深拷贝生成"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void DeepCopy_SkipsDuplicateInheritedMemberNames()
        {
            string dir = NewTempDir(nameof(DeepCopy_SkipsDuplicateInheritedMemberNames));
            try
            {
                var generator = new DeepCopyGenerator(
                    dir,
                    new[] { typeof(CodeGenDeepCopyDuplicateDerivedSample) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenDeepCopyDuplicateDerivedSample)));
                string messages = string.Join("\n", result.Messages);
                int copyCount = source.Split(new[] { "target.Value = this.Value;" }, StringSplitOptions.None).Length - 1;

                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(messages, Does.Contain("duplicate serialized member name is ambiguous"));
                Assert.That(copyCount, Is.EqualTo(1));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void PacketCodec_IncludesGenerateSerializerPacketBeforeInterfaceExists()
        {
            string dir = NewTempDir(nameof(PacketCodec_IncludesGenerateSerializerPacketBeforeInterfaceExists));
            string outputPath = Path.Combine(dir, "GeneratedPacketCodec.g.cs");
            Type packetType = CreatePendingGeneratedPacketType(960001);

            try
            {
                var generator = new PacketCodecGenerator(
                    outputPath,
                    "EjoyFramework.Tests.Generated",
                    new[] { packetType });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(outputPath);

                Assert.That(result.Skipped, Is.EqualTo(0));
                Assert.That(string.Join("\n", result.Messages), Does.Not.Contain("未实现 IBinarySerializable"));
                Assert.That(source, Does.Contain("case 960001: packet = new " + CodeGenTypeUtil.TypeName(packetType)));
                Assert.That(source, Does.Contain("((IBinarySerializable)packet).Deserialize(body);"));
                Assert.That(source, Does.Contain("public static int GetPacketId(Packet packet)"));
                Assert.That(source, Does.Contain("if (packet is " + CodeGenTypeUtil.TypeName(packetType) + ") return 960001;"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void PacketCodec_SkipsNonPublicPacketTypes()
        {
            string dir = NewTempDir(nameof(PacketCodec_SkipsNonPublicPacketTypes));
            string outputPath = Path.Combine(dir, "GeneratedPacketCodec.g.cs");
            try
            {
                var generator = new PacketCodecGenerator(
                    outputPath,
                    "EjoyFramework.Tests.Generated",
                    new[] { typeof(CodeGenInternalCodecPacket) });

                CodeGenResult result = generator.Run();
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Written, Is.EqualTo(0));
                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(messages, Does.Contain("PacketCodec 无法从生成代码可见地引用该 Packet 类型"));
                Assert.That(File.Exists(outputPath), Is.False);
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void PacketHandlerRegistry_UsesCSharpTypeNamesAndSkipsOpenGenerics()
        {
            string dir = NewTempDir(nameof(PacketHandlerRegistry_UsesCSharpTypeNamesAndSkipsOpenGenerics));
            string outputPath = Path.Combine(dir, "PacketRegistryAutoInit.g.cs");
            try
            {
                var generator = new PacketHandlerRegistryGenerator(
                    outputPath,
                    "EjoyFramework.Tests.Generated",
                    new[] { typeof(CodeGenPacketHandlerOuter.NestedHandler), typeof(CodeGenOpenGenericHandler<>) });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(outputPath);

                Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(source, Does.Contain("new EjoyFramework.Tests.CodeGenPacketHandlerOuter.NestedHandler()"));
                Assert.That(source, Does.Not.Contain("+"));
                Assert.That(source, Does.Not.Contain("CodeGenOpenGenericHandler<"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        [Test]
        public void EventArgsGenerator_SkipsUnsupportedTypeShapesAndIndexerProperties()
        {
            string dir = NewTempDir(nameof(EventArgsGenerator_SkipsUnsupportedTypeShapesAndIndexerProperties));
            try
            {
                var generator = new EventArgsGenerator(
                    dir,
                    new[]
                    {
                        typeof(CodeGenEventArgsSample),
                        typeof(CodeGenEventArgsOuter.NestedEventArgs),
                        typeof(CodeGenGenericEventArgs<>),
                        typeof(CodeGenNonFrameworkEventArgs),
                    });

                CodeGenResult result = generator.Run();
                string source = File.ReadAllText(GeneratedPath(dir, typeof(CodeGenEventArgsSample)));
                string messages = string.Join("\n", result.Messages);

                Assert.That(result.Written, Is.EqualTo(1));
                Assert.That(result.Skipped, Is.EqualTo(3));
                Assert.That(messages, Does.Contain("NestedEventArgs"));
                Assert.That(messages, Does.Contain("CodeGenGenericEventArgs"));
                Assert.That(messages, Does.Contain("不是 FrameworkEventArgs 子类"));
                Assert.That(source, Does.Contain("public static CodeGenEventArgsSample Create(System.String name)"));
                Assert.That(source, Does.Not.Contain("Item"));
            }
            finally
            {
                DeleteTempDir(dir);
            }
        }

        private static string NewTempDir(string testName)
        {
            string dir = Path.Combine(TestTempPaths.Root, "ejoy_codegen_tests", testName + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GeneratedPath(string dir, Type type)
        {
            return Path.Combine(dir, CodeGenTypeUtil.GeneratedFileName(type));
        }

        private static void DeleteTempDir(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        private static Type CreateDynamicPublicType(string assemblyName, string typeName)
        {
            string uniqueAssemblyName = assemblyName == CodeGenTypeUtil.AssemblyCSharpName
                ? assemblyName
                : assemblyName + "." + Guid.NewGuid().ToString("N");
            var name = new AssemblyName(uniqueAssemblyName);
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule(uniqueAssemblyName);
            TypeBuilder type = module.DefineType(
                typeName + Guid.NewGuid().ToString("N"),
                TypeAttributes.Public | TypeAttributes.Class);
            type.DefineDefaultConstructor(MethodAttributes.Public);
            return type.CreateType();
        }

        private static Type CreatePendingGeneratedPacketType(int packetId)
        {
            string suffix = Guid.NewGuid().ToString("N");
            string assemblyName = "EjoyFramework.Tests.DynamicCodeGen." + suffix;
            var name = new AssemblyName(assemblyName);
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
            TypeBuilder type = module.DefineType(
                "EjoyFramework.Tests.DynamicCodeGen.PendingGeneratedPacket" + suffix,
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(Packet));

            type.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(GeneratePacketCodecAttribute).GetConstructor(new[] { typeof(int) }),
                new object[] { packetId }));
            type.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(GenerateSerializerAttribute).GetConstructor(Type.EmptyTypes),
                Array.Empty<object>()));
            type.DefineDefaultConstructor(MethodAttributes.Public);

            PropertyBuilder id = type.DefineProperty("Id", PropertyAttributes.None, typeof(int), Type.EmptyTypes);
            MethodBuilder getId = type.DefineMethod(
                "get_Id",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                typeof(int),
                Type.EmptyTypes);
            ILGenerator idIl = getId.GetILGenerator();
            idIl.Emit(OpCodes.Ldc_I4, packetId);
            idIl.Emit(OpCodes.Ret);
            id.SetGetMethod(getId);
            type.DefineMethodOverride(getId, typeof(BaseEventArgs).GetProperty("Id").GetGetMethod());

            MethodBuilder clear = type.DefineMethod(
                "Clear",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(void),
                Type.EmptyTypes);
            clear.GetILGenerator().Emit(OpCodes.Ret);
            type.DefineMethodOverride(clear, typeof(FrameworkEventArgs).GetMethod("Clear"));

            return type.CreateType();
        }

        private static Type CreateDynamicPolymorphicPayloadType()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string assemblyName = "EjoyFramework.Tests.DynamicCodeGen." + suffix;
            var name = new AssemblyName(assemblyName);
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
            TypeBuilder type = module.DefineType(
                "EjoyFramework.Tests.DynamicCodeGen.DynamicPolymorphicPayload" + suffix,
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(CodeGenSerializerPolymorphicPayload),
                new[] { typeof(IBinarySerializable) });
            type.DefineDefaultConstructor(MethodAttributes.Public);

            MethodBuilder serialize = type.DefineMethod(
                "Serialize",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(void),
                new[] { typeof(ByteBuffer) });
            serialize.GetILGenerator().Emit(OpCodes.Ret);
            type.DefineMethodOverride(serialize, typeof(IBinarySerializable).GetMethod("Serialize"));

            MethodBuilder deserialize = type.DefineMethod(
                "Deserialize",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(void),
                new[] { typeof(ByteBuffer) });
            deserialize.GetILGenerator().Emit(OpCodes.Ret);
            type.DefineMethodOverride(deserialize, typeof(IBinarySerializable).GetMethod("Deserialize"));

            return type.CreateType();
        }
    }

    public partial class CodeGenSerializerUnsupportedCollectionSample
    {
        public List<int> Supported;
        public List<CodeGenUnsupportedPayload> UnsupportedItems;
    }

    public sealed class CodeGenUnsupportedPayload
    {
        public string Value;
    }

    public class CodeGenSerializerDuplicateBaseSample
    {
        public int Value;
    }

    public partial class CodeGenSerializerDuplicateDerivedSample : CodeGenSerializerDuplicateBaseSample
    {
        public new int Value;
        public int Other;
    }

    public partial class CodeGenSerializerUnityContainerSample
    {
        public RectInt Area;
        public Bounds Bounds;
        public BoundsInt GridBounds;
        public LayerMask Mask;
        public Matrix4x4 Matrix;
        public Dictionary<string, Vector3> Points;
        public HashSet<int> Ids;
        public Queue<string> Names;
        public Stack<int> History;
    }

    public partial class CodeGenSerializerPolymorphicContainerSample
    {
        public CodeGenSerializerPolymorphicPayload Payload;
        public CodeGenSerializerGenericPayload<int> GenericPayload;
        public List<CodeGenSerializerPolymorphicPayload> Payloads;
    }

    [GenerateSerializer]
    public abstract partial class CodeGenSerializerPolymorphicPayload
    {
        public int Value;
    }

    public sealed class CodeGenSerializerPolymorphicConcretePayload
        : CodeGenSerializerPolymorphicPayload, IBinarySerializable
    {
        public string Name;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Value);
            buffer.WriteString(Name);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Value = buffer.ReadInt();
            Name = buffer.ReadString();
        }
    }

    public abstract class CodeGenSerializerGenericPayload<T>
    {
        public T Value;
    }

    public sealed class CodeGenSerializerGenericIntPayload
        : CodeGenSerializerGenericPayload<int>, IBinarySerializable
    {
        public int Extra;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Value);
            buffer.WriteInt(Extra);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Value = buffer.ReadInt();
            Extra = buffer.ReadInt();
        }
    }

    public partial class CodeGenSerializerHierarchyContainerSample
    {
        public CodeGenSerializerHierarchyBasePayload Payload;
    }

    public abstract class CodeGenSerializerHierarchyBasePayload
    {
        public int BaseValue;
    }

    public class CodeGenSerializerHierarchyMiddlePayload
        : CodeGenSerializerHierarchyBasePayload, IBinarySerializable
    {
        public int MiddleValue;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(BaseValue);
            buffer.WriteInt(MiddleValue);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            BaseValue = buffer.ReadInt();
            MiddleValue = buffer.ReadInt();
        }
    }

    public sealed class CodeGenSerializerHierarchyLeafPayload
        : CodeGenSerializerHierarchyMiddlePayload
    {
        public int LeafValue;

        public new void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(BaseValue);
            buffer.WriteInt(MiddleValue);
            buffer.WriteInt(LeafValue);
        }

        public new void Deserialize(ByteBuffer buffer)
        {
            BaseValue = buffer.ReadInt();
            MiddleValue = buffer.ReadInt();
            LeafValue = buffer.ReadInt();
        }
    }

    public partial class CodeGenSerializerNestedGeneratedOnlyContainer
    {
        public NestedPayload Payload;

        [GenerateSerializer]
        public partial class NestedPayload
        {
            public int Value;
        }
    }

    public partial class CodeGenSnapshotUnityContainerSample : MonoBehaviour
    {
        public Bounds Bounds;
        public LayerMask Mask;
        public Dictionary<string, Vector3> Points;
        public HashSet<int> Ids;
        public Queue<Vector2Int> Cells;
        public Stack<RectInt> Areas;
    }

    public partial class CodeGenSnapshotNestedGeneratedOnlySample : MonoBehaviour
    {
        public CodeGenSnapshotNestedGeneratedOnlyOuter.NestedPayload Payload;
    }

    public sealed class CodeGenSnapshotNestedGeneratedOnlyOuter
    {
        [GenerateSerializer]
        public partial class NestedPayload
        {
            public int Value;
        }
    }

    public partial class CodeGenSnapshotSerializeReferenceSample : MonoBehaviour
    {
        [SerializeReference]
        public CodeGenSnapshotManagedBase Managed;

        [SerializeReference]
        public CodeGenSnapshotGenericManagedBase<int> GenericManaged;

        [SerializeReference]
        public CodeGenSnapshotConcreteManagedBase ConcreteManaged;

        [SerializeReference]
        public List<CodeGenSnapshotConcreteManagedBase> ConcreteManagedList;
    }

    public partial class CodeGenSnapshotOpenGenericSample<T> : MonoBehaviour
    {
        public T Value;
    }

    public abstract class CodeGenSnapshotManagedBase
    {
        public int Value;
    }

    public sealed class CodeGenSnapshotManagedConcrete
        : CodeGenSnapshotManagedBase, IBinarySerializable
    {
        public string Name;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Value);
            buffer.WriteString(Name);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Value = buffer.ReadInt();
            Name = buffer.ReadString();
        }
    }

    public abstract class CodeGenSnapshotGenericManagedBase<T>
    {
        public T Value;
    }

    public sealed class CodeGenSnapshotGenericManagedInt
        : CodeGenSnapshotGenericManagedBase<int>, IBinarySerializable
    {
        public int Extra;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Value);
            buffer.WriteInt(Extra);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Value = buffer.ReadInt();
            Extra = buffer.ReadInt();
        }
    }

    public class CodeGenSnapshotConcreteManagedBase : IBinarySerializable
    {
        public int BaseValue;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(BaseValue);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            BaseValue = buffer.ReadInt();
        }
    }

    public sealed class CodeGenSnapshotConcreteManagedDerived : CodeGenSnapshotConcreteManagedBase
    {
        public int DerivedValue;

        public new void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(BaseValue);
            buffer.WriteInt(DerivedValue);
        }

        public new void Deserialize(ByteBuffer buffer)
        {
            BaseValue = buffer.ReadInt();
            DerivedValue = buffer.ReadInt();
        }
    }

    [GenerateDeepCopy]
    public partial class CodeGenDeepCopyContainerSample
    {
        public Dictionary<string, int> Scores;
        public HashSet<int> Ids;
        public Queue<string> Names;
        public Stack<int> History;
    }

    [GenerateDeepCopy]
    public partial class CodeGenDeepCopyOpenGenericSample<T>
    {
        public T Value;
    }

    public class CodeGenDeepCopyDuplicateBaseSample
    {
        public int Value;
    }

    public partial class CodeGenDeepCopyDuplicateDerivedSample : CodeGenDeepCopyDuplicateBaseSample
    {
        public new int Value;
        public int Other;
    }

    [GeneratePacketCodec(960002)]
    internal sealed class CodeGenInternalCodecPacket : Packet, IBinarySerializable
    {
        public override int Id => 960002;

        public override void Clear()
        {
        }

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(1);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            buffer.ReadInt();
        }
    }

    public sealed class CodeGenPacketHandlerOuter
    {
        public sealed class NestedHandler : IPacketHandler
        {
            public int PacketId => 910001;
            public void Handle(Packet packet, INetworkChannel channel) { }
        }
    }

    public sealed class CodeGenOpenGenericHandler<T> : IPacketHandler
    {
        public int PacketId => 910002;
        public void Handle(Packet packet, INetworkChannel channel) { }
    }

    public partial class CodeGenEventArgsSample : FrameworkEventArgs
    {
        public string Name { get; private set; }

        public string this[int index]
        {
            get { return Name; }
            set { Name = value; }
        }

        public override void Clear()
        {
            Name = null;
        }
    }

    public sealed class CodeGenEventArgsOuter
    {
        public partial class NestedEventArgs : FrameworkEventArgs
        {
            public override void Clear() { }
        }
    }

    public partial class CodeGenGenericEventArgs<T> : FrameworkEventArgs
    {
        public override void Clear() { }
    }

    public sealed class CodeGenNonFrameworkEventArgs
    {
    }

    namespace CodeGenFileNameA
    {
        public sealed class DuplicateName
        {
        }
    }

    namespace CodeGenFileNameB
    {
        public sealed class DuplicateName
        {
        }
    }

    public static class CodeGenGeneratorTestCli
    {
        public static void RunPolymorphicSmoke()
        {
            try
            {
                var tests = new CodeGenGeneratorTests();
                tests.CodeGenTypeUtil_ResolvesGeneratedPathForUnityDefaultAssembly();
                tests.CodeGenTypeUtil_SkipsUnknownNoAsmdefAssemblies();
                tests.Serializer_WritesPolymorphicCompositeMembers();
                tests.Serializer_UsesExactRuntimeTypeForPolymorphicDiscriminator();
                tests.Serializer_SkipsPolymorphicCandidatesOutsideAsmdefReferenceGraph();
                tests.MonoBehaviourSnapshot_WritesSerializeReferencePolymorphicFields();
                Debug.Log("CodeGen polymorphic smoke passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

    }
}
